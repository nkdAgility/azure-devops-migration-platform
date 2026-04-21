using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

/// <summary>
/// Orchestrates work item link analysis across all configured organisations and sources.
/// Dispatches to the appropriate per-source implementation (ADO, TFS, Simulated) based on organisation type.
/// </summary>
public sealed class DependencyDiscoveryService : IDependencyDiscoveryService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DependencyDiscoveryService> _logger;
    private readonly ICatalogService _catalogService;

    public DependencyDiscoveryService(
        IOptions<DiscoveryOptions> options,
        IServiceProvider serviceProvider,
        ICatalogService catalogService,
        ILogger<DependencyDiscoveryService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers external work item links across all enabled organisations.
    /// Yields DependencyProgressEvent records (found events and heartbeats) for each organisation.
    /// Results from all organisations are streamed sequentially.
    /// </summary>
    /// <param name="completedProjectKeys">
    /// Optional set of project keys (<c>"orgUrl|projectName"</c>) that have already been
    /// fully analysed in a previous run. These projects are skipped entirely — no API calls.
    /// </param>
    /// <param name="wiqlFilter">Optional WIQL expression to filter the set of work items to analyse.</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation.</param>
    public async IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
        HashSet<string>? completedProjectKeys = null,
        string? wiqlFilter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting dependency discovery across {OrganisationCount} organisations", _options.Value.Organisations.Count);

        foreach (var organisation in _options.Value.Organisations)
        {
            var orgEndpointForLog = organisation.ToEndpointOptions();
            if (!organisation.Enabled)
            {
                _logger.LogInformation("Skipping disabled organisation: {Url}", orgEndpointForLog.GetResolvedUrl());
                continue;
            }

            _logger.LogInformation("Analysing organisation: {Url}, type: {Type}", orgEndpointForLog.GetResolvedUrl(), organisation.Type);

            // Resolve the per-source implementation by keyed DI
            var key = organisation.Type ?? "Unknown";
            var service = _serviceProvider.GetKeyedService<IWorkItemLinkAnalysisService>(key);

            if (service == null)
            {
                var errorMsg = key switch
                {
                    "Simulated" => "Simulated source not yet implemented — add in Phase 4",
                    "TeamFoundationServer" => "TFS source requires TfsDependencyProcessAdapter — registered in CLI host only",
                    _ => $"No implementation registered for source type '{key}'"
                };

                _logger.LogError("Service not available for {SourceType}: {ErrorMessage}", key, errorMsg);
                throw new NotSupportedException(errorMsg);
            }

            // Determine which projects to analyse
            var projectsToAnalyse = organisation.Projects;
            if (projectsToAnalyse.Count == 0)
            {
                _logger.LogInformation("Projects list is empty, fetching all projects from {Url}", orgEndpointForLog.GetResolvedUrl());
                var endpoint = orgEndpointForLog;
                try
                {
                    projectsToAnalyse = (await _catalogService.GetProjectsAsync(
                        endpoint,
                        cancellationToken)).ToList();
                    _logger.LogInformation("Found {ProjectCount} projects in {Url}", projectsToAnalyse.Count, orgEndpointForLog.GetResolvedUrl());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch projects from {Url}", orgEndpointForLog.GetResolvedUrl());
                    throw;
                }
            }

            // Analyse each project in the organisation
            foreach (var project in projectsToAnalyse)
            {
                var orgEndpoint = orgEndpointForLog;
                var projectKey = $"{orgEndpoint.GetResolvedUrl()}|{project}";

                if (completedProjectKeys?.Contains(projectKey) == true)
                {
                    _logger.LogInformation("Skipping completed project: {OrgUrl} / {Project}", orgEndpoint.GetResolvedUrl(), project);
                    // Emit a heartbeat so the CLI/TUI can display the skip
                    yield return new DependencyHeartbeatEvent(
                        orgEndpoint.GetResolvedUrl(), project, 0, 0, 0, 0, true,
                        TotalWorkItems: 0, SkippedWorkItems: 0);
                    continue;
                }

                _logger.LogInformation("Analysing project {Project} in {Url}", project, orgEndpoint.GetResolvedUrl());

                // Stream events from the service
                await foreach (var evt in service.AnalyseLinksAsync(
                    orgEndpoint,
                    project,
                    wiqlFilter,
                    cancellationToken))
                {
                    yield return evt;
                }
            }
        }

        _logger.LogInformation("Dependency discovery completed");
    }
}

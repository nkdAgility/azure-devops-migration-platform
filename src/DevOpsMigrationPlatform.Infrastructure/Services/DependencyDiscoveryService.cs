using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public DependencyDiscoveryService(
        IOptions<DiscoveryOptions> options,
        IServiceProvider serviceProvider,
        ILogger<DependencyDiscoveryService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers external work item links across all enabled organisations.
    /// Yields DependencyProgressEvent records (found events and heartbeats) for each organisation.
    /// Results from all organisations are streamed sequentially.
    /// </summary>
    public async IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
        string? wiqlFilter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting dependency discovery across {OrganisationCount} organisations", _options.Value.Organisations.Count);

        foreach (var organisation in _options.Value.Organisations)
        {
            if (!organisation.Enabled)
            {
                _logger.LogInformation("Skipping disabled organisation: {Url}", organisation.ResolvedUrl);
                continue;
            }

            _logger.LogInformation("Analysing organisation: {Url}, type: {Type}", organisation.ResolvedUrl, organisation.Type);

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

            // Analyse each project in the organisation
            foreach (var project in organisation.Projects)
            {
                _logger.LogInformation("Analysing project {Project} in {Url}", project, organisation.ResolvedUrl);

                var pat = organisation.Authentication?.ResolvedAccessToken ?? "";

                // Stream events from the service
                await foreach (var evt in service.AnalyseLinksAsync(
                    organisation.ResolvedUrl,
                    project,
                    pat,
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

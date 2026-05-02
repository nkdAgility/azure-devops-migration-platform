using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin inventory discovery module for standalone <c>JobKind.Inventory</c> jobs.
/// Resolves multi-org configuration and delegates all orchestration to
/// <see cref="InventoryOrchestrator"/>.
/// </summary>
public sealed class InventoryDiscoveryModule : IModule
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly IInventoryServiceFactory _inventoryFactory;
    private readonly ILogger<InventoryDiscoveryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;
    private readonly IOptions<DiscoveryOptions>? _discoveryOptions;
    private readonly ISourceEndpointInfo? _sourceEndpointInfo;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "InventoryDiscovery";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsImport => false;

    public InventoryDiscoveryModule(
        IInventoryServiceFactory inventoryFactory,
        IServiceProvider serviceProvider,
        ILogger<InventoryDiscoveryModule> logger,
        IDiscoveryMetrics? metrics = null,
        IOptions<DiscoveryOptions>? discoveryOptions = null,
        ISourceEndpointInfo? sourceEndpointInfo = null)
    {
        _inventoryFactory = inventoryFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _metrics = metrics;
        _discoveryOptions = discoveryOptions;
        _sourceEndpointInfo = sourceEndpointInfo;
    }

    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("discovery.inventory.multi-org", ActivityKind.Internal);
        activity?.SetTag("job.id", context.Job.JobId);

        // Resolve multi-org organisations from context or config.
        List<ScopedOrganisationEndpoint> orgs;
        if (context.Organisations.Count > 0)
        {
            orgs = context.Organisations.ToList();
        }
        else if (_discoveryOptions?.Value?.Organisations is { Count: > 0 } discoveryOrgs)
        {
            orgs = discoveryOrgs
                .Where(o => o.Enabled)
                .Select(o => new ScopedOrganisationEndpoint
                {
                    Endpoint = o.ToEndpointOptions(),
                    Projects = new List<string>(o.Projects),
                    Scopes = o.Scopes.Select(s => new JobModuleScope
                    {
                        Type = s.Type,
                        Parameters = s.Parameters.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
                    }).ToList()
                })
                .ToList<ScopedOrganisationEndpoint>();
        }
        else if (_sourceEndpointInfo is not null)
        {
            _logger.LogInformation(
                "No organisations configured — falling back to source connector ({ConnectorType}).",
                _sourceEndpointInfo.ConnectorType);
            orgs = new List<ScopedOrganisationEndpoint>();
        }
        else
        {
            _logger.LogWarning("InventoryDiscoveryModule has no source endpoint and no organisations — skipping.");
            return;
        }

        var connectorType = _sourceEndpointInfo?.ConnectorType ?? string.Empty;
        var factory = (!string.IsNullOrEmpty(connectorType)
            ? _serviceProvider.GetKeyedService<IInventoryServiceFactory>(connectorType)
            : null) ?? _inventoryFactory;

        var policies = _discoveryOptions?.Value?.Policies is { } p
            ? new JobPolicies { MaxRetries = p.Retries.Max, MaxConcurrency = p.Throttle.MaxConcurrency, CheckpointIntervalSeconds = p.Checkpoints.Interval }
            : new JobPolicies();

        var inventoryService = factory.Create(orgs, policies);

        // Load completed keys for resume support.
        var completedKeys = await InventoryOrchestrator.LoadCompletedKeysAsync(
            context.ArtefactStore, context.StateStore, ct).ConfigureAwait(false);

        // Get the event stream — single-endpoint fallback or multi-org.
        IAsyncEnumerable<InventoryProgressEvent> eventStream;
        if (orgs.Count == 0 && _sourceEndpointInfo is not null)
        {
            var endpoint = _sourceEndpointInfo.ToOrganisationEndpoint();
            eventStream = inventoryService.RunInventoryAsync(endpoint, projects: null, completedKeys, ct);
        }
        else
        {
            eventStream = inventoryService.RunInventoryAsync(completedKeys, ct);
        }

        // Delegate all orchestration to the shared orchestrator.
        var checkpointInterval = _discoveryOptions?.Value?.Policies?.Checkpoints?.Interval ?? 300;
        var orchestrator = new InventoryOrchestrator(_logger, _metrics);
        await orchestrator.RunAsync(
            Name,
            eventStream,
            context,
            orgs,
            checkpointInterval,
            ct).ConfigureAwait(false);
    }

    public Task ImportAsync(ImportContext context, CancellationToken ct)
        => throw new NotSupportedException("InventoryDiscoveryModule does not support import.");

    public Task ValidateAsync(ValidationContext context, CancellationToken ct)
        => Task.CompletedTask;
}
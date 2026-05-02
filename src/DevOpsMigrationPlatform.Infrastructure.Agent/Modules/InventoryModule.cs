using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin inventory module for Export/Migrate jobs. Pulled in automatically when
/// <see cref="WorkItemsModule"/> declares a dependency on it. Uses the single source
/// endpoint (same as every other module) and delegates orchestration to
/// <see cref="InventoryOrchestrator"/>.
/// </summary>
public sealed class InventoryModule : IModule
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IInventoryServiceFactory _inventoryFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InventoryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;
    private readonly IInventoryOrchestrator _orchestrator;

    public string Name => "Inventory";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsImport => false;

    public InventoryModule(
        ISourceEndpointInfo sourceEndpointInfo,
        IInventoryServiceFactory inventoryFactory,
        IServiceProvider serviceProvider,
        ILogger<InventoryModule> logger,
        IInventoryOrchestrator orchestrator,
        IDiscoveryMetrics? metrics = null)
    {
        _sourceEndpointInfo = sourceEndpointInfo;
        _inventoryFactory = inventoryFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _metrics = metrics;
    }

    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("discovery.inventory.export", ActivityKind.Internal);
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("connector.type", _sourceEndpointInfo.ConnectorType);

        // Resolve endpoint — same mechanism as every other module.
        var endpoint = _sourceEndpointInfo.ToOrganisationEndpoint();

        _logger.LogInformation(
            "InventoryModule starting single-source inventory for job {JobId} ({ConnectorType}).",
            context.Job.JobId, _sourceEndpointInfo.ConnectorType);

        // Resolve the correct keyed InventoryService for this connector.
        var connectorType = _sourceEndpointInfo.ConnectorType;
        var factory = (!string.IsNullOrEmpty(connectorType)
            ? _serviceProvider.GetKeyedService<IInventoryServiceFactory>(connectorType)
            : null) ?? _inventoryFactory;
        var inventoryService = factory.Create(
            new List<ScopedOrganisationEndpoint>(),
            new JobPolicies());

        // Load completed keys for resume support.
        var completedKeys = await InventoryOrchestrator.LoadCompletedKeysAsync(
            context.ArtefactStore, context.StateStore, ct).ConfigureAwait(false);

        // Get the event stream from the single-endpoint overload.
        var eventStream = inventoryService.RunInventoryAsync(
            endpoint, projects: null, completedKeys, ct);

        // Delegate all orchestration to the shared orchestrator.
        await _orchestrator.RunAsync(
            Name,
            eventStream,
            context,
            organisations: Array.Empty<ScopedOrganisationEndpoint>(),
            ct: ct).ConfigureAwait(false);
    }

    public Task ImportAsync(ImportContext context, CancellationToken ct)
        => throw new NotSupportedException("InventoryModule does not support import.");

    public Task ValidateAsync(ValidationContext context, CancellationToken ct)
        => Task.CompletedTask;
}

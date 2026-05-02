#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for team export/import.
/// Delegates all orchestration to <see cref="TeamsOrchestrator"/>, which handles
/// the enumeration loop, checkpointing, progress events, and metrics, and in turn
/// delegates per-team operations to <see cref="TeamExportOrchestrator"/> and
/// <see cref="TeamImportOrchestrator"/>.
/// </summary>
/// <remarks>
/// <strong>Connector coverage:</strong> Team import is supported for
/// <c>AzureDevOpsServices</c> and <c>Simulated</c> connectors only.
/// TFS (TeamFoundationServer) is a <em>source-only</em> connector — it is always
/// the migration origin, never the destination.
/// </remarks>
public sealed class TeamsModule : IModule
{
    private readonly ITeamSource? _teamSource;
    private readonly ITeamTarget? _teamTarget;
    private readonly TeamExportOrchestrator? _exportOrchestrator;
    private readonly TeamImportOrchestrator? _importOrchestrator;
    private readonly TeamSlugGenerator _slugGenerator;
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<TeamsModule> _logger;
    private readonly TeamsModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly TeamsOrchestrator _orchestrator;

    public string Name => "Teams";
    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import)
    };
    public bool SupportsExport => true;
    public bool SupportsImport => true;

    public TeamsModule(
        ILogger<TeamsModule> logger,
        IOptions<TeamsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        TeamSlugGenerator slugGenerator,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null,
        ICheckpointingServiceFactory? checkpointingFactory = null,
        IMigrationMetrics? migrationMetrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _slugGenerator = slugGenerator ?? throw new ArgumentNullException(nameof(slugGenerator));
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _exportOrchestrator = exportOrchestrator;
        _importOrchestrator = importOrchestrator;
        _checkpointingFactory = checkpointingFactory;
        _orchestrator = new TeamsOrchestrator(logger, migrationMetrics);
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping export.");
            return;
        }

        if (_teamSource is null)
        {
            _logger.LogWarning("[Teams] No ITeamSource registered — team export skipped.");
            return;
        }

        if (_exportOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamExportOrchestrator registered — team export skipped.");
            return;
        }

        await _orchestrator.ExportAsync(
            _teamSource, _exportOrchestrator, _slugGenerator,
            context, _sourceEndpointInfo, _checkpointingFactory,
            _options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping import.");
            return;
        }

        if (_teamTarget is null)
        {
            _logger.LogWarning("[Teams] No ITeamTarget registered — team import skipped.");
            return;
        }

        if (_importOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamImportOrchestrator registered — team import skipped.");
            return;
        }

        await _orchestrator.ImportAsync(
            _importOrchestrator, context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);
    }
}
#endif

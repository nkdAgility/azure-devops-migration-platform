// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
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
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<TeamsModule> _logger;
    private readonly TeamsModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly ITeamsOrchestrator _orchestrator;

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
        ITeamsOrchestrator orchestrator,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _checkpointingFactory = checkpointingFactory;
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

        await _orchestrator.ExportAsync(
            _teamSource, context, _sourceEndpointInfo, _checkpointingFactory,
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

        await _orchestrator.ImportAsync(
            context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);
    }
}
#endif

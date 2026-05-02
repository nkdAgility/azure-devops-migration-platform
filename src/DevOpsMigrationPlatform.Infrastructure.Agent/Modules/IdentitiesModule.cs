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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for identity export/import.
/// Delegates all orchestration to <see cref="IdentitiesOrchestrator"/>, which handles
/// JSONL streaming, checkpointing, progress events, and metrics.
/// </summary>
public sealed class IdentitiesModule : IModule
{
    private readonly IIdentitySource? _identitySource;
#if !NET481
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly IMigrationMetrics? _migrationMetrics;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<IdentitiesModule> _logger;
    private readonly IdentitiesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IdentitiesOrchestrator _orchestrator;

    public string Name => "Identities";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsImport => true;

    public IdentitiesModule(
        ILogger<IdentitiesModule> logger,
        IOptions<IdentitiesModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        IIdentitySource? identitySource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null
#if !NET481
        , IIdentityLookupTool? identityLookupTool = null
        , IMigrationMetrics? migrationMetrics = null
#endif
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _identitySource = identitySource;
        _checkpointingFactory = checkpointingFactory;
#if !NET481
        _identityLookupTool = identityLookupTool;
        _migrationMetrics = migrationMetrics;
        _orchestrator = new IdentitiesOrchestrator(logger, migrationMetrics);
#else
        _orchestrator = new IdentitiesOrchestrator(logger);
#endif
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping export.");
            return;
        }

        if (_identitySource is null)
        {
            _logger.LogWarning("[Identities] No IIdentitySource registered — identity export skipped.");
            return;
        }

        await _orchestrator.ExportAsync(
            _identitySource, context, _sourceEndpointInfo.Project,
            _checkpointingFactory, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[Identities] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping import.");
            return;
        }

        await _orchestrator.ImportAsync(
            _identityLookupTool, context, _checkpointingFactory, ct).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(
            context.ArtefactStore, context,
#if !NET481
            _migrationMetrics,
#endif
            ct).ConfigureAwait(false);
    }
}

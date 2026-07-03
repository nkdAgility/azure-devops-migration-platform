// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
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
    private static readonly ActivitySource MigrationActivity = new(WellKnownActivitySourceNames.Migration);

    private readonly IIdentitySource? _identitySource;
    // FR-018: no DI-hiding guard. The optional tool is null on net481 (not registered);
    // import is skipped there via the capability branch in ImportAsync.
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<IdentitiesModule> _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly IdentitiesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IIdentitiesOrchestrator _orchestrator;

    public string Name => "Identities";

    /// <inheritdoc cref="IModule.Contract"/>
    private static readonly IModuleContract IdentitiesContract = new ModuleContract(
        moduleName: "Identities",
        selection: [],
        data: [new DataDefinition("Identities", Required: true)],
        processing: [new ProcessingDefinition("DefaultIdentity", Required: false)]);

    /// <inheritdoc cref="IModule.Contract"/>
    public IModuleContract Contract => IdentitiesContract;
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

    public IdentitiesModule(
        ILogger<IdentitiesModule> logger,
    IOptions<IdentitiesModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        IIdentitiesOrchestrator orchestrator,
        IPlatformMetrics? PlatformMetrics = null,
        IIdentitySource? identitySource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null,
        IIdentityTranslationTool? identityTranslationTool = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _PlatformMetrics = PlatformMetrics;
        _identitySource = identitySource;
        _checkpointingFactory = checkpointingFactory;
        _identityTranslationTool = identityTranslationTool;
    }

    public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        => _orchestrator.CaptureAsync(_identitySource, context, _sourceEndpointInfo.Url, ct);

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.identities");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Preparing {Module}", Name);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Preparing",
            Message = $"Preparing {Name}",
            Timestamp = DateTimeOffset.UtcNow
        });

        // Delegate Prepare-phase identity resolution (UPN/display-name matching against the
        // target tenant, cache population, and prepare-report.json) to the orchestrator.
        var (organisation, project) = ResolvePrepareScope(context);
        await _orchestrator.PrepareAsync(context, organisation, project, ct).ConfigureAwait(false);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Prepared",
            Message = $"{Name} prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });

        return TaskExecutionResult.Completed();
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping export.");
            return TaskExecutionResult.Skipped("Identities module disabled for export.");
        }

        if (_identitySource is null)
        {
            _logger.LogWarning("[Identities] No IIdentitySource registered — identity export skipped.");
            return TaskExecutionResult.Skipped("No identity source registered.");
        }

        await _orchestrator.ExportAsync(
            _identitySource, context, _sourceEndpointInfo.OrganisationSlug, _sourceEndpointInfo.Project,
            _checkpointingFactory, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        _logger.LogDebug("[Identities] Import not supported on net481 — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
        return TaskExecutionResult.Skipped("Identities import is not supported on net481.");
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping import.");
            return TaskExecutionResult.Skipped("Identities module disabled for import.");
        }

        var organisation = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = "unknown";
        }

        var project = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            project = "unknown";
        }

        await _orchestrator.ImportAsync(
            _identityTranslationTool, context, organisation, project, _checkpointingFactory, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
#endif
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(
            context.Package,
            _sourceEndpointInfo.OrganisationSlug,
            _sourceEndpointInfo.Project,
            context,
            ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

    private static async Task PersistPackageTextAsync(IPackageAccess package, PackageContentContext context, string content, CancellationToken ct)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistContentAsync(context, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
    }

    private (string Organisation, string Project) ResolvePrepareScope(PrepareContext context)
    {
        var organisation = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = !string.IsNullOrWhiteSpace(context.TargetEndpoint.Url)
                ? PackagePathResolver.DeriveInventoryOrgSlug(context.TargetEndpoint.Url)
                : PackagePathResolver.Sanitise((context.TargetEndpoint.ConnectorType ?? "unknown").ToLowerInvariant());
        }

        var project = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            project = context.TargetEndpoint.Project;
        }

        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = "unknown";
        }
        if (string.IsNullOrWhiteSpace(project))
        {
            project = "unknown";
        }

        return (organisation, project);
    }
}

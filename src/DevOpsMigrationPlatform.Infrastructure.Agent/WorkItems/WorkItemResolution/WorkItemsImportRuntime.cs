// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Default WorkItems import orchestration.
/// </summary>
public sealed class WorkItemsImportRuntime
{
    private readonly IWorkItemTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IWorkItemResolutionProcessorFactory _processorFactory;
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly IWorkItemsImportCapabilityValidator _capabilityValidator;
    private readonly IWorkItemsNodeReadinessOrchestrator _nodeReadinessOrchestrator;
    private readonly IPlatformMetrics? _metrics;
    private readonly ILogger<WorkItemsImportRuntime> _orchestratorLogger;
    private readonly ILogger _logger;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly IOptions<WorkItemsModuleOptions> _moduleOptions;
    private readonly IOptions<WorkItemOptions>? _workItemImportOptions;
    private readonly IOptions<NodesModuleOptions>? _nodesModuleOptions;
    private readonly WorkItemStreamOrchestrator? _streamOrchestrator;

    public WorkItemsImportRuntime(
        IWorkItemTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IWorkItemResolutionProcessorFactory processorFactory,
        IIdentityLookupTool? identityLookupTool,
        IWorkItemsImportCapabilityValidator capabilityValidator,
        IWorkItemsNodeReadinessOrchestrator nodeReadinessOrchestrator,
        IPlatformMetrics? metrics,
        ILogger<WorkItemsImportRuntime> orchestratorLogger,
        ILogger logger,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        IOptions<WorkItemsModuleOptions> moduleOptions,
        IOptions<WorkItemOptions>? workItemImportOptions = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null)
    {
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _identityLookupTool = identityLookupTool;
        _capabilityValidator = capabilityValidator ?? throw new ArgumentNullException(nameof(capabilityValidator));
        _nodeReadinessOrchestrator = nodeReadinessOrchestrator ?? throw new ArgumentNullException(nameof(nodeReadinessOrchestrator));
        _metrics = metrics;
        _orchestratorLogger = orchestratorLogger ?? throw new ArgumentNullException(nameof(orchestratorLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _moduleOptions = moduleOptions ?? throw new ArgumentNullException(nameof(moduleOptions));
        _workItemImportOptions = workItemImportOptions;
        _nodesModuleOptions = nodesModuleOptions;
        _streamOrchestrator = null;
    }

    public WorkItemsImportRuntime(
        IPackageAccess package,
        string organisation,
        string project,
        ICheckpointingService checkpointing,
        IProgressSink progressSink,
        IWorkItemResolutionStrategy resolutionStrategy,
        IIdMapStore idMapStore,
        IWorkItemResolutionProcessor processor,
        IWorkItemTarget target,
        ILogger<WorkItemsImportRuntime> logger,
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null,
        IPlatformMetrics? metrics = null,
        string? jobId = null)
    {
        _importTargetFactory = null!;
        _resolutionStrategyFactory = null!;
        _checkpointingFactory = null!;
        _idMapStoreFactory = null!;
        _processorFactory = null!;
        _identityLookupTool = null;
        _capabilityValidator = null!;
        _nodeReadinessOrchestrator = null!;
        _metrics = metrics;
        _orchestratorLogger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger = logger;
        _sourceEndpointInfo = null!;
        _targetEndpointInfo = null!;
        _moduleOptions = null!;
        _workItemImportOptions = null;
        _nodesModuleOptions = null;
        _streamOrchestrator = new WorkItemStreamOrchestrator(
            package,
            organisation,
            project,
            checkpointing,
            progressSink,
            resolutionStrategy,
            idMapStore,
            processor,
            target,
            logger,
            filterOptions,
            metrics,
            jobId);
    }

    public Task ImportAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
    {
        if (_streamOrchestrator is null)
            throw new InvalidOperationException("This WorkItemsImportRuntime instance was not constructed for streaming import.");

        return _streamOrchestrator.ImportAsync(ext, resumeMode, ct);
    }

    public Task<TaskExecutionResult> ExecuteAsync(ImportContext context, CancellationToken ct)
    {
        return ImportAsync(context, ct);
    }

    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        var job = context.Job;
        _capabilityValidator.Validate();

        var orgUrl = _targetEndpointInfo.Url;
        var project = _targetEndpointInfo.Project;
        var startupPolicy = AssembleStartupPolicy(job);
        var ext = startupPolicy.Extensions;
        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "StartupPolicy",
            Message = "Assembled WorkItems startup policy.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
            orgUrl, project, ext.RevisionsEnabled, ext.LinksEnabled, ext.AttachmentsEnabled, ext.Comments.Enabled);

        var target = await _importTargetFactory.CreateAsync(ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.Package);

        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, _targetEndpointInfo, ct)
            .ConfigureAwait(false);

        var idMapConnection = await context.Package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, ct).ConfigureAwait(false);
        var idMapStore = _idMapStoreFactory.Create(idMapConnection);

        var sourceProjectName = _sourceEndpointInfo.Project;
        var nodeReadinessContext = new ProjectMapping(sourceProjectName, project);
        var replicateSourceTree = _nodesModuleOptions?.Value.ReplicateSourceTree ?? false;
        await _nodeReadinessOrchestrator
            .EnsureReadyAsync(nodeReadinessContext, replicateSourceTree, context, _sourceEndpointInfo.OrganisationSlug, sourceProjectName, ct)
            .ConfigureAwait(false);
        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "NodeReadiness",
            Message = "Completed WorkItems node readiness checks.",
            Timestamp = DateTimeOffset.UtcNow
        });

        var nodeStructureContext = new ProjectMapping(sourceProjectName, project);
        var processor = _processorFactory.Create(
            target,
            idMapStore,
            checkpointingService,
            _identityLookupTool,
            _sourceEndpointInfo.OrganisationSlug,
            sourceProjectName,
            nodeStructureContext);

        var importFilters = startupPolicy.ImportFilters;
        var orchestrator = new WorkItemsImportRuntime(
            context.Package,
            _sourceEndpointInfo.OrganisationSlug,
            sourceProjectName,
            checkpointingService,
            context.ProgressSink,
            resolutionStrategy,
            idMapStore,
            processor,
            target,
            _orchestratorLogger,
            filterOptions: importFilters.Count > 0 ? importFilters : null,
            metrics: _metrics,
            jobId: job.JobId);
        var revisionImporter = new WorkItemRevisionImporter(orchestrator);

        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "RevisionDispatch",
            Message = "Dispatching revision import.",
            Timestamp = DateTimeOffset.UtcNow
        });
        await revisionImporter.ExecuteAsync(ext, startupPolicy.ResumeMode, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Import complete.");
        return TaskExecutionResult.Completed();
    }

    private WorkItemsModuleExtensions ApplyImportReplayLevers(WorkItemsModuleExtensions ext)
    {
        if (_workItemImportOptions is null)
            return ext;

        var replayOptions = _workItemImportOptions.Value;
        var hasExplicitLeverConfig =
            replayOptions.RevisionReplay ||
            replayOptions.LinkReplay ||
            replayOptions.AttachmentReplay ||
            replayOptions.EmbeddedImageReplay ||
            replayOptions.FieldTransform;

        if (!hasExplicitLeverConfig)
            return ext;

        var attachmentsEnabled = ext.AttachmentsEnabled &&
                                 (!replayOptions.RevisionReplay || replayOptions.AttachmentReplay);
        var linksEnabled = ext.LinksEnabled &&
                           (!replayOptions.RevisionReplay || replayOptions.LinkReplay);
        var embeddedImagesEnabled = ext.EmbeddedImages.Enabled &&
                                    (!replayOptions.RevisionReplay || replayOptions.EmbeddedImageReplay);

        return new WorkItemsModuleExtensions
        {
            Query = ext.Query,
            RevisionsEnabled = ext.RevisionsEnabled,
            LinksEnabled = linksEnabled,
            AttachmentsEnabled = attachmentsEnabled,
            Comments = ext.Comments,
            EmbeddedImages = new EmbeddedImagesExtensionOptionsConfig
            {
                Enabled = embeddedImagesEnabled,
                DownloadTimeoutSeconds = ext.EmbeddedImages.DownloadTimeoutSeconds
            },
            ResolutionStrategy = ext.ResolutionStrategy,
            IncludeFilters = ext.IncludeFilters,
            ExcludeFilters = ext.ExcludeFilters
        };
    }

    private ImportStartupPolicy AssembleStartupPolicy(Job job)
    {
        var extensions = ApplyImportReplayLevers(WorkItemsModuleExtensions.FromOptions(_moduleOptions.Value));
        var importFilters = extensions.IncludeFilters.Concat(extensions.ExcludeFilters).ToList();
        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        return new ImportStartupPolicy(
            extensions,
            importFilters,
            resumeMode);
    }

    private sealed record ImportStartupPolicy(
        WorkItemsModuleExtensions Extensions,
        System.Collections.Generic.List<WorkItemFieldFilterOptions> ImportFilters,
        ResumeMode ResumeMode);
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Default WorkItems import orchestration extracted from WorkItemsModule.
/// </summary>
public sealed class WorkItemsImportOrchestrator : IWorkItemsImportOrchestrator
{
    private readonly IWorkItemImportTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IRevisionFolderProcessorFactory _processorFactory;
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly IWorkItemsImportCapabilityValidator _capabilityValidator;
    private readonly IWorkItemsNodeReadinessOrchestrator _nodeReadinessOrchestrator;
    private readonly IPlatformMetrics? _metrics;
    private readonly ILogger<WorkItemImportOrchestrator> _orchestratorLogger;
    private readonly ILogger _logger;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly IOptions<WorkItemsModuleOptions> _moduleOptions;
    private readonly IOptions<WorkItemImportOptions>? _workItemImportOptions;
    private readonly IOptions<NodesModuleOptions>? _nodesModuleOptions;

    public WorkItemsImportOrchestrator(
        IWorkItemImportTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IRevisionFolderProcessorFactory processorFactory,
        IIdentityLookupTool? identityLookupTool,
        IWorkItemsImportCapabilityValidator capabilityValidator,
        IWorkItemsNodeReadinessOrchestrator nodeReadinessOrchestrator,
        IPlatformMetrics? metrics,
        ILogger<WorkItemImportOrchestrator> orchestratorLogger,
        ILogger logger,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        IOptions<WorkItemsModuleOptions> moduleOptions,
        IOptions<WorkItemImportOptions>? workItemImportOptions = null,
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
    }

    public async Task<TaskExecutionResult> ExecuteAsync(ImportContext context, CancellationToken ct)
    {
        var job = context.Job;
        _capabilityValidator.Validate();

        var orgUrl = _targetEndpointInfo.Url;
        var project = _targetEndpointInfo.Project;
        var ext = ApplyImportReplayLevers(WorkItemsModuleExtensions.FromOptions(_moduleOptions.Value));

        _logger.LogInformation(
            "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
            orgUrl, project, ext.RevisionsEnabled, ext.LinksEnabled, ext.AttachmentsEnabled, ext.Comments.Enabled);

        var target = await _importTargetFactory.CreateAsync(ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.Package);

        var targetEndpointOptions = new SimulatedEndpointOptions
        {
            Type = _targetEndpointInfo.ConnectorType,
            Url = _targetEndpointInfo.Url,
            Project = _targetEndpointInfo.Project
        };

        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, targetEndpointOptions, ct)
            .ConfigureAwait(false);

        var idMapConnection = await context.Package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, ct).ConfigureAwait(false);
        var idMapStore = _idMapStoreFactory.Create(idMapConnection);

        var sourceProjectName = _sourceEndpointInfo.Project;
        var nodeReadinessContext = new ProjectMapping(sourceProjectName, project);
        var replicateSourceTree = _nodesModuleOptions?.Value.ReplicateSourceTree ?? false;
        await _nodeReadinessOrchestrator
            .EnsureReadyAsync(nodeReadinessContext, replicateSourceTree, context, _sourceEndpointInfo.OrganisationSlug, sourceProjectName, ct)
            .ConfigureAwait(false);

        var nodeStructureContext = new ProjectMapping(sourceProjectName, project);
        var processor = _processorFactory.Create(
            target,
            idMapStore,
            checkpointingService,
            _identityLookupTool,
            _sourceEndpointInfo.OrganisationSlug,
            sourceProjectName,
            nodeStructureContext);

        var importFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();
        var orchestrator = new WorkItemImportOrchestrator(
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

        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        await revisionImporter.ExecuteAsync(ext, resumeMode, ct).ConfigureAwait(false);

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
}


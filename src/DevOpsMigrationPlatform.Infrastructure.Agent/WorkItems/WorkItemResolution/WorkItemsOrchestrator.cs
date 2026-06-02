// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Orchestrates WorkItems export, prepare, import, and validate phases through one symmetric contract.
/// </summary>
public sealed class WorkItemsOrchestrator : IWorkItemsOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IWorkItemExportOrchestratorFactory _exportOrchestratorFactory;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly ILogger<WorkItemsModule> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IWorkItemDiscoveryService? _discoveryService;
    private readonly IExportProgressStoreFactory? _exportProgressStoreFactory;
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly IOptions<WorkItemsModuleOptions> _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ImportPreparer _importPreparer;
    private readonly WorkItemsImportRuntime _importOrchestrator;

    public WorkItemsOrchestrator(
        IWorkItemRevisionSourceFactory sourceFactory,
        IAttachmentBinarySource? attachmentBinarySource,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory,
        IWorkItemFetchService? fetchService,
        IWorkItemExportOrchestratorFactory exportOrchestratorFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        ILogger<WorkItemsModule> logger,
        IPlatformMetrics? metrics,
        IWorkItemDiscoveryService? discoveryService,
        IExportProgressStoreFactory? exportProgressStoreFactory,
        IReferencedPathTracker? referencedPathTracker,
        IOptions<WorkItemsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ImportPreparer importPreparer,
        WorkItemsImportRuntime importOrchestrator)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
        _exportOrchestratorFactory = exportOrchestratorFactory ?? throw new ArgumentNullException(nameof(exportOrchestratorFactory));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _discoveryService = discoveryService;
        _exportProgressStoreFactory = exportProgressStoreFactory;
        _referencedPathTracker = referencedPathTracker;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _importPreparer = importPreparer ?? throw new ArgumentNullException(nameof(importPreparer));
        _importOrchestrator = importOrchestrator ?? throw new ArgumentNullException(nameof(importOrchestrator));
    }

    public async Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.export");

        var job = context.Job;

        var orgUrl = _sourceEndpointInfo.Url;
        var orgSlug = _sourceEndpointInfo.OrganisationSlug;
        var project = _sourceEndpointInfo.Project;

#if !NET481
        var ext = WorkItemsModuleExtensions.FromOptions(_options.Value);

        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={AttachmentsEnabled}, comments={CommentsEnabled})",
                orgUrl, project, ext.AttachmentsEnabled, ext.Comments.Enabled);
        }

        if (ext.AttachmentsEnabled && _attachmentBinarySource == null)
            _logger.LogWarning("[WorkItems] AttachmentsEnabled is true but no IAttachmentBinarySource is registered — attachment binaries will NOT be written to the package. Register a connector-specific IAttachmentBinarySource to enable attachment export.");
        if (ext.Comments.Enabled && _inlineCommentSourceFactory == null)
            _logger.LogWarning("[WorkItems] Comments.Enabled is true but no IWorkItemCommentSourceFactory is registered — inline comments will NOT be exported. Register a connector-specific IWorkItemCommentSourceFactory to enable comment export.");

        var allFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();

        if (allFilters.Count > 0 && _fetchService == null)
            _logger.LogWarning("[WorkItems] IncludeFilters/ExcludeFilters are configured but no IWorkItemFetchService is registered — filters will be ignored and all work items will be exported. Register a connector-specific IWorkItemFetchService to enable filtered export.");
#else
        _logger.LogInformation("[WorkItems] Exporting from {OrgUrl}/{Project} (attachments=true, comments=false)", orgUrl, project);
        var wiqlQuery = (string?)null;
        var discoveryService481 = _discoveryService;
        var allFilters = new List<WorkItemFieldFilterOptions>();
#endif

        var source = await _sourceFactory
            .CreateAsync(ct)
            .ConfigureAwait(false);

#if !NET481
        if (_referencedPathTracker is null)
            _logger.LogWarning("[WorkItems] IReferencedPathTracker is not available — referenced path tracking will be skipped.");

        if (_referencedPathTracker is not null)
            await _referencedPathTracker.InitializeAsync(context.Package, orgSlug, project, ct).ConfigureAwait(false);
#endif

        var checkpointingService = _checkpointingFactory.Create(context.Package);

#if !NET481
        var inlineFactory = ext.Comments.Enabled ? _inlineCommentSourceFactory : null;
#else
        var inlineFactory = (IWorkItemCommentSourceFactory?)null;
#endif

        var orchestrator = _exportOrchestratorFactory.Create(
            context.Package,
            orgSlug,
            project,
            checkpointingService,
#if !NET481
            ext.AttachmentsEnabled ? _attachmentBinarySource : null,
#else
            _attachmentBinarySource,
#endif
            context.ProgressSink,
            inlineCommentSourceFactory: inlineFactory,
            fetchService: allFilters.Count > 0 ? _fetchService : null,
            filterOptions: allFilters.Count > 0 ? allFilters : null,
            metrics: _metrics,
            jobId: job.JobId,
            logger: _logger,
            taskId: context.TaskId,
#if !NET481
            wiqlQuery: ext.Query,
            discoveryService: _discoveryService,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: null,
            referencedPathTracker: _referencedPathTracker
#else
            wiqlQuery: wiqlQuery,
            discoveryService: discoveryService481,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: null
#endif
            );

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("prepare.workitems");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", "WorkItems");

        _logger.LogInformation("Preparing {Module}", "WorkItems");
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Preparing",
            Message = "Preparing WorkItems",
            Timestamp = DateTimeOffset.UtcNow
        });

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", "WorkItems" } };
        PrepareReport report;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            report = await _importPreparer.PrepareAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics?.RecordPrepareWorkItemsError(tags);
            _logger.LogError(ex, "[WorkItems] Prepare phase dispatch failed.");
            throw new InvalidOperationException("[WorkItems] Prepare phase dispatch failed.", ex);
        }
        finally
        {
            stopwatch.Stop();
        }

        _metrics?.RecordPrepareWorkItemsResolved(report.ResolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsUnresolved(report.UnresolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsDuration(stopwatch.Elapsed.TotalMilliseconds, tags);
        var org = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(org))
        {
            org = context.TargetEndpoint.OrganisationSlug;
        }

        var project = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            project = context.TargetEndpoint.Project;
        }

        if (string.IsNullOrWhiteSpace(org))
        {
            org = "unknown";
        }
        if (string.IsNullOrWhiteSpace(project))
        {
            project = "unknown";
        }

        await WritePackageTextAsync(
            context.Package,
            new PackageContentContext(PackageContentKind.Artefact, Organisation: org, Project: project, Module: "WorkItems", Address: new RelativePathAddress("prepare-report.json")),
            JsonSerializer.Serialize(report),
            ct).ConfigureAwait(false);
        if (report.ImportReadinessReport is not null)
        {
            using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report.ImportReadinessReport)), writable: false);
            await context.Package.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.WorkItemsImportReadiness),
                new PackageMetaPayload(stream),
                ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Prepared WorkItems: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms",
            report.ResolvedCount,
            report.UnresolvedCount,
            stopwatch.ElapsedMilliseconds);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Prepared",
            Message = "WorkItems prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });

        return TaskExecutionResult.Completed();
    }

    public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        return _importOrchestrator.ImportAsync(context, ct);
    }

    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var job = context.Job;

        if (job.Kind != JobKind.Import)
            return TaskExecutionResult.Skipped("WorkItems package validation applies only to import jobs.");

        var found = false;
        await foreach (var _ in context.Package.EnumerateContentAsync(
                           new PackageContentContext(PackageContentKind.Collection,
                               Organisation: _sourceEndpointInfo.OrganisationSlug,
                               Project: _sourceEndpointInfo.Project,
                               Module: "WorkItems",
                               IsCollectionRequest: true),
                           ct).ConfigureAwait(false))
        {
            found = true;
            break;
        }

        if (!found)
        {
            context.Errors.Add(new ValidationError
            {
                Path = "WorkItems/",
                Message = "The package contains no work item revision folders under WorkItems/. Ensure an export has been run before attempting import."
            });
        }

        return TaskExecutionResult.Completed();
    }

    private static async Task WritePackageTextAsync(IPackageAccess package, PackageContentContext context, string content, CancellationToken cancellationToken)
    {
        using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistContentAsync(context, new PackagePayload(stream, "application/json"), cancellationToken).ConfigureAwait(false);
    }
}

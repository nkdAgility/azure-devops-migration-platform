// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;


namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemRevisionSource"/>.
/// Streams work item revisions in the order returned by
/// <see cref="TfsWorkItemQueryWindowStrategy"/>, loading each work item in full
/// and iterating its revisions in ascending index order.
///
/// Before yielding each revision, all NEW attachment IDs for that revision are
/// registered in <see cref="TfsAttachmentRegistry"/> so that
/// <see cref="TfsAttachmentBinarySource"/> can look them up when the orchestrator
/// calls <c>GetBytesAsync</c> after receiving the revision.
/// </summary>
public sealed class TfsWorkItemRevisionSource : IWorkItemRevisionSource
{
    private readonly WorkItemStore _workItemStore;
    private readonly IWorkItemRevisionMapper _mapper;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;
    private readonly TfsAttachmentRegistry _registry;
    private readonly string _project;
    private readonly string _wiqlQuery;
    private readonly ILogger<TfsWorkItemRevisionSource> _logger;

    public TfsWorkItemRevisionSource(
        WorkItemStore workItemStore,
        IWorkItemRevisionMapper mapper,
        TfsWorkItemQueryWindowStrategy windowStrategy,
        TfsAttachmentRegistry registry,
        string project,
        string wiqlQuery,
        ILogger<TfsWorkItemRevisionSource> logger)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _wiqlQuery = wiqlQuery ?? throw new ArgumentNullException(nameof(wiqlQuery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = new WorkItemQueryWindowOptions { BaseQuery = _wiqlQuery };

        // TFS window strategy ignores the endpoint (WorkItemStore is already authenticated),
        // but the interface requires one. Create a minimal placeholder.
        var tfsEndpoint = new OrganisationEndpoint { Type = "TfsObjectModel" };

        await foreach (var window in _windowStrategy
            .EnumerateWindowsAsync(tfsEndpoint, _project, options, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var workItemId in window.WorkItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WorkItem workItem;
                try
                {
                    workItem = _workItemStore.GetWorkItem(workItemId);
                }
                catch (Exception ex)
                {
                    using (DataClassificationScope.Begin(DataClassification.Customer))
                        _logger.LogError(ex, "Failed to load work item {WorkItemId} — skipping", workItemId);
                    continue;
                }

                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogDebug("Streaming {Count} revisions for work item {WorkItemId}",
                        workItem.Revisions.Count, workItemId);

                Revision? previousRevision = null;

                foreach (Revision revision in workItem.Revisions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Register new attachment IDs BEFORE yielding so that
                    // TfsAttachmentBinarySource.GetBytesAsync can look them up
                    // immediately after the orchestrator receives this revision.
                    RegisterNewAttachments(workItem.Id, revision.Index, revision, previousRevision);

                    WorkItemRevision mapped;
                    try
                    {
                        mapped = _mapper.Map(workItem, revision, previousRevision);
                    }
                    catch (Exception ex)
                    {
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogError(ex,
                                "Failed to map work item {WorkItemId} revision {RevisionIndex} — skipping",
                                workItemId, revision.Index);
                        previousRevision = revision;
                        continue;
                    }

                    previousRevision = revision;
                    yield return mapped;
                }
            }
        }
    }

    private void RegisterNewAttachments(
        int workItemId,
        int revisionIndex,
        Revision revision,
        Revision? previousRevision)
    {
        var newAttachments = revision.Attachments
            .Cast<Attachment>()
            .Where(a =>
                previousRevision == null ||
                !previousRevision.Attachments.Cast<Attachment>().Any(prev => prev.Name == a.Name));

        foreach (var attachment in newAttachments)
        {
            _registry.Register(workItemId, revisionIndex, attachment.Name, attachment.Id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps REST implementation of <see cref="IWorkItemRevisionSource"/>.
/// Discovers work items via <see cref="IWorkItemQueryWindowStrategy"/> (handles the
/// 20,000-item WIQL cap automatically), then streams all revisions for each work item
/// one at a time (no full-load into memory). Registers attachment download URLs in
/// <see cref="AzureDevOpsAttachmentRegistry"/> as each revision is mapped.
/// </summary>
public sealed class AzureDevOpsWorkItemRevisionSource : IWorkItemRevisionSource
{
    private readonly WorkItemTrackingHttpClient _client;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsWorkItemRevisionMapper _mapper;
    private readonly AzureDevOpsAttachmentRegistry _attachmentRegistry;
    private readonly OrganisationEndpoint _endpoint;
    private readonly string _project;
    private readonly string? _wiqlQuery;

    private const int RevisionPageSize = 100;

    public AzureDevOpsWorkItemRevisionSource(
        WorkItemTrackingHttpClient client,
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsWorkItemRevisionMapper mapper,
        AzureDevOpsAttachmentRegistry attachmentRegistry,
        OrganisationEndpoint endpoint,
        string project,
        string? wiqlQuery = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _attachmentRegistry = attachmentRegistry ?? throw new ArgumentNullException(nameof(attachmentRegistry));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _wiqlQuery = wiqlQuery;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. Use the window strategy to enumerate work item IDs in date-scoped windows
        //    that stay under the 20,000-item WIQL cap.  Pass the caller-supplied WIQL
        //    query (if any) so the strategy preserves custom WHERE conditions and ORDER BY.
        var windowOptions = _wiqlQuery != null
            ? new WorkItemQueryWindowOptions { BaseQuery = _wiqlQuery }
            : null;
        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            _endpoint, _project, windowOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            // 2. For each work item in the window, stream its revisions in ascending order.
            foreach (var workItemId in window.WorkItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await foreach (var revision in StreamRevisionsAsync(workItemId, cancellationToken))
                    yield return revision;
            }
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async IAsyncEnumerable<WorkItemRevision> StreamRevisionsAsync(
        int workItemId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int skip = 0;
        WorkItem? previous = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await _client.GetRevisionsAsync(
                workItemId,
                top: RevisionPageSize,
                skip: skip,
                expand: WorkItemExpand.Relations,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (page is null || page.Count == 0)
                yield break;

            foreach (var current in page)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Register attachment download URLs before mapping so the binary
                // source can access them later in the same export run.
                RegisterAttachmentUrls(workItemId, previous, current);

                var mapped = _mapper.Map(current, previous);
                previous = current;

                yield return mapped;
            }

            if (page.Count < RevisionPageSize)
                yield break;   // last page

            skip += page.Count;
        }
    }

    private void RegisterAttachmentUrls(int workItemId, WorkItem? previous, WorkItem current)
    {
        if (current.Relations is null)
            return;

        var prevRelations = previous?.Relations ?? Enumerable.Empty<WorkItemRelation>();
        var revisionIndex = GetRevisionIndex(current);

        foreach (var relation in current.Relations)
        {
            if (!string.Equals(relation.Rel, "AttachedFile", StringComparison.OrdinalIgnoreCase))
                continue;

            // Only register new attachments (not ones carried over from a prior revision).
            if (ExistsInPrevious(relation, prevRelations))
                continue;

            if (relation.Attributes is null)
                continue;

            var name = TryGetAttribute<string>(relation, "name");
            var url = relation.Url;

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                _attachmentRegistry.Register(workItemId, revisionIndex, name, url);
        }
    }

    private static bool ExistsInPrevious(WorkItemRelation current, IEnumerable<WorkItemRelation> prev)
    {
        foreach (var p in prev)
        {
            if (string.Equals(p.Rel, current.Rel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Url, current.Url, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int GetRevisionIndex(WorkItem workItem)
    {
        if (workItem.Rev.HasValue)
            return workItem.Rev.Value - 1;

        if (workItem.Fields != null &&
            workItem.Fields.TryGetValue("System.Rev", out var raw) &&
            raw is IConvertible c)
            return c.ToInt32(null) - 1;

        return 0;
    }

    private static T? TryGetAttribute<T>(WorkItemRelation relation, string key)
    {
        if (relation.Attributes is null || !relation.Attributes.TryGetValue(key, out var value))
            return default;
        try { return (T)Convert.ChangeType(value, typeof(T)); }
        catch { return default; }
    }
}

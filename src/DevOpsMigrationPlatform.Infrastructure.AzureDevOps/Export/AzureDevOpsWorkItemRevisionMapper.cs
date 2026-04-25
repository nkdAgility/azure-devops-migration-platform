using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

using PlatformWorkItemField = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;

/// <summary>
/// Maps a pair of consecutive Azure DevOps REST API <see cref="WorkItem"/> revisions to a
/// <see cref="WorkItemRevision"/>, capturing only the delta (fields and links added in the
/// current revision that were not present in the previous one).
/// </summary>
public interface IAzureDevOpsWorkItemRevisionMapper
{
    /// <summary>
    /// Maps <paramref name="current"/> to a <see cref="WorkItemRevision"/>.
    /// <paramref name="previous"/> is the immediately preceding revision, or <c>null</c> for
    /// revision index 0.
    /// </summary>
    WorkItemRevision Map(WorkItem current, WorkItem? previous);
}

/// <summary>
/// Default implementation of <see cref="IAzureDevOpsWorkItemRevisionMapper"/>.
/// </summary>
public sealed class AzureDevOpsWorkItemRevisionMapper : IAzureDevOpsWorkItemRevisionMapper
{
    private const string AttachedFileRel = "AttachedFile";
    private const string HyperlinkRel = "Hyperlink";
    private const string ArtifactLinkRel = "ArtifactLink";

    public WorkItemRevision Map(WorkItem current, WorkItem? previous)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (!current.Id.HasValue) throw new ArgumentException("WorkItem.Id must have a value.", nameof(current));

        var workItemId = current.Id.Value;
        var revisionIndex = GetRevisionIndex(current);
        var changedDate = GetChangedDate(current);

        var fields = MapFields(current, previous);
        var (externalLinks, relatedLinks, hyperlinks, attachments) = MapRelations(current, previous);

        return new WorkItemRevision
        {
            WorkItemId = workItemId,
            RevisionIndex = revisionIndex,
            ChangedDate = changedDate,
            Fields = fields,
            ExternalLinks = externalLinks,
            RelatedLinks = relatedLinks,
            Hyperlinks = hyperlinks,
            Attachments = attachments
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static int GetRevisionIndex(WorkItem workItem)
    {
        if (workItem.Rev.HasValue)
            return workItem.Rev.Value - 1; // Azure DevOps uses 1-based revision numbers; we use 0-based index

        if (workItem.Fields != null &&
            workItem.Fields.TryGetValue("System.Rev", out var revObj) &&
            revObj is IConvertible c)
            return c.ToInt32(null) - 1;

        return 0;
    }

    private static DateTimeOffset GetChangedDate(WorkItem workItem)
    {
        if (workItem.Fields != null &&
            workItem.Fields.TryGetValue("System.ChangedDate", out var raw))
        {
            if (raw is DateTime dt)
                return new DateTimeOffset(dt, TimeSpan.Zero);
            if (raw is DateTimeOffset dto)
                return dto;
            if (DateTime.TryParse(raw?.ToString(), out var parsed))
                return new DateTimeOffset(parsed, TimeSpan.Zero);
        }

        return DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<PlatformWorkItemField> MapFields(WorkItem current, WorkItem? previous)
    {
        var result = new List<PlatformWorkItemField>();

        if (current.Fields is null)
            return result;

        foreach (var kvp in current.Fields)
        {
            var currentValue = kvp.Value?.ToString();

            // Only include fields that changed relative to the previous revision.
            if (previous?.Fields != null &&
                previous.Fields.TryGetValue(kvp.Key, out var prevValue) &&
                string.Equals(prevValue?.ToString(), currentValue, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(new PlatformWorkItemField
            {
                ReferenceName = kvp.Key,
                Value = currentValue
            });
        }

        return result;
    }

    private static (
        IReadOnlyList<ExternalWorkItemLink> external,
        IReadOnlyList<RelatedWorkItemLink> related,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        IReadOnlyList<AttachmentMetadata> attachments)
    MapRelations(WorkItem current, WorkItem? previous)
    {
        var external = new List<ExternalWorkItemLink>();
        var related = new List<RelatedWorkItemLink>();
        var hyperlinks = new List<HyperlinkWorkItemLink>();
        var attachments = new List<AttachmentMetadata>();

        if (current.Relations is null)
            return (external, related, hyperlinks, attachments);

        var prevRelations = previous?.Relations ?? Enumerable.Empty<WorkItemRelation>();

        foreach (var relation in current.Relations)
        {
            // Skip relations that already existed in the previous revision.
            if (ExistsInPrevious(relation, prevRelations))
                continue;

            var rel = relation.Rel ?? string.Empty;
            var url = relation.Url ?? string.Empty;
            var comment = TryGetAttribute<string>(relation, "comment");

            if (rel.Equals(HyperlinkRel, StringComparison.OrdinalIgnoreCase))
            {
                hyperlinks.Add(new HyperlinkWorkItemLink
                {
                    ArtifactLinkType = rel,
                    Comment = string.IsNullOrEmpty(comment) ? null : comment,
                    Location = url
                });
            }
            else if (rel.Equals(ArtifactLinkRel, StringComparison.OrdinalIgnoreCase))
            {
                var linkTypeName = TryGetAttribute<string>(relation, "name") ?? rel;
                external.Add(new ExternalWorkItemLink
                {
                    ArtifactLinkType = linkTypeName,
                    Comment = string.IsNullOrEmpty(comment) ? null : comment,
                    LinkedArtifactUri = url
                });
            }
            else if (rel.Equals(AttachedFileRel, StringComparison.OrdinalIgnoreCase))
            {
                var name = TryGetAttribute<string>(relation, "name") ?? ExtractFileName(url);
                var size = TryGetAttribute<long>(relation, "resourceSize");
                attachments.Add(new AttachmentMetadata
                {
                    OriginalName = name,
                    RelativePath = name,
                    Size = size,
                    Sha256 = string.Empty   // Azure DevOps REST does not expose SHA-256 for attachments
                });
            }
            else if (!string.IsNullOrEmpty(rel))
            {
                // All other relations are work-item-to-work-item links (related, parent, child, etc.)
                var relatedId = ExtractWorkItemId(url);
                related.Add(new RelatedWorkItemLink
                {
                    ArtifactLinkType = rel,
                    Comment = string.IsNullOrEmpty(comment) ? null : comment,
                    LinkTypeEnd = rel,
                    RelatedWorkItemId = relatedId
                });
            }
        }

        return (external, related, hyperlinks, attachments);
    }

    private static bool ExistsInPrevious(
        WorkItemRelation current,
        IEnumerable<WorkItemRelation> previousRelations)
    {
        foreach (var prev in previousRelations)
        {
            if (!string.Equals(prev.Rel, current.Rel, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(prev.Url, current.Url, StringComparison.OrdinalIgnoreCase))
                continue;
            return true;
        }
        return false;
    }

    private static T? TryGetAttribute<T>(WorkItemRelation relation, string key)
    {
        if (relation.Attributes is null)
            return default;
        if (!relation.Attributes.TryGetValue(key, out var value))
            return default;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private static string ExtractFileName(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "attachment";

        var lastSlash = url.LastIndexOf('/');
        var raw = lastSlash >= 0 ? url.Substring(lastSlash + 1) : url;

        // Strip query string.
        var q = raw.IndexOf('?');
        return q >= 0 ? raw.Substring(0, q) : raw;
    }

    private static int ExtractWorkItemId(string url)
    {
        // URL format: .../wit/workitems/{id}
        if (string.IsNullOrEmpty(url))
            return 0;

        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0)
            return 0;

        var segment = url.Substring(lastSlash + 1);
        // Strip query string.
        var q = segment.IndexOf('?');
        if (q >= 0)
            segment = segment.Substring(0, q);

        return int.TryParse(segment, out var id) ? id : 0;
    }
}

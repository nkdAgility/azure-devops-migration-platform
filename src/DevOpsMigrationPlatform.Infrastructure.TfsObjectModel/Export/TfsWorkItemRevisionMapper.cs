using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;

/// <summary>
/// Maps a TFS <see cref="Revision"/> to a <see cref="WorkItemRevision"/>.
/// Only captures fields and links that changed relative to the previous revision.
/// </summary>
public interface IWorkItemRevisionMapper
{
    WorkItemRevision Map(WorkItem workItem, Revision revision, Revision? previousRevision);
}

public class TfsWorkItemRevisionMapper : IWorkItemRevisionMapper
{
    private readonly IWorkItemExportMetrics _metrics;
    private readonly ILogger<TfsWorkItemRevisionMapper> _logger;

    public TfsWorkItemRevisionMapper(IWorkItemExportMetrics metrics, ILogger<TfsWorkItemRevisionMapper> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public WorkItemRevision Map(WorkItem workItem, Revision revision, Revision? previousRevision)
    {
        var changedDate = (DateTime)revision.Fields["System.ChangedDate"].Value;
        var fields = new List<WorkItemField>();
        var externalLinks = new List<ExternalWorkItemLink>();
        var relatedLinks = new List<RelatedWorkItemLink>();
        var hyperlinks = new List<HyperlinkWorkItemLink>();
        var attachments = new List<AttachmentMetadata>();

        // Changed fields only
        var changedFields = revision.Fields
            .Cast<Field>()
            .Where(field =>
                previousRevision == null ||
                !previousRevision.Fields.Contains(field.Id) ||
                !Equals(previousRevision.Fields[field.ReferenceName].Value, field.Value))
            .ToList();

        foreach (var field in changedFields)
        {
            fields.Add(new WorkItemField
            {
                ReferenceName = field.ReferenceName,
                Value = field.Value?.ToString()
            });
        }

        // New links only
        var newLinks = revision.Links
            .Cast<Link>()
            .Where(link =>
                previousRevision == null ||
                !LinkExistsInPrevious(link, previousRevision.Links))
            .ToList();

        foreach (var link in newLinks)
        {
            var linkStopwatch = Stopwatch.StartNew();
            var handled = true;
            try
            {
                switch (link)
                {
                    case ExternalLink e:
                        externalLinks.Add(new ExternalWorkItemLink
                        {
                            ArtifactLinkType = e.ArtifactLinkType.ToString(),
                            Comment = string.IsNullOrEmpty(e.Comment) ? null : e.Comment,
                            LinkedArtifactUri = e.LinkedArtifactUri
                        });
                        break;

                    case RelatedLink r:
                        relatedLinks.Add(new RelatedWorkItemLink
                        {
                            ArtifactLinkType = r.ArtifactLinkType.ToString(),
                            Comment = string.IsNullOrEmpty(r.Comment) ? null : r.Comment,
                            LinkTypeEnd = r.LinkTypeEnd.ToString(),
                            RelatedWorkItemId = r.RelatedWorkItemId
                        });
                        break;

                    case Hyperlink h:
                        hyperlinks.Add(new HyperlinkWorkItemLink
                        {
                            ArtifactLinkType = h.ArtifactLinkType.ToString(),
                            Comment = string.IsNullOrEmpty(h.Comment) ? null : h.Comment,
                            Location = h.Location
                        });
                        break;

                    default:
                        handled = false;
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogWarning(
                                "Skipping unhandled link type {LinkType} on WorkItem {WorkItemId} Revision {RevisionIndex}",
                                link.GetType().Name, workItem.Id, revision.Index);
                        break;
                }

                if (handled)
                    _metrics.RecordLinkExported(workItem.Store.TeamProjectCollection.InstanceId, workItem.Id, revision.Index);
            }
            catch (Exception ex)
            {
                _metrics.RecordLinkError(workItem.Store.TeamProjectCollection.InstanceId, workItem.Id, revision.Index);
                throw new InvalidOperationException(
                    $"Failed to map link of type {link.GetType().Name} on WorkItem {workItem.Id} Revision {revision.Index}", ex);
            }
            finally
            {
                linkStopwatch.Stop();
                _metrics.RecordLinkProcessingDuration(
                    workItem.Store.TeamProjectCollection.InstanceId,
                    workItem.Id,
                    revision.Index,
                    linkStopwatch.Elapsed);
            }
        }

        // New attachments — metadata only (binary copied separately by WorkItemExportService)
        var newAttachmentNames = revision.Attachments
            .Cast<Attachment>()
            .Where(a =>
                previousRevision == null ||
                !previousRevision.Attachments.Cast<Attachment>().Any(prev => prev.Name == a.Name))
            .Select(a => a.Name)
            .ToList();

        foreach (var name in newAttachmentNames)
        {
            attachments.Add(new AttachmentMetadata { OriginalName = name, RelativePath = name });
        }

        return new WorkItemRevision
        {
            WorkItemId = workItem.Id,
            RevisionIndex = revision.Index,
            ChangedDate = new DateTimeOffset(changedDate, TimeSpan.Zero),
            Fields = fields,
            ExternalLinks = externalLinks,
            RelatedLinks = relatedLinks,
            Hyperlinks = hyperlinks,
            Attachments = attachments
        };
    }

    private static bool LinkExistsInPrevious(Link current, LinkCollection previousLinks)
    {
        foreach (var previous in previousLinks.Cast<Link>())
        {
            if (current.BaseType != previous.BaseType) continue;
            if (current.ArtifactLinkType != previous.ArtifactLinkType) continue;
            if (current.Comment != previous.Comment) continue;
            if (GetComparableLinkTarget(current) == GetComparableLinkTarget(previous))
                return true;
        }
        return false;
    }

    private static string? GetComparableLinkTarget(Link link)
    {
        return link switch
        {
            ExternalLink e => e.LinkedArtifactUri,
            RelatedLink r => r.RelatedWorkItemId.ToString(),
            Hyperlink h => h.Location,
            _ => null
        };
    }
}

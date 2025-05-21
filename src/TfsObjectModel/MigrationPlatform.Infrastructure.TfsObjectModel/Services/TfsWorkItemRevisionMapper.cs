using Microsoft.TeamFoundation.WorkItemTracking.Client;
using MigrationPlatform.Abstractions;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Telemetry;
using System.Diagnostics;

namespace MigrationPlatform.Infrastructure.TfsObjectModel.Services
{
    public interface IWorkItemRevisionMapper
    {
        MigrationWorkItemRevision Map(WorkItem workItem, Revision revision, Revision? previousRevision);
    }

    public class TfsWorkItemRevisionMapper : IWorkItemRevisionMapper
    {
        private readonly IWorkItemExportMetrics _workItemExportMetrics;

        public TfsWorkItemRevisionMapper(IWorkItemExportMetrics workItemExportMetrics)
        {
            _workItemExportMetrics = workItemExportMetrics;
        }

        public MigrationWorkItemRevision Map(WorkItem workItem, Revision revision, Revision? previousRevision)
        {
            var mapped = new MigrationWorkItemRevision
            {
                workItemId = workItem.Id,
                Index = revision.Index,
                ChangedDate = (DateTime)revision.Fields["System.ChangedDate"].Value
            };

            // Changed fields
            var changedFields = revision.Fields
                .Cast<Field>()
                .Where(field =>
                    previousRevision == null ||
                    !previousRevision.Fields.Contains(field.Id) ||
                    !Equals(previousRevision.Fields[field.ReferenceName].Value, field.Value))
                .ToList();

            foreach (var field in changedFields)
            {
                mapped.Fields.Add(new MigrationWorkItemField(field.Name, field.ReferenceName, field.Value));
            }

            // Changed links
            var newLinks = revision.Links
                .Cast<Link>()
                .Where(link =>
                    previousRevision == null ||
                    !LinkExistsInPrevious(link, previousRevision.Links))
                .ToList();

            foreach (var link in newLinks)
            {
                var linkStopwatch = Stopwatch.StartNew();

                try
                {
                    switch (link)
                    {
                        case ExternalLink e:
                            mapped.ExternalLinks.Add(new MigrationWorkItemExternalLink(
                                e.ArtifactLinkType.ToString(),
                                e.Comment,
                                e.LinkedArtifactUri));
                            break;

                        case RelatedLink r:
                            mapped.RelatedLinks.Add(new MigrationWorkItemRelatedLink(
                                r.ArtifactLinkType.ToString(),
                                r.Comment,
                                r.LinkTypeEnd.ToString(),
                                r.RelatedWorkItemId));
                            break;

                        case Hyperlink h:
                            mapped.Hyperlinks.Add(new MigrationWorkItemHyperlink(
                                h.ArtifactLinkType.ToString(),
                                h.Comment,
                                h.Location));
                            break;

                        default:
                            throw new NotImplementedException($"Unhandled link type: {link.GetType()}");
                    }

                    _workItemExportMetrics.RecordLinkExported(workItem.Store.TeamProjectCollection.InstanceId, workItem.Id, revision.Index);
                }
                catch (Exception ex)
                {
                    _workItemExportMetrics.RecordLinkError(workItem.Store.TeamProjectCollection.InstanceId, workItem.Id, revision.Index);
                    throw new InvalidOperationException($"Failed to map link of type {link.GetType().Name} on WorkItem {workItem.Id} Revision {revision.Index}", ex);
                }
                finally
                {
                    linkStopwatch.Stop();
                    _workItemExportMetrics.RecordLinkProcessingDuration(workItem.Store.TeamProjectCollection.InstanceId, workItem.Id, revision.Index, linkStopwatch.Elapsed);
                }
            }

            return mapped;
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
}

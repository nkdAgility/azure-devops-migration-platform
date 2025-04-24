using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using MigrationPlatform.Abstractions;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.TfsObjectModel.Extensions;
using MigrationPlatform.Infrastructure.TfsObjectModel.Models;

namespace MigrationPlatform.Infrastructure.Services
{
    public class WorkItemExportService : IWorkItemExportService
    {

        private readonly IMigrationRepository _migrationRepository;
        private readonly WorkItemStore _workItemStore;


        public WorkItemExportService(IMigrationRepository migrationRepository, WorkItemStore store)
        {
            _migrationRepository = migrationRepository;
            _workItemStore = store;
        }


        public async IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(string tfsServer, string project)
        {
            var progressUpdate = new WorkItemMigrationProgress();
            yield return progressUpdate;


            var allWorkItemsQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.ChangedDate] > '2025-04-23'";

            var queryCount = _migrationRepository.GetQueryCount(allWorkItemsQuery);
            if (queryCount == null)
            {
                queryCount = 0;
                foreach (WorkItemQueryCountChunk chunkItem in _workItemStore.QueryCountAllByDateChunk(allWorkItemsQuery))
                {
                    queryCount = chunkItem.TotalWorkItems;
                    progressUpdate.TotalWorkItems = chunkItem.TotalWorkItems;
                    yield return progressUpdate;
                }
                _migrationRepository.UpdateQueryCount(allWorkItemsQuery, queryCount ?? 0);
            }
            if (queryCount.HasValue)
            {
                progressUpdate.TotalWorkItems = queryCount.Value;
            }
            else
            {
                progressUpdate.TotalWorkItems = 0; // Or handle the null case appropriately
            }
            yield return progressUpdate;

            foreach (WorkItemFromChunk chunkItem in _workItemStore.QueryAllByDateChunk(allWorkItemsQuery))
            {
                var tfsWorkItem = chunkItem.WorkItem;
                progressUpdate.ChunkInfo = (WorkItemQueryChunk)chunkItem;
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                progressUpdate.WorkItemsProcessed++;
                if (_migrationRepository.GetWatermark(tfsWorkItem.Id) + 1 == tfsWorkItem.Revision)
                {
                    progressUpdate.RevisionsProcessed += tfsWorkItem.Revision;

                    yield return progressUpdate;
                    continue;
                }
                Revision previousTfsRevision = null;
                foreach (Revision tfsRevision in tfsWorkItem.Revisions)
                {
                    if (_migrationRepository.IsRevisionProcessed(tfsWorkItem.Id, tfsRevision.Index))
                    {
                        progressUpdate.RevisionsProcessed++;
                        continue; // Skip already processed revisions
                    }
                    progressUpdate.RevisionIndex = tfsRevision.Index;
                    var mWorkItem = new MigrationWorkItemRevision();
                    mWorkItem.workItemId = tfsWorkItem.Id;
                    mWorkItem.Index = tfsRevision.Index;

                    mWorkItem.ChangedDate = (DateTime)tfsRevision.Fields["System.ChangedDate"].Value;



                    var changedFields = tfsRevision.Fields
                         .Cast<Field>()
                         .Where(field =>
                             previousTfsRevision == null ||
                             !previousTfsRevision.Fields.Contains(field.Id) ||
                             !Equals(previousTfsRevision.Fields[field.ReferenceName].Value, field.Value))
                         .ToList();

                    foreach (var field in changedFields)
                    {
                        mWorkItem.Fields.Add(new MigrationWorkItemField(field.Name, field.ReferenceName, field.Value));
                        progressUpdate.FieldsProcessed++;
                    }

                    var newLinks = tfsRevision.Links
                        .Cast<Link>()
                        .Where(link => previousTfsRevision == null || !LinkExistsInPrevious(link, previousTfsRevision.Links))
                        .ToList();
                    foreach (Link link in newLinks)
                    {
                        if (previousTfsRevision != null && previousTfsRevision.Links.Contains(link))
                        {
                            continue; // Skip unchnaged link
                        }
                        if (link is ExternalLink externalLink)
                        {
                            mWorkItem.ExternalLinks.Add(new MigrationWorkItemExternalLink(link.ArtifactLinkType.ToString(), link.Comment, externalLink.LinkedArtifactUri));
                        }
                        else if (link is RelatedLink relatedLink)
                        {
                            mWorkItem.RelatedLinks.Add(new MigrationWorkItemRelatedLink(link.ArtifactLinkType.ToString(), link.Comment, relatedLink.LinkTypeEnd.ToString(), relatedLink.RelatedWorkItemId));
                        }
                        else if (link is Hyperlink hyperlink)
                        {
                            mWorkItem.Hyperlinks.Add(new MigrationWorkItemHyperlink(link.ArtifactLinkType.ToString(), link.Comment, hyperlink.Location));
                        }
                        else
                        {
                            throw new NotImplementedException($"Link type {link.GetType()} is not implemented.");
                        }

                    }


                    var newAttachments = tfsRevision.Attachments
                         .Cast<Attachment>()
                         .Where(attachment =>
                             previousTfsRevision == null ||
                             !previousTfsRevision.Attachments
                                 .Cast<Attachment>()
                                 .Any(prev => prev.Name == attachment.Name))
                         .ToList();

                    foreach (var attachment in newAttachments)
                    {
                        var wiStore = _workItemStore.TeamProjectCollection.GetService<WorkItemServer>();
                        var fileLocation = wiStore.DownloadFile(attachment.Id);

                        _migrationRepository.AddWorkItemRevisionAttachment(
                            mWorkItem,
                            attachment.Name,
                            fileLocation,
                            attachment.Comment);

                        progressUpdate.AttachmentsProcessed++;
                    }


                    _migrationRepository.AddWorkItemRevision(mWorkItem); //TODO: Once we know where to save it.
                    progressUpdate.RevisionsProcessed++;
                    previousTfsRevision = tfsRevision;
                }
                // Process each work item
                yield return progressUpdate;

            }

        }

        private static bool LinkExistsInPrevious(Link current, LinkCollection previousLinks)
        {
            foreach (var previous in previousLinks.Cast<Link>())
            {
                if (current.BaseType != previous.BaseType) continue;
                if (current.ArtifactLinkType != previous.ArtifactLinkType) continue;
                if (current.Comment != previous.Comment) continue;

                var currentTarget = GetComparableLinkTarget(current);
                var previousTarget = GetComparableLinkTarget(previous);

                if (currentTarget == previousTarget)
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

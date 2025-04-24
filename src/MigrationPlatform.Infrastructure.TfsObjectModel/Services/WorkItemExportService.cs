using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
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
        public WorkItemExportService(IMigrationRepository migrationRepository)
        {
            _migrationRepository = migrationRepository;
        }
        public async IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(string tfsServer, string project)
        {
            var progressUpdate = new WorkItemMigrationProgress();
            yield return progressUpdate;

            WorkItemStore store;
            var creds = new VssClientCredentials(true);
            creds.PromptType = CredentialPromptType.PromptIfNeeded;


            var collection = new TfsTeamProjectCollection(new Uri(tfsServer), creds);
            collection.EnsureAuthenticated(); // Optional but recommended
            store = collection.GetService<WorkItemStore>();


            var allWorkItemsQuery = "SELECT * FROM WorkItems WHERE [System.TeamProject] = '" + project + "'";

            foreach (WorkItemFromChunk chunkItem in store.QueryAllByDateChunk(allWorkItemsQuery))
            {
                var tfsWorkItem = chunkItem.WorkItem;
                progressUpdate.ChunkInfo = (WorkItemQueryChunk)chunkItem;
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                progressUpdate.WorkItemsProcessed++;
                foreach (Revision tfsWorkItemRevision in tfsWorkItem.Revisions)
                {
                    progressUpdate.RevisionIndex = tfsWorkItemRevision.Index;
                    var mWorkItem = new MigrationWorkItemRevision();
                    mWorkItem.id = tfsWorkItem.Id;
                    mWorkItem.Index = tfsWorkItemRevision.Index;

                    mWorkItem.ChangedDate = (DateTime)tfsWorkItemRevision.Fields["System.ChangedDate"].Value;
                    foreach (Field field in tfsWorkItemRevision.Fields)
                    {
                        mWorkItem.Fields.Add(new MigrationWorkItemField(field.Name, field.ReferenceName, field.Value));
                        progressUpdate.FieldsProcessed++;
                    }
                    _migrationRepository.AddWorkItemRevision(mWorkItem); //TODO: Once we know where to save it.
                    progressUpdate.RevisionsProcessed++;
                    yield return progressUpdate;
                }
                // Process each work item

            }

        }



    }
}

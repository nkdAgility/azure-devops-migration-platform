using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using MigrationPlatform.Abstractions;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;

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

            Thread.CurrentThread.GetApartmentState();

            WorkItemStore store;
            var creds = new VssClientCredentials(true);
            creds.PromptType = CredentialPromptType.PromptIfNeeded;


            var collection = new TfsTeamProjectCollection(new Uri(tfsServer), creds);
            collection.EnsureAuthenticated(); // Optional but recommended
            store = collection.GetService<WorkItemStore>();


            var allWorkItemsQuery = "SELECT * FROM WorkItems WHERE [System.TeamProject] = '" + project + "'";
            var totalWorkItems = store.QueryCount(allWorkItemsQuery);

            var workItems = store.Query("SELECT * FROM WorkItems WHERE [System.TeamProject] = '" + project + "'");

            foreach (WorkItem tfsWorkItem in workItems)
            {
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                foreach (Revision tfsWorkItemRevision in tfsWorkItem.Revisions)
                {
                    progressUpdate.RevisionIndex = tfsWorkItemRevision.Index;
                    var mWorkItem = new MigrationWorkItemRevision();
                    mWorkItem.id = tfsWorkItem.Id;
                    mWorkItem.Index = tfsWorkItemRevision.Index;

                    mWorkItem.ChangedDate = (DateTime)tfsWorkItemRevision.Fields["System.ChangedDate"].Value;
                    foreach (Field field in tfsWorkItemRevision.Fields)
                    {
                        var mField = new MigrationWorkItemField();
                        mField.Name = field.Name;
                        mField.ReferenceName = field.ReferenceName;
                        mField.Value = field.Value;
                        mWorkItem.Fields.Add(mField);
                    }
                    //_migrationRepository.AddWorkItemRevision(mWorkItem);
                    yield return progressUpdate;
                }
                // Process each work item

            }

        }


        public static T RunInStaThread<T>(Func<T> action)
        {
            T result = default!;
            var thread = new Thread(() => result = action())
            {
                IsBackground = false,
                ApartmentState = ApartmentState.STA
            };
            thread.Start();
            thread.Join();
            return result;
        }

    }
}

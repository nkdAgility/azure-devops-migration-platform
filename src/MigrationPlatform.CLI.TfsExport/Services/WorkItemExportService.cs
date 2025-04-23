using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;

namespace MigrationPlatform.TfsExport.Services
{
    public class WorkItemExportService : IWorkItemExportService
    {

        private readonly IWorkItemRepository _workItemRepository;
        public WorkItemExportService(IWorkItemRepository workItemRepository)
        {
            _workItemRepository = workItemRepository;
        }
        public async IAsyncEnumerable<WorkItemsProcessingSummary> ExportWorkItemsAsync()
        {
            yield return null;
        }

    }
}

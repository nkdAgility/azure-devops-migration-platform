

using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Abstractions.Services
{
    public interface IWorkItemExportService
    {
        public IAsyncEnumerable<WorkItemsProcessingSummary> ExportWorkItemsAsync();
    }
}

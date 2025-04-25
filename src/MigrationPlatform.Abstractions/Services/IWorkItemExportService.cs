

using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Abstractions.Services
{
    public interface IWorkItemExportService
    {
        public IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(string tfsServer, string project, string wiqlQuery);
    }
}

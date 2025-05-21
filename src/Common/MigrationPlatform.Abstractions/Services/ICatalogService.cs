using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Abstractions.Services
{
    public interface ICatalogService
    {
        IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(string orgUrl, string project, string pat, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetProjectsAsync(string orgUrl, string pat);

    }
}

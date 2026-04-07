using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Creates Azure DevOps HTTP clients from a URL and PAT, isolating all
/// <c>VssConnection</c> / SDK construction from service logic.
/// </summary>
public interface IAzureDevOpsClientFactory
{
    /// <summary>Returns a <see cref="ProjectHttpClient"/> authenticated against <paramref name="url"/>.</summary>
    Task<ProjectHttpClient> CreateProjectClientAsync(
        string url, string pat, CancellationToken cancellationToken = default);

    /// <summary>Returns a <see cref="WorkItemTrackingHttpClient"/> authenticated against <paramref name="url"/>.</summary>
    Task<WorkItemTrackingHttpClient> CreateWorkItemClientAsync(
        string url, string pat, CancellationToken cancellationToken = default);
}

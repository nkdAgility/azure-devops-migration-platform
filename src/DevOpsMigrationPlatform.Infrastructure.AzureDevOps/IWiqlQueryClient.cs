using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Abstracts the single WIQL query operation needed by
/// <see cref="Services.WorkItemQueryWindowStrategy"/> so that the strategy
/// can be unit-tested without a live Azure DevOps connection.
/// </summary>
public interface IWiqlQueryClient
{
    Task<WorkItemQueryResult> QueryByWiqlAsync(
        Wiql wiql,
        string project,
        CancellationToken cancellationToken = default);
}

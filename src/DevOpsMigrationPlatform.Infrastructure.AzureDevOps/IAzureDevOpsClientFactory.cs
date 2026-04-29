using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Creates Azure DevOps HTTP clients from an <see cref="OrganisationEndpoint"/>,
/// isolating all <c>VssConnection</c> / SDK construction from service logic.
/// </summary>
/// <remarks>
/// Intentionally placed in <c>Infrastructure.AzureDevOps</c> rather than <c>Abstractions</c>.
/// Its return types (<see cref="ProjectHttpClient"/>, <see cref="WorkItemTrackingHttpClient"/>)
/// are Azure DevOps SDK types. Moving this interface to <c>Abstractions</c> would introduce
/// an SDK package dependency on the domain layer, violating the layering constraint.
/// </remarks>
internal interface IAzureDevOpsClientFactory
{
    /// <summary>Returns a <see cref="ProjectHttpClient"/> authenticated against <paramref name="endpoint"/>.</summary>
    Task<ProjectHttpClient> CreateProjectClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns a <see cref="WorkItemTrackingHttpClient"/> authenticated against <paramref name="endpoint"/>.</summary>
    Task<WorkItemTrackingHttpClient> CreateWorkItemClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns a <see cref="GitHttpClient"/> authenticated against <paramref name="endpoint"/>.</summary>
    Task<GitHttpClient> CreateGitClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns a <see cref="TeamHttpClient"/> authenticated against <paramref name="endpoint"/>.</summary>
    Task<TeamHttpClient> CreateTeamClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns a <see cref="WorkHttpClient"/> authenticated against <paramref name="endpoint"/>.</summary>
    Task<WorkHttpClient> CreateWorkClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default);
}

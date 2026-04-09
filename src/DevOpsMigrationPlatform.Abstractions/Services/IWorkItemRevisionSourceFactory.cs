using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Factory that creates an <see cref="IWorkItemRevisionSource"/> from job-level parameters.
/// Lives in Abstractions so that <c>WorkItemsModule</c> (in Infrastructure) can depend on it
/// without a direct project reference to <c>Infrastructure.AzureDevOps</c>.
/// </summary>
public interface IWorkItemRevisionSourceFactory
{
    /// <summary>
    /// Creates a source that streams revisions for the given project.
    /// </summary>
    /// <param name="organisationUrl">Azure DevOps organisation URL.</param>
    /// <param name="project">Team project name.</param>
    /// <param name="pat">Personal access token (resolved value, not a placeholder).</param>
    /// <param name="wiqlQuery">WIQL query that selects work item IDs.</param>
    /// <param name="cancellationToken">Cancellation token for the async connection step.</param>
    Task<IWorkItemRevisionSource> CreateAsync(
        string organisationUrl,
        string project,
        string pat,
        string wiqlQuery,
        CancellationToken cancellationToken);
}

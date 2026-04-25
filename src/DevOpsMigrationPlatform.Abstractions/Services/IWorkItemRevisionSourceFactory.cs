using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Factory that creates an <see cref="IWorkItemRevisionSource"/> from endpoint options.
/// Lives in Abstractions so that <c>WorkItemsModule</c> (in Infrastructure) can depend on it
/// without a direct project reference to <c>Infrastructure.AzureDevOps</c>.
/// </summary>
public interface IWorkItemRevisionSourceFactory
{
    /// <summary>
    /// Creates a source that streams revisions for the given endpoint.
    /// </summary>
    /// <param name="endpoint">Endpoint options; concrete type determines the connector used.</param>
    /// <param name="ct">Cancellation token for the async connection step.</param>
    Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct);
}

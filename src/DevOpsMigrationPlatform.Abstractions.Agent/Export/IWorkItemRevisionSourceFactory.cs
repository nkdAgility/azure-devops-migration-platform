using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Factory that creates an <see cref="IWorkItemRevisionSource"/> from endpoint options.
/// Lives in Abstractions so that <c>WorkItemsModule</c> (in Infrastructure) can depend on it
/// without a direct project reference to <c>Infrastructure.AzureDevOps</c>.
/// </summary>
public interface IWorkItemRevisionSourceFactory
{
    /// <summary>
    /// Creates a source that streams revisions. Endpoint info is resolved from DI.
    /// </summary>
    /// <param name="ct">Cancellation token for the async connection step.</param>
    Task<IWorkItemRevisionSource> CreateAsync(CancellationToken ct);
}

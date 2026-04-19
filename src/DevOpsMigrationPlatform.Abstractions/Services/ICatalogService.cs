using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Discovers projects and counts artefacts within an Azure DevOps organisation.
/// Implementations must not buffer all results into memory.
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Returns the names of all team projects in <paramref name="orgUrl"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetProjectsAsync(MigrationEndpointOptions endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams incremental discovery summaries for <paramref name="project"/>.
    /// Each yielded item reflects the latest known counts; the stream ends when
    /// all countable artefacts have been tallied.
    /// </summary>
    IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(
        MigrationEndpointOptions endpoint,
        string project,
        CancellationToken cancellationToken = default);
}

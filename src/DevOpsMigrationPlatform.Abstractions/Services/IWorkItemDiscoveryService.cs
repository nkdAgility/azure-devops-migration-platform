using System.Collections.Generic;
using System.Threading;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Discovers work items and their revision counts for a single project.
/// Streams incremental <see cref="ProjectDiscoverySummary"/> snapshots as
/// each query window completes. The final snapshot has
/// <see cref="ProjectDiscoverySummary.IsWorkItemComplete"/> = <c>true</c>.
///
/// Used by both inventory (counting) and export (cataloguing work items to fetch).
/// </summary>
public interface IWorkItemDiscoveryService
{
    /// <summary>
    /// Streams incremental work-item discovery snapshots for <paramref name="project"/>.
    /// </summary>
    IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        string url,
        string project,
        string pat,
        CancellationToken cancellationToken = default);
}

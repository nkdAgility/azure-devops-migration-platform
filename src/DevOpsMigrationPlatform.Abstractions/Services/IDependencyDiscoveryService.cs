using System;
using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Service for discovering external (cross-project and cross-organisation) work item links
/// across one or more configured organisations.
/// Coordinates link analysis across all enabled organisations and sources.
/// </summary>
public interface IDependencyDiscoveryService
{
    /// <summary>
    /// Discovers all external work item links in the configured organisations.
    /// Streams results as DependencyProgressEvent records (DependencyFoundEvent for each link,
    /// DependencyHeartbeatEvent for progress updates).
    /// </summary>
    /// <param name="completedProjectKeys">Optional set of project keys (<c>"orgUrl|projectName"</c>) to skip.</param>
    /// <param name="wiqlFilter">Optional WIQL expression to filter the set of work items to analyse.
    /// If null or empty, all work items are included (equivalent to SELECT [System.Id] FROM WorkItems).</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation.</param>
    /// <returns>An async enumerable of DependencyProgressEvent records.</returns>
    IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
        HashSet<string>? completedProjectKeys = null,
        string? wiqlFilter = null,
        CancellationToken cancellationToken = default);
}

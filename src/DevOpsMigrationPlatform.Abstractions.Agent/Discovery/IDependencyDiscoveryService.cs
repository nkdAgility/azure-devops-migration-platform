using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

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
    /// <param name="inProgressProjectKey">Optional project key (<c>"orgUrl|projectName"</c>) that was in progress
    /// when the previous run was interrupted. When set, the continuation token is used to resume that project.</param>
    /// <param name="inProgressToken">Optional <see cref="BatchContinuationToken"/> for the in-progress project.</param>
    /// <param name="continuationCheckpointWriter">Optional callback invoked per-batch to persist resume state.</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation.</param>
    /// <returns>An async enumerable of DependencyProgressEvent records.</returns>
    IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
        HashSet<string>? completedProjectKeys = null,
        string? wiqlFilter = null,
        string? inProgressProjectKey = null,
        BatchContinuationToken? inProgressToken = null,
        Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
        CancellationToken cancellationToken = default);
}

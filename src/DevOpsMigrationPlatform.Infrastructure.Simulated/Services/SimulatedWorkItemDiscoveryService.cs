using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Returns a single final <see cref="ProjectDiscoverySummary"/> with zero work items.
/// No network calls are made.
/// </summary>
public sealed class SimulatedWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        MigrationEndpointOptions endpoint,
        string project,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 0,
            RevisionsCount = 0,
            IsWorkItemComplete = true,
            IsRepoComplete = true,
            IsPipelineComplete = true
        };

        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        MigrationEndpointOptions endpoint,
        string project,
        string? baseQuery = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 0,
            RevisionsCount = 0,
            IsWorkItemComplete = true,
            IsRepoComplete = true,
            IsPipelineComplete = true
        };

        await System.Threading.Tasks.Task.CompletedTask;
    }
}

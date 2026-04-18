using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Returns a single final <see cref="ProjectDiscoverySummary"/> with counts
/// derived from the <see cref="SimulatedEndpointOptions.Generator"/> configuration.
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
        var (workItems, revisions) = ComputeCounts(endpoint, project);

        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = workItems,
            RevisionsCount = revisions,
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
        var (workItems, revisions) = ComputeCounts(endpoint, project);

        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = workItems,
            RevisionsCount = revisions,
            IsWorkItemComplete = true,
            IsRepoComplete = true,
            IsPipelineComplete = true
        };

        await System.Threading.Tasks.Task.CompletedTask;
    }

    private static (int WorkItems, int Revisions) ComputeCounts(
        MigrationEndpointOptions endpoint, string project)
    {
        if (endpoint is SimulatedEndpointOptions simulated
            && simulated.Generator?.Projects is { Count: > 0 } projects)
        {
            var projectConfig = projects.FirstOrDefault(
                p => string.Equals(p.Name, project, System.StringComparison.OrdinalIgnoreCase));

            if (projectConfig?.WorkItemTypes is { Count: > 0 } types)
            {
                int workItems = types.Sum(t => t.Count);
                int revisions = types.Sum(t => t.Count * t.RevisionsPerItem);
                return (workItems, revisions);
            }
        }

        return (0, 0);
    }
}

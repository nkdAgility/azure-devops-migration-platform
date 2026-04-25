using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Returns a single final <see cref="ProjectDiscoverySummary"/> with counts
/// derived from the <see cref="SimulatedGeneratorConfig"/> configuration.
/// No network calls are made.
/// </summary>
public sealed class SimulatedWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly SimulatedGeneratorConfig _config;

    public SimulatedWorkItemDiscoveryService(SimulatedGeneratorConfig config)
    {
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <paramref name="scope"/> filter options are not applied in the Simulated implementation
    /// because the service derives counts from a generator config, not from real field values.
    /// </remarks>
    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope? scope = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workItems, revisions) = ComputeCounts(project);

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
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workItems, revisions) = ComputeCounts(project);

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

    private (int WorkItems, int Revisions) ComputeCounts(string project)
    {
        if (_config.Projects is { Count: > 0 } projects)
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

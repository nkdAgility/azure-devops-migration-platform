using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Discovery;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Uses the injected <see cref="WorkItemStore"/> and <see cref="TfsWorkItemQueryWindowStrategy"/>
/// to enumerate work items and count revisions in a streaming, memory-safe manner.
/// Runs in the .NET Framework 4.8.1 subprocess.
/// </summary>
public sealed class TfsObjectModelWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly WorkItemStore _workItemStore;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;

    public TfsObjectModelWorkItemDiscoveryService(
        WorkItemStore workItemStore,
        TfsWorkItemQueryWindowStrategy windowStrategy)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope? scope = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TFS Object Model subprocess does not support field-level filter scopes.
        // The scope parameter is accepted to satisfy the interface but is intentionally ignored.
        var summary = new ProjectDiscoverySummary { ProjectName = project };

        await foreach (var window in _windowStrategy
            .EnumerateWindowsAsync(endpoint, project, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            if (window.WorkItemIds.Count == 0)
                continue;

            foreach (var id in window.WorkItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wi = _workItemStore.GetWorkItem(id);
                summary.WorkItemsCount++;
                summary.RevisionsCount += wi.Revisions.Count;
            }

            summary.LastUpdatedUtc = DateTime.UtcNow;
            yield return summary;
        }

        summary.IsWorkItemComplete = true;
        summary.LastUpdatedUtc = DateTime.UtcNow;
        yield return summary;
    }

    /// <summary>
    /// TFS Object Model does not support custom WIQL-scoped pre-flight counting.
    /// Delegates to <see cref="DiscoverWorkItemsAsync"/> using the full project scope.
    /// The <paramref name="baseQuery"/> parameter is intentionally ignored.
    /// </summary>
    public IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
        CancellationToken cancellationToken = default)
        => DiscoverWorkItemsAsync(endpoint, project, scope: null, cancellationToken);

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}


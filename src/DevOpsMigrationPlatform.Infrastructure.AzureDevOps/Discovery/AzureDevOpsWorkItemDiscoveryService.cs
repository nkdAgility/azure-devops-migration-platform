// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Uses <see cref="IWorkItemFetchService"/> for field-projected work item fetching
/// and the shared <see cref="IWorkItemQueryWindowStrategy"/> for counting-only paths.
/// </summary>
internal sealed class AzureDevOpsWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IWorkItemFetchService _fetchService;
    private const int ProgressInterval = 200;

    public AzureDevOpsWorkItemDiscoveryService(
        IWorkItemQueryWindowStrategy windowStrategy,
        IWorkItemFetchService fetchService)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _fetchService = fetchService ?? throw new ArgumentNullException(nameof(fetchService));
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope? scope = null,
        IProgress<int>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var summary = new ProjectDiscoverySummary { ProjectName = project };

        // Union caller-supplied fields with System.Rev (always required for revision counting).
        var mergedFields = scope?.Fields is { Count: > 0 }
            ? new[] { "System.Rev" }.Union(scope.Fields).ToArray()
            : new[] { "System.Rev" };

        // Thread the caller's progress callback into the fetch scope so per-batch
        // ProgressEvents are fired automatically without a manual counting loop here.
        var fetchScope = new WorkItemFetchScope(
            Fields: mergedFields,
            FilterOptions: scope?.FilterOptions,
            BaseQuery: scope?.BaseQuery,
            Progress: progress);

        var itemsSinceLastYield = 0;

        await foreach (var item in _fetchService.FetchAsync(endpoint, project, fetchScope, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            summary.WorkItemsCount++;
            if (item.Fields.TryGetValue("System.Rev", out var revObj) && revObj is IConvertible c)
                summary.RevisionsCount += c.ToInt32(null);

            itemsSinceLastYield++;
            if (itemsSinceLastYield >= ProgressInterval)
            {
                summary.LastUpdatedUtc = DateTime.UtcNow;
                yield return summary;
                itemsSinceLastYield = 0;
            }
        }

        summary.IsWorkItemComplete = true;
        summary.LastUpdatedUtc = DateTime.UtcNow;
        yield return summary;
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
        IProgress<int>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var summary = new ProjectDiscoverySummary { ProjectName = project };

        var options = baseQuery is not null
            ? new WorkItemQueryWindowOptions { BaseQuery = baseQuery }
            : null;

        var enumerator = _windowStrategy
            .EnumerateWindowsAsync(endpoint, project, options: options, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool hasNext;
                Exception? windowError = null;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    windowError = ex;
                    hasNext = false;
                }

                if (windowError != null)
                {
                    summary.Error = windowError.Message;
                    summary.IsWorkItemComplete = true;
                    summary.LastUpdatedUtc = DateTime.UtcNow;
                    yield return summary;
                    yield break;
                }

                if (!hasNext)
                    break;

                var window = enumerator.Current;
                cancellationToken.ThrowIfCancellationRequested();

                summary.WorkItemsCount += window.WorkItemIds.Count;
                summary.LastUpdatedUtc = DateTime.UtcNow;
                progress?.Report(summary.WorkItemsCount);
                yield return summary;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        summary.IsWorkItemComplete = true;
        summary.LastUpdatedUtc = DateTime.UtcNow;
        yield return summary;
    }
}

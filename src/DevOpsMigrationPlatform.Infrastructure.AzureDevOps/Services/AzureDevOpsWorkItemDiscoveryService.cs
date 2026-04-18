using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Uses the shared <see cref="IWorkItemQueryWindowStrategy"/> for date-window WIQL
/// queries and fetches <c>System.Rev</c> to tally revision counts.
/// </summary>
public sealed class AzureDevOpsWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private const int RevisionBatchSize = 200;

    public AzureDevOpsWorkItemDiscoveryService(
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsClientFactory clientFactory)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var witClient = await _clientFactory.CreateWorkItemClientAsync(endpoint, cancellationToken);

        var summary = new ProjectDiscoverySummary { ProjectName = project };

        // Use IAsyncEnumerator directly so we can catch exceptions from the window
        // strategy (yield return cannot appear inside a try-catch block in C#).
        var enumerator = _windowStrategy
            .EnumerateWindowsAsync(endpoint, project, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                // Advance one window — catch errors here, yield outside.
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
                    // Emit a terminal partial-result summary so the caller can record
                    // what was collected up to the point of failure and move on.
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

                foreach (var batch in window.WorkItemIds.Chunk(RevisionBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var workItems = await witClient.GetWorkItemsAsync(
                        batch.ToList(),
                        fields: new[] { "System.Rev" },
                        cancellationToken: cancellationToken);

                    foreach (var wi in workItems)
                    {
                        if (wi.Fields.TryGetValue("System.Rev", out var revObj) && revObj is IConvertible c)
                            summary.RevisionsCount += c.ToInt32(null);
                    }
                }

                summary.LastUpdatedUtc = DateTime.UtcNow;
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

    public async IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
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

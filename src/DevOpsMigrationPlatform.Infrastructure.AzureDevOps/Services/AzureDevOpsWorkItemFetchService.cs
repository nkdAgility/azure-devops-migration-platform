using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemFetchService"/>.
/// Uses <see cref="IWorkItemQueryWindowStrategy"/> for date-windowed WIQL queries
/// and batch-fetches only the declared fields via the REST API.
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates in-process
/// before yielding each item.
/// </summary>
public sealed class AzureDevOpsWorkItemFetchService : IWorkItemFetchService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private const int BatchSize = 200;

    public AzureDevOpsWorkItemFetchService(
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsClientFactory clientFactory)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (scope.Fields is null || scope.Fields.Count == 0)
            throw new ArgumentException("Fields must not be null or empty.", nameof(scope));

        var witClient = await _clientFactory.CreateWorkItemClientAsync(endpoint, cancellationToken);

        var windowOptions = scope.BaseQuery is not null
            ? new WorkItemQueryWindowOptions { BaseQuery = scope.BaseQuery }
            : null;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            endpoint, project, windowOptions, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var batch in window.WorkItemIds.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var workItems = await witClient.GetWorkItemsAsync(
                    batch.ToList(),
                    fields: scope.Fields.ToArray(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var wi in workItems)
                {
                    var fields = wi.Fields.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (object?)kvp.Value);

                    var item = new FetchedWorkItem(
                        wi.Id ?? 0,
                        fields);

                    if (PassesFilters(item, scope.FilterOptions))
                        yield return item;
                }
            }
        }
    }

    /// <summary>
    /// Delegates to the shared <see cref="WorkItemFieldFilterEvaluator.PassesFilters"/>.
    /// Kept as an internal static for backward compatibility with tests.
    /// </summary>
    internal static bool PassesFilters(
        FetchedWorkItem item,
        IReadOnlyList<WorkItemFieldFilterOptions>? filters) =>
        WorkItemFieldFilterEvaluator.PassesFilters(item, filters);
}

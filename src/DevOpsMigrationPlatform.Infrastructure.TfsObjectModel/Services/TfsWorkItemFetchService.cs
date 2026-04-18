using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemFetchService"/>.
/// Uses <see cref="TfsWorkItemQueryWindowStrategy"/> for date-windowed WIQL queries
/// and fetches only the declared fields from each work item via the TFS Object Model.
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates in-process
/// before yielding each item.
/// </summary>
public sealed class TfsWorkItemFetchService : IWorkItemFetchService
{
    private readonly WorkItemStore _workItemStore;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;

    public TfsWorkItemFetchService(
        WorkItemStore workItemStore,
        TfsWorkItemQueryWindowStrategy windowStrategy)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
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

        // Bridge OrganisationEndpoint → MigrationEndpointOptions for the window strategy.
        // TFS window strategy ignores the endpoint URL (WorkItemStore is already authenticated).
        var endpointOptions = new TfsMigrationEndpointOptionsAdapter(endpoint);

        var windowOptions = scope.BaseQuery is not null
            ? new WorkItemQueryWindowOptions { BaseQuery = scope.BaseQuery }
            : null;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            endpointOptions, project, windowOptions, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var id in window.WorkItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wi = _workItemStore.GetWorkItem(id);
                var fields = new Dictionary<string, object?>();

                foreach (var fieldName in scope.Fields)
                {
                    if (wi.Fields.Contains(fieldName))
                    {
                        fields[fieldName] = wi.Fields[fieldName].Value;
                    }
                }

                var item = new FetchedWorkItem(wi.Id, fields);

                if (PassesFilters(item, scope.FilterOptions))
                    yield return item;
            }
        }
    }

    internal static bool PassesFilters(FetchedWorkItem item, IReadOnlyList<WorkItemFieldFilterOptions>? filters) =>
        WorkItemFieldFilterEvaluator.PassesFilters(item, filters);
}

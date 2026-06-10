// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemFetchService"/>.
/// Uses <see cref="IWorkItemQueryWindowStrategy"/> for date-windowed WIQL queries
/// and fetches only the declared fields from each work item via <see cref="IWorkItemFieldReader"/>.
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates in-process
/// before yielding each item.
/// </summary>
public sealed class TfsWorkItemFetchService : IWorkItemFetchService
{
    private readonly IWorkItemFieldReader _fieldReader;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;

    /// <summary>
    /// Production constructor: wraps a live <see cref="WorkItemStore"/> in a
    /// <see cref="WorkItemStoreFieldReader"/> and accepts the concrete window strategy.
    /// </summary>
    public TfsWorkItemFetchService(
        WorkItemStore workItemStore,
        TfsWorkItemQueryWindowStrategy windowStrategy)
        : this(new WorkItemStoreFieldReader(workItemStore ?? throw new ArgumentNullException(nameof(workItemStore))),
               windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy)))
    {
    }

    /// <summary>
    /// Testable constructor: accepts the thin <see cref="IWorkItemFieldReader"/> seam
    /// and the <see cref="IWorkItemQueryWindowStrategy"/> abstraction.
    /// </summary>
    internal TfsWorkItemFetchService(
        IWorkItemFieldReader fieldReader,
        IWorkItemQueryWindowStrategy windowStrategy)
    {
        _fieldReader = fieldReader ?? throw new ArgumentNullException(nameof(fieldReader));
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

        var windowOptions = scope.BaseQuery is not null
            ? new WorkItemQueryWindowOptions { BaseQuery = scope.BaseQuery }
            : null;

        int totalYielded = 0;
        const int ProgressBatchSize = 200;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            endpoint, project, windowOptions, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var id in window.WorkItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var allFields = _fieldReader.GetFields(id);
                var fields = new Dictionary<string, object?>();

                foreach (var fieldName in scope.Fields)
                {
                    if (allFields.TryGetValue(fieldName, out var fieldValue))
                    {
                        fields[fieldName] = fieldValue;
                    }
                }

                var item = new FetchedWorkItem(id, fields);

                if (PassesFilters(item, scope.FilterOptions))
                {
                    yield return item;
                    totalYielded++;
                    if (totalYielded % ProgressBatchSize == 0)
                        scope.Progress?.Report(totalYielded);
                }
            }
        }

        // Final progress report for any remainder below the batch boundary.
        if (totalYielded % ProgressBatchSize != 0)
            scope.Progress?.Report(totalYielded);
    }

    internal static bool PassesFilters(FetchedWorkItem item, IReadOnlyList<WorkItemFieldFilterOptions>? filters) =>
        WorkItemFieldFilterEvaluator.PassesFilters(item, filters);

    /// <inheritdoc />
    public Task<ResumeDecision> EvaluateResumeDecisionAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default)
    {
        // TFS Object Model does not support resumable batching — always report Unavailable.
        return Task.FromResult(new ResumeDecision
        {
            Status = ResumeDecisionStatus.Unavailable,
            Reason = "tfs_object_model_unsupported"
        });
    }
}

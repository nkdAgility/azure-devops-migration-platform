// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Discovers work items and their revision counts for a single project.
/// Streams incremental <see cref="ProjectDiscoverySummary"/> snapshots as
/// each query window completes. The final snapshot has
/// <see cref="ProjectDiscoverySummary.IsWorkItemComplete"/> = <c>true</c>.
///
/// Used by both inventory (counting) and export (cataloguing work items to fetch).
/// </summary>
public interface IWorkItemDiscoveryService
{
    /// <summary>
    /// Streams incremental work-item discovery snapshots for <paramref name="project"/>.
    /// Fetches work item IDs and revision counts (<c>System.Rev</c>) for every work item found.
    /// <para>
    /// When <paramref name="scope"/> is non-null, the implementation merges
    /// <see cref="WorkItemFetchScope.Fields"/> with <c>["System.Rev"]</c> and passes
    /// <see cref="WorkItemFetchScope.FilterOptions"/> and <see cref="WorkItemFetchScope.BaseQuery"/>
    /// to the underlying <see cref="IWorkItemFetchService"/>. Items that do not pass the filter
    /// are not included in the returned snapshots.
    /// </para>
    /// </summary>
    /// <param name="progress">
    /// Optional per-batch callback receiving the cumulative work item count after each intermediate
    /// snapshot. Callers MUST wire this to <c>IProgressSink.Emit</c> in production code — passing
    /// <see langword="null"/> is only permitted in unit tests.
    /// </param>
    IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope? scope = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts work items matching <paramref name="baseQuery"/> (or all project work items when
    /// <see langword="null"/>) using the same date-window chunking as export.
    /// Cheaper than <see cref="DiscoverWorkItemsAsync"/>: counts IDs only — does not fetch
    /// <c>System.Rev</c> per work item.
    /// Streams incremental snapshots; the final snapshot has
    /// <see cref="ProjectDiscoverySummary.IsWorkItemComplete"/> = <c>true</c>.
    /// </summary>
    /// <param name="progress">
    /// Optional per-window callback receiving the cumulative work item count after each window
    /// completes. Callers MUST wire this to <c>IProgressSink.Emit</c> in production code — passing
    /// <see langword="null"/> is only permitted in unit tests.
    /// </param>
    IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

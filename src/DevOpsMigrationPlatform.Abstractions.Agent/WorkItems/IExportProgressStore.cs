// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Tracks per-work-item export progress so the export orchestrator can
/// skip already-written revisions on resume without per-revision <c>ExistsAsync</c> checks.
/// </summary>
public interface IExportProgressStore : IAsyncDisposable
{
    /// <summary>Creates the underlying schema if it does not already exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the stored progress for <paramref name="workItemId"/>, or
    /// <see langword="null"/> if the work item has never been recorded.
    /// </summary>
    Task<WorkItemExportProgress?> GetProgressAsync(int workItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the revision with the given <paramref name="rev"/> index has been
    /// successfully written for <paramref name="workItemId"/>.
    /// On resume, revisions with an index ≤ <paramref name="rev"/> will be skipped.
    /// </summary>
    Task SetRevAsync(int workItemId, int rev, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the total number of work items recorded in the store.
    /// Used at orchestrator startup to seed the initial skipped count so the progress
    /// display immediately reflects work already done in a prior run, even when the
    /// cursor has been reset or corrupted.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}

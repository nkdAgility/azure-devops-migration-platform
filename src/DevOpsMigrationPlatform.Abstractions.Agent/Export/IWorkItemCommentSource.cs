// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Abstraction for fetching work item comments from the source system.
/// Implementations may stream comments with pagination or version history.
/// </summary>
public interface IWorkItemCommentSource
{
    /// <summary>
    /// Retrieves comments (and optionally comment version history) for a work item.
    /// </summary>
    /// <param name="workItemId">The work item ID in the source system.</param>
    /// <param name="includeDeleted">If true, include soft-deleted comments; otherwise exclude them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An async enumerable of comments. May yield multiple versions of the same comment if version history is tracked.
    /// Implementations must stream results (no buffering entire result set into memory).
    /// </returns>
    IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

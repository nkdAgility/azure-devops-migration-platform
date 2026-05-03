// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

public record CursorEntry
{
    public string LastProcessed { get; init; } = string.Empty;
    public string Stage { get; init; } = CursorStage.Completed;
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Cumulative count of work items fully processed when this cursor was written.</summary>
    public int WorkItemsProcessed { get; init; }

    /// <summary>Cumulative count of revisions processed when this cursor was written.</summary>
    public int RevisionsProcessed { get; init; }

    /// <summary>ID of the last work item that was fully processed.</summary>
    public int LastWorkItemId { get; init; }

    /// <summary>
    /// Total work items in scope as counted at job start.
    /// Stored in the cursor so that resume runs skip the pre-flight count.
    /// </summary>
    public int TotalWorkItems { get; init; }
}

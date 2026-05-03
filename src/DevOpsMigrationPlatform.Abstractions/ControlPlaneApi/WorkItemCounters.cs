// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for work item processing within a migration job.
/// Used by <see cref="MigrationCounters"/> at both aggregate and per-project scope.
/// </summary>
public record WorkItemCounters
{
    /// <summary>Work items that entered the processing pipeline.</summary>
    public long Attempted { get; init; }

    /// <summary>Work items successfully completed.</summary>
    public long Completed { get; init; }

    /// <summary>Work items that failed permanently.</summary>
    public long Failed { get; init; }

    /// <summary>Work items skipped (e.g. already processed, filtered out).</summary>
    public long Skipped { get; init; }

    /// <summary>Total revisions processed across all work items.</summary>
    public long RevisionsProcessed { get; init; }

    /// <summary>
    /// Duration in milliseconds for the most recently completed work item.
    /// Used to detect back-off / throttling: a sudden spike indicates server-side rate limiting.
    /// </summary>
    public double LastWorkItemDurationMs { get; init; }

    /// <summary>
    /// Rolling average duration in milliseconds per completed work item.
    /// Baseline to compare against <see cref="LastWorkItemDurationMs"/>.
    /// </summary>
    public double AverageWorkItemDurationMs { get; init; }

    /// <summary>
    /// Number of revisions written for the most recently completed work item.
    /// </summary>
    public int LastWorkItemRevisions { get; init; }

    // ── In-flight work item (current) ────────────────────────────────────────────

    /// <summary>
    /// Azure DevOps ID of the work item currently being exported.
    /// Zero when no work item is in flight.
    /// </summary>
    public int CurrentWorkItemId { get; init; }

    /// <summary>
    /// 1-based ordinal position of <see cref="CurrentWorkItemId"/> in the overall
    /// export run (i.e. how many distinct work items have been started so far).
    /// Zero when no work item is in flight.
    /// </summary>
    public int CurrentWorkItemIndex { get; init; }

    /// <summary>
    /// Number of revisions written so far for <see cref="CurrentWorkItemId"/>.
    /// Increments with each revision and resets to zero when the next WI starts.
    /// </summary>
    public int CurrentWorkItemRevisionsWritten { get; init; }

    // ── Per-revision timing ───────────────────────────────────────────────────────

    /// <summary>
    /// Duration in milliseconds for the most recently written revision.
    /// </summary>
    public double LastRevisionDurationMs { get; init; }

    /// <summary>
    /// Rolling average duration in milliseconds per revision across all revisions written so far.
    /// </summary>
    public double AverageRevisionDurationMs { get; init; }

    // ── Last work item outcome ────────────────────────────────────────────────────

    /// <summary>
    /// Azure DevOps ID of the most recently resolved work item (completed, skipped, or failed).
    /// Zero until the first work item is resolved.
    /// </summary>
    public int LastWorkItemId { get; init; }

    /// <summary>
    /// Outcome of the most recently resolved work item.
    /// One of: <c>"Exported"</c>, <c>"Skipped"</c>, <c>"Failed"</c>.
    /// Null until the first work item is resolved.
    /// </summary>
    public string? LastWorkItemStatus { get; init; }

    /// <summary>Optional attachment counters. Null when no attachments have been processed.</summary>
    public AttachmentCounters? Attachments { get; init; }
}

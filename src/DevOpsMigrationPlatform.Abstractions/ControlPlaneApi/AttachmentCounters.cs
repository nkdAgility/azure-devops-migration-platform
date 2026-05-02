// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for attachment processing within a migration job.
/// Shared by <see cref="WorkItemCounters"/> (nested) and used at both
/// aggregate (<see cref="JobMetrics"/>) and per-project (<see cref="ProjectSnapshot"/>) scope.
/// </summary>
public record AttachmentCounters
{
    /// <summary>Attachments successfully processed.</summary>
    public long Processed { get; init; }

    /// <summary>Attachment downloads or uploads that failed.</summary>
    public long Failed { get; init; }

    /// <summary>Total bytes transferred for attachments.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Duration in milliseconds for the most recently completed attachment download.</summary>
    public double LastDownloadDurationMs { get; init; }

    /// <summary>Rolling average download duration in milliseconds across all completed attachments.</summary>
    public double AverageDownloadDurationMs { get; init; }

    /// <summary>Size in bytes of the most recently completed attachment download.</summary>
    public long LastSizeBytes { get; init; }

    /// <summary>Rolling average size in bytes across all completed attachments. Zero when none processed.</summary>
    public long AverageSizeBytes { get; init; }

    // ── In-flight attachment (current) ───────────────────────────────────────────

    /// <summary>
    /// File name of the attachment currently being downloaded.
    /// Null when no download is in flight.
    /// </summary>
    public string? CurrentAttachmentName { get; init; }
}

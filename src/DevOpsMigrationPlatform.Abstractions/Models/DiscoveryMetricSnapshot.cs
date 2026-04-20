using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Point-in-time metric aggregates for a running discovery job.
/// Properties correspond to registered OTel instruments in <see cref="WellKnownDiscoveryMetricNames"/>.
/// </summary>
public record DiscoveryMetricSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // --- Organisation ---
    public long OrganisationsCompleted { get; init; }
    public long OrganisationsFailed { get; init; }
    public double? OrganisationDurationMeanMs { get; init; }

    // --- Project ---
    public long ProjectsCompleted { get; init; }
    public long ProjectsFailed { get; init; }
    public double? ProjectDurationMeanMs { get; init; }

    // --- Inventory ---
    public long WorkItemsCounted { get; init; }
    public long RevisionsCounted { get; init; }
    public long ReposCounted { get; init; }

    // --- Dependencies ---
    public long LinksFound { get; init; }
    public long WorkItemsAnalysed { get; init; }

    // --- Operational ---
    public long CheckpointsSaved { get; init; }
    public int OrganisationsQueued { get; init; }
    public int ProjectsQueued { get; init; }
    public int JobsActive { get; init; }
}

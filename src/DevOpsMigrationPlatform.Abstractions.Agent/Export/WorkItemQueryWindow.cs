using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Options controlling the date-window algorithm.
/// </summary>
public sealed class WorkItemQueryWindowOptions
{
    public int InitialWindowDays { get; init; } = 120;
    public int LimitThreshold { get; init; } = 20_000;
    public int MinWindowDays { get; init; } = 1;
    /// <summary>
    /// Maximum window size in days used when skipping over empty periods.
    /// Defaults to 1825 days (5 years).
    /// </summary>
    public int MaxWindowDays { get; init; } = 1825;
    /// <summary>
    /// The earliest date the strategy will scan back to.
    /// Defaults to 30 years before the current UTC time.
    /// </summary>
    public DateTime MinDate { get; init; } = DateTime.UtcNow.AddYears(-30);

    /// <summary>
    /// Starting ID-range width for Level 2 cursor paging inside a single dense day.
    /// Applies only when the date window has shrunk to <see cref="MinWindowDays"/>
    /// and still overflows the WIQL limit. Defaults to 5 000.
    /// </summary>
    public int InitialIdWindowSize { get; init; } = 5_000;

    /// <summary>
    /// Optional user-supplied WIQL query whose WHERE predicate and ORDER BY clause
    /// are used when building windowed queries. The SELECT is always normalised to
    /// <c>SELECT [System.Id]</c>. Supports the <c>@project</c> WIQL macro.
    /// When <see langword="null"/>, a default project-scoped query is generated
    /// from the <c>project</c> argument passed to
    /// <see cref="IWorkItemQueryWindowStrategy.EnumerateWindowsAsync"/>.
    /// </summary>
    public string? BaseQuery { get; init; }

    // ── Resumable Batching (opt-in) ──────────────────────────────────────

    /// <summary>
    /// When <see langword="true"/>, the strategy attempts to resume from
    /// <see cref="SavedContinuationToken"/> instead of starting from scratch.
    /// Default: <see langword="false"/> — zero behavioral change for existing callers.
    /// </summary>
    public bool ResumeEnabled { get; init; }

    /// <summary>
    /// Continuation token from a prior run. Only inspected when
    /// <see cref="ResumeEnabled"/> is <see langword="true"/>.
    /// </summary>
    public BatchContinuationToken? SavedContinuationToken { get; init; }

    /// <summary>
    /// Query parameters included in the fingerprint computation.
    /// Excludes post-fetch filters — only enumeration-level parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string>? QueryParameters { get; init; }
}

/// <summary>
/// One window result yielded by an <see cref="IWorkItemQueryWindowStrategy"/>.
/// </summary>
public sealed class WorkItemQueryWindow
{
    public DateTime WindowStart { get; init; }
    public DateTime WindowEnd { get; init; }
    public TimeSpan WindowSize { get; init; }
    public IReadOnlyList<int> WorkItemIds { get; init; } = Array.Empty<int>();
}

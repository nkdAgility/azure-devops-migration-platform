using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Options controlling the date-window algorithm.
/// </summary>
public sealed class WorkItemQueryWindowOptions
{
    public int InitialWindowDays { get; set; } = 120;
    public int LimitThreshold { get; set; } = 20_000;
    public int MinWindowDays { get; set; } = 1;
    /// <summary>
    /// Maximum window size in days used when skipping over empty periods.
    /// Defaults to 1825 days (5 years).
    /// </summary>
    public int MaxWindowDays { get; set; } = 1825;
    /// <summary>
    /// The earliest date the strategy will scan back to.
    /// Defaults to 30 years before the current UTC time.
    /// </summary>
    public DateTime MinDate { get; set; } = DateTime.UtcNow.AddYears(-30);

    /// <summary>
    /// Optional user-supplied WIQL query whose WHERE predicate and ORDER BY clause
    /// are used when building windowed queries. The SELECT is always normalised to
    /// <c>SELECT [System.Id]</c>. Supports the <c>@project</c> WIQL macro.
    /// When <see langword="null"/>, a default project-scoped query is generated
    /// from the <c>project</c> argument passed to
    /// <see cref="IWorkItemQueryWindowStrategy.EnumerateWindowsAsync"/>.
    /// </summary>
    public string? BaseQuery { get; init; }
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

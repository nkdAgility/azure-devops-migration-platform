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

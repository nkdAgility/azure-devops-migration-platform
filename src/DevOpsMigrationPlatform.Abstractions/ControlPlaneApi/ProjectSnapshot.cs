namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Per-project state within an <see cref="OrgSnapshot"/>.
/// Counter types are shared with <see cref="JobMetrics"/> — same record types,
/// different scope (per-project here vs. aggregate in <c>JobMetrics</c>).
/// <para>
/// <see cref="MigrationCounters.Diagnostics"/> is always null at per-project scope —
/// OTel-derived means and in-flight gauges are not meaningful per-project.
/// </para>
/// </summary>
public record ProjectSnapshot
{
    /// <summary>Project name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current project processing status.</summary>
    public ProjectStatus Status { get; init; }

    /// <summary>Migration counters scoped to this project. Null for discovery jobs.</summary>
    public MigrationCounters? Migration { get; init; }

    /// <summary>Discovery counters scoped to this project. Null for migration jobs.</summary>
    public DiscoveryCounters? Discovery { get; init; }
}

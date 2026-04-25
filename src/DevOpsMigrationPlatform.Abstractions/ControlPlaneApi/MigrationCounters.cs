namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Migration-specific counters. Used at both aggregate (<see cref="JobMetrics.Migration"/>)
/// and per-project (<see cref="ProjectSnapshot.Migration"/>) scope.
/// <para>
/// At per-project scope, <see cref="Diagnostics"/> is always null — OTel-derived means
/// and in-flight gauges are not meaningful at individual project granularity.
/// </para>
/// </summary>
public record MigrationCounters
{
    /// <summary>Work item processing counters.</summary>
    public WorkItemCounters WorkItems { get; init; } = new();

    /// <summary>
    /// OTel-derived diagnostic means and correctness counters.
    /// Populated only at aggregate scope (<see cref="JobMetrics"/>); null at per-project scope.
    /// </summary>
    public MigrationDiagnostics? Diagnostics { get; init; }
}

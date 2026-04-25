namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Discovery-specific counters. Used at both aggregate (<see cref="JobMetrics.Discovery"/>)
/// and per-project (<see cref="ProjectSnapshot.Discovery"/>) scope.
/// Null for migration jobs.
/// </summary>
public record DiscoveryCounters
{
    /// <summary>Inventory counters. Null until inventory phase begins.</summary>
    public InventoryCounters? Inventory { get; init; }

    /// <summary>Dependency analysis counters. Null until dependency phase begins.</summary>
    public DependencyCounters? Dependencies { get; init; }
}

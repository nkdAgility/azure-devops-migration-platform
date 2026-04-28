namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for classification node (area/iteration) processing within a migration job.
/// Used by <see cref="MigrationCounters"/> at aggregate scope.
/// </summary>
public record NodesCounters
{
    /// <summary>Area nodes successfully created or verified in the target system.</summary>
    public long AreaPathsReplicated { get; init; }

    /// <summary>Iteration nodes successfully created or verified in the target system.</summary>
    public long IterationPathsReplicated { get; init; }

    /// <summary>Nodes that failed permanently during replication.</summary>
    public long Failed { get; init; }
}

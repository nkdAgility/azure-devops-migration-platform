namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for dependency analysis discovery operations.
/// Used by <see cref="DiscoveryCounters"/> at both aggregate and per-project scope.
/// </summary>
public record DependencyCounters
{
    /// <summary>Work items analysed for dependency links.</summary>
    public long WorkItemsAnalysed { get; init; }

    /// <summary>External links discovered.</summary>
    public long ExternalLinksFound { get; init; }

    /// <summary>Cross-project links discovered.</summary>
    public long CrossProjectLinks { get; init; }

    /// <summary>Cross-organisation links discovered.</summary>
    public long CrossOrgLinks { get; init; }

    /// <summary>Number of checkpoint saves during dependency analysis.</summary>
    public long CheckpointsSaved { get; init; }
}

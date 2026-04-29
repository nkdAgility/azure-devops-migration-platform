namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for identity processing within a migration job.
/// Used by <see cref="MigrationCounters"/> at aggregate scope.
/// </summary>
public record IdentitiesCounters
{
    /// <summary>Identity descriptors successfully written to the package during export.</summary>
    public long Exported { get; init; }

    /// <summary>Identities successfully resolved to target accounts during import.</summary>
    public long Resolved { get; init; }

    /// <summary>Identities that could not be mapped to a target account.</summary>
    public long Unresolved { get; init; }

    /// <summary>Identities that failed permanently during export or import.</summary>
    public long Failed { get; init; }
}

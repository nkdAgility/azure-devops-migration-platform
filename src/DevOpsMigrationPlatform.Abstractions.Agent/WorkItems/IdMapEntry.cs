namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Represents a single source-to-target work item ID mapping stored in <c>Checkpoints/idmap.db</c>.
/// </summary>
public record IdMapEntry
{
    /// <summary>Source work item ID.</summary>
    public int SourceId { get; init; }

    /// <summary>Target work item ID.</summary>
    public int TargetId { get; init; }
}

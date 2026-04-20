namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Represents a single source-to-target work item ID mapping stored in <c>Checkpoints/idmap.db</c>.
/// </summary>
public record IdMapEntry
{
    /// <summary>Source work item ID.</summary>
    public int SourceId { get; init; }

    /// <summary>Target work item ID.</summary>
    public int TargetId { get; init; }

    /// <summary>
    /// The highest revision index that has been successfully imported for this work item,
    /// or <see langword="null"/> if revision-level tracking has not yet been recorded.
    /// Updated monotonically — never decremented.
    /// </summary>
    public int? LastRevisionIndex { get; init; }
}

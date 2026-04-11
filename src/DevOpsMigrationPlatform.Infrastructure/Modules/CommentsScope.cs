namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Configuration scope for work item comments export functionality.
/// </summary>
public sealed class CommentsScope
{
    /// <summary>
    /// Configuration section name for binding from appSettings.json.
    /// </summary>
    public const string SectionName = "Comments";

    /// <summary>
    /// Enables or disables work item comments export.
    /// Default: true (enabled).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// If true, include soft-deleted comments in the export.
    /// If false, exclude deleted comments.
    /// Default: false (exclude deleted).
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;
}

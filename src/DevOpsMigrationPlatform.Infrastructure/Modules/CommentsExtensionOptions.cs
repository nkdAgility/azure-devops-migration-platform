namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Options for the WorkItems Comments sub-module extension.
/// Controls whether comment versions are fetched and exported.
/// </summary>
public sealed class CommentsExtensionOptions
{
    /// <summary>
    /// Enables or disables the Comments extension.
    /// Default: <c>true</c> (enabled).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, include soft-deleted comments (<c>isDeleted: true</c>) in the export.
    /// Default: <c>false</c> (exclude deleted).
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;
}

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Comments extension options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Extensions:Comments</c>.
/// </summary>
public sealed class CommentsExtensionOptionsConfig : EnabledExtensionOptions
{
    /// <summary>When true, include soft-deleted comments. Default: false.</summary>
    public bool IncludeDeleted { get; init; } = false;
}

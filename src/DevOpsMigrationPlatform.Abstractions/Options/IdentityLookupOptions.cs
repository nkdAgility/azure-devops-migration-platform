namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the IdentityLookup tool.
/// Bound from <c>MigrationPlatform:Tools:IdentityLookup</c>.
/// </summary>
#if NET7_0_OR_GREATER
public sealed class IdentityLookupOptions : IConfigSection
#else
public sealed class IdentityLookupOptions
#endif
{
    /// <summary>Configuration section path.</summary>
    public static string SectionName => "MigrationPlatform:Tools:IdentityLookup";

    /// <summary>
    /// Master switch. When <c>false</c>, all identity resolution returns the source identity unchanged.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Fallback identity applied when no mapping override is found and no automatic match succeeds.
    /// When empty, the source identity is returned unchanged.
    /// </summary>
    public string? DefaultIdentity { get; init; }
}

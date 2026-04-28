#if !NET481
namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Options for the IdentitiesModule.</summary>
public sealed class IdentitiesModuleOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MigrationPlatform:Modules:Identities";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Default identity to use when an identity cannot be resolved.
    /// Falls back to the source identity string when empty.
    /// </summary>
    public string DefaultIdentity { get; init; } = string.Empty;
}
#endif

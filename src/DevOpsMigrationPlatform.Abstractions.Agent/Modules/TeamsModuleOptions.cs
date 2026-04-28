#if !NET481
namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Controls which extensions are enabled in the TeamsModule.</summary>
public sealed class TeamsModuleExtensionsOptions
{
    /// <summary>Export/import team settings (backlog level, bugs behaviour, working days).</summary>
    public bool TeamSettings { get; init; } = true;

    /// <summary>Record team area/iteration paths into ReferencedPathTracker during export.</summary>
    public bool NodeTranslation { get; init; } = true;

    /// <summary>Export/import team iteration assignments.</summary>
    public bool TeamIterations { get; init; } = true;

    /// <summary>Export/import team members with admin flags.</summary>
    public bool TeamMembers { get; init; } = true;

    /// <summary>Export/import per-member per-sprint capacity data.</summary>
    public bool TeamCapacity { get; init; } = true;
}

/// <summary>Options for the TeamsModule.</summary>
public sealed class TeamsModuleOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MigrationPlatform:Modules:Teams";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Scope type: <c>"all"</c> (default) exports all teams;
    /// <c>"teams"</c> exports only teams matching <see cref="Filter"/>.
    /// </summary>
    public string Scope { get; init; } = "all";

    /// <summary>
    /// Optional case-insensitive regex filter applied to team names when <see cref="Scope"/> is <c>"teams"</c>.
    /// An empty string matches all teams.
    /// </summary>
    public string Filter { get; init; } = string.Empty;

    /// <summary>Controls which extensions are active for this module.</summary>
    public TeamsModuleExtensionsOptions Extensions { get; init; } = new();
}
#endif

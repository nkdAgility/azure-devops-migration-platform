// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

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

    /// <summary>Resolve team member identities via <c>IdentityTranslationTool</c>.</summary>
    public bool IdentityLookup { get; init; } = true;

    /// <summary>Export/import per-member per-sprint capacity data.</summary>
    public bool TeamCapacity { get; init; } = true;
}

/// <summary>Options for the TeamsModule.</summary>
#if NET7_0_OR_GREATER
public sealed class TeamsModuleOptions : IConfigSection
#else
public sealed class TeamsModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When <see langword="false"/> (default), a team whose <c>Teams/{slug}/team.json</c>
    /// artefact already exists in the package is skipped on re-run — supporting
    /// resumable exports without re-fetching from the source.
    /// Set to <see langword="true"/> to force a fresh export of every team regardless
    /// of whether its artefact is already present.
    /// </summary>
    public bool AlwaysExport { get; init; } = false;

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

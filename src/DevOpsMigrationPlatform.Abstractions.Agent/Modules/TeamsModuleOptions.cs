// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Selection aspect for the TeamsModule — which teams to migrate.</summary>
public sealed class TeamsSelectionOptions
{
    /// <summary>Scope type: <c>"all"</c> (default) or <c>"teams"</c> (apply <see cref="Filter"/>).</summary>
    public string Scope { get; init; } = "all";

    /// <summary>Optional case-insensitive regex filter applied to team names when Scope is <c>"teams"</c>.</summary>
    public string Filter { get; init; } = string.Empty;
}

/// <summary>Data aspect for the TeamsModule — which team payloads to carry.</summary>
public sealed class TeamsDataOptions
{
    /// <summary>Export/import team settings (backlog level, bugs behaviour, working days).</summary>
    public bool TeamSettings { get; init; } = true;

    /// <summary>Export/import team iteration assignments.</summary>
    public bool TeamIterations { get; init; } = true;

    /// <summary>Export/import team members with admin flags.</summary>
    public bool TeamMembers { get; init; } = true;

    /// <summary>Export/import per-member per-sprint capacity data.</summary>
    public bool TeamCapacity { get; init; } = true;
}

/// <summary>Processing aspect for the TeamsModule — runtime behaviour policies.</summary>
public sealed class TeamsProcessingOptions
{
    /// <summary>Force fresh export of every team even when its package artefact exists. Default: false (resumable).</summary>
    public bool AlwaysExport { get; init; } = false;

    /// <summary>Record team area/iteration paths into ReferencedPathTracker during export (NodeTranslation seam).</summary>
    public bool NodeTranslation { get; init; } = true;

    /// <summary>Resolve team member identities via <c>IdentityTranslationTool</c> (IdentityLookup seam).</summary>
    public bool IdentityLookup { get; init; } = true;
}

/// <summary>Options for the TeamsModule (ConfigVersion 2.0 anatomy).</summary>
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

    /// <summary>Selection aspect: team scope and name filter.</summary>
    public TeamsSelectionOptions Selection { get; init; } = new();

    /// <summary>Data aspect: settings, iterations, members, capacity.</summary>
    public TeamsDataOptions Data { get; init; } = new();

    /// <summary>Processing aspect: re-export, node translation, identity lookup.</summary>
    public TeamsProcessingOptions Processing { get; init; } = new();
}

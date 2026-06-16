// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Config for the board-config extension.
/// Bound via <c>IOptions&lt;BoardConfigExtensionOptions&gt;</c> — not nested in a shared module god-object.
/// </summary>
public sealed class BoardConfigExtensionOptions
{
    /// <summary>Configuration section path for binding.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams:Extensions:BoardConfig";

    /// <summary>Optional extension — carries Enabled (a mandatory extension would not).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Export/import Kanban board columns.</summary>
    public bool Columns { get; init; } = true;

    /// <summary>Export/import board swimlanes (rows).</summary>
    public bool SwimLanes { get; init; } = true;

    /// <summary>Export/import card rule settings (colour-coding).</summary>
    public bool CardRules { get; init; } = true;

    /// <summary>Export backlog display name and WIT category metadata.</summary>
    public bool Backlogs { get; init; } = true;

    /// <summary>Export/import sprint taskboard columns.</summary>
    public bool TaskboardColumns { get; init; } = true;

    /// <summary>
    /// Import strategy applied uniformly to all board config types.
    /// Replace (default): overwrite target with package values.
    /// Merge: overlay package values; preserve target-only entries.
    /// Skip: leave target unchanged.
    /// </summary>
    public BoardConfigImportMode ImportMode { get; init; } = BoardConfigImportMode.Replace;
}

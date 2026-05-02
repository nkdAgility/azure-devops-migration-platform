// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Describes one work item type to generate in a simulated project.
/// </summary>
public sealed class SimulatedWorkItemTypeConfig
{
    /// <summary>Work item type name (e.g. <c>"User Story"</c>, <c>"Bug"</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Number of work items to generate. 0 means skip this type.</summary>
    public int Count { get; set; }

    /// <summary>
    /// Number of revisions per work item. Must be ≥ 1.
    /// Validated at job startup — 0 causes an <see cref="System.InvalidOperationException"/>.
    /// </summary>
    public int RevisionsPerItem { get; set; } = 1;
}

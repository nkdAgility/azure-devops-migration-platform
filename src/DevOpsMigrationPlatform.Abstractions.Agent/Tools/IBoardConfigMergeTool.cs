// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Canonical board-configuration merge/validation Tool (ADR-0024, EC-M4).
/// Pure, deterministic, connector-agnostic: computes merged board configuration and
/// validates state mappings against the target process. Policy (import modes, logging,
/// progress) stays with the consuming extension; this seam owns the engine only.
/// </summary>
public interface IBoardConfigMergeTool
{
    /// <summary>
    /// Builds a map of work-item type → set of valid state names from the current
    /// target board columns. Keys and values are case-insensitive.
    /// </summary>
    IReadOnlyDictionary<string, ISet<string>> BuildValidStatesMap(IReadOnlyList<BoardColumn>? targetColumns);

    /// <summary>
    /// Omits state mappings referencing states absent from the target process (FR-013).
    /// Returns the filtered columns together with the omitted mappings so the caller
    /// can apply its warning policy. When <paramref name="validStates"/> is empty the
    /// input columns are returned unchanged.
    /// </summary>
    BoardColumnValidationResult FilterInvalidStateMappings(
        IReadOnlyList<BoardColumn> columns,
        IReadOnlyDictionary<string, ISet<string>> validStates);

    /// <summary>
    /// Merges package items with target items by case-insensitive key: package items
    /// override target items with the same key; target-only items are appended.
    /// </summary>
    IReadOnlyList<T> MergeByName<T>(
        IReadOnlyList<T>? packageItems,
        IReadOnlyList<T>? targetItems,
        Func<T, string> keySelector);
}

/// <summary>A state mapping omitted by board-config validation (FR-013).</summary>
public sealed record OmittedStateMapping(string ColumnName, string WorkItemType, string State);

/// <summary>Result of board-column state-mapping validation.</summary>
public sealed record BoardColumnValidationResult(
    IReadOnlyList<BoardColumn> Columns,
    IReadOnlyList<OmittedStateMapping> OmittedMappings);

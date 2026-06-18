// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Captures the current board configuration state of a target team.
/// Fetched once before import writes begin so that all target reads are batched up-front.
/// </summary>
public sealed record TargetBoardSnapshot
{
    /// <summary>Names of boards that already exist on the target team.</summary>
    public ISet<string> BoardNames { get; init; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Current columns per board, keyed by board name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BoardColumn>> BoardColumns { get; init; }
        = new Dictionary<string, IReadOnlyList<BoardColumn>>();

    /// <summary>Current swim lanes per board, keyed by board name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BoardSwimLane>> BoardSwimLanes { get; init; }
        = new Dictionary<string, IReadOnlyList<BoardSwimLane>>();

    /// <summary>Current taskboard columns on the target team. Empty when not present.</summary>
    public IReadOnlyList<TaskboardColumn> TaskboardColumns { get; init; }
        = new List<TaskboardColumn>();

    /// <summary>Empty snapshot — used when no target state is needed (e.g., Replace mode, capability absent).</summary>
    public static readonly TargetBoardSnapshot Empty = new();
}

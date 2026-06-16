// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Configuration for one Kanban board (one per backlog level).
/// Contains at least 2 columns (Incoming + Outgoing).
/// </summary>
public sealed record BoardConfig(
    string BoardName,
    IReadOnlyList<BoardColumn> Columns,
    IReadOnlyList<BoardSwimLane> SwimLanes);

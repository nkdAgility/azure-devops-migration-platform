// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent;

/// <summary>
/// Flags enum describing the board-configuration capabilities a connector exposes.
/// TFS connectors declare <see cref="None"/> explicitly — no null-guards in extension code.
/// </summary>
[Flags]
public enum ConnectorCapability
{
    /// <summary>No board-configuration capability (e.g. TFS Object Model).</summary>
    None = 0,

    /// <summary>Connector can read and write Kanban board columns.</summary>
    BoardColumns = 1 << 0,

    /// <summary>Connector can read and write Kanban board swim lanes (rows).</summary>
    BoardRows = 1 << 1,

    /// <summary>Connector can read and write card rule settings (colour-coding).</summary>
    CardRules = 1 << 2,

    /// <summary>Connector exposes backlog level metadata (name, WIT category, rank).</summary>
    Backlogs = 1 << 3,

    /// <summary>Connector can read and write sprint taskboard column definitions.</summary>
    TaskboardColumns = 1 << 4,

    /// <summary>Connector can read and write team settings (backlog levels, bug behaviour, working days).</summary>
    TeamSettings = 1 << 5,

    /// <summary>Connector can read and assign team iterations (sprints).</summary>
    TeamIterations = 1 << 6,

    /// <summary>Connector can read and add team members.</summary>
    TeamMembers = 1 << 7,

    /// <summary>Connector can read and write per-member iteration capacity.</summary>
    TeamCapacity = 1 << 8,

    /// <summary>Connector can read and assign team area paths.</summary>
    TeamAreaPaths = 1 << 9,

    /// <summary>Connector can read and replay work-item comments.</summary>
    WorkItemComments = 1 << 10,

    /// <summary>
    /// Composite: full Kanban board configuration (columns + rows + card rules).
    /// Equivalent to <see cref="BoardColumns"/> | <see cref="BoardRows"/> | <see cref="CardRules"/>.
    /// </summary>
    BoardConfig = BoardColumns | BoardRows | CardRules,

    /// <summary>
    /// Composite: full team capability surface (settings + iterations + members + capacity + area paths).
    /// </summary>
    TeamCapabilities = TeamSettings | TeamIterations | TeamMembers | TeamCapacity | TeamAreaPaths,
}

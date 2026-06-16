// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Top-level package model for a team's board configuration.
/// Serialized to Teams/{slug}/board-config.json.
/// </summary>
public sealed class TeamBoardConfig
{
    /// <summary>The team name. Must not be null or whitespace.</summary>
    public required string TeamName { get; init; }

    /// <summary>When this configuration was exported.</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>One entry per Kanban board (one per backlog level). Never null; may be empty.</summary>
    public required IReadOnlyList<BoardConfig> Boards { get; init; }

    /// <summary>Card colour-coding rules. Null if the connector does not support card rules.</summary>
    public CardRuleSettings? CardRules { get; init; }

    /// <summary>Backlog level metadata. Never null; may be empty.</summary>
    public required IReadOnlyList<BacklogMetadata> Backlogs { get; init; }

    /// <summary>Sprint taskboard columns. Never null; may be empty.</summary>
    public required IReadOnlyList<TaskboardColumn> TaskboardColumns { get; init; }
}

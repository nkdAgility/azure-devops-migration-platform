// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Display name and WIT category for a backlog level.
/// Visibility flags are NOT stored here — they live in TeamSettings.
/// </summary>
public sealed record BacklogMetadata(
    /// <summary>Display name, e.g. "Stories".</summary>
    string Name,
    /// <summary>WIT category reference name, e.g. "Microsoft.RequirementCategory".</summary>
    string WitCategory,
    /// <summary>Backlog level type (portfolio / requirement / task).</summary>
    BacklogLevelType LevelType,
    /// <summary>Ordering within the backlog levels. Task backlog = 0.</summary>
    int Rank);

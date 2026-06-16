// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A Kanban board column definition.</summary>
public sealed record BoardColumn(
    string Name,
    BoardColumnType ColumnType,
    int? ItemLimit,
    bool IsSplit,
    string? Description,
    IReadOnlyList<BoardColumnStateMapping> StateMappings);

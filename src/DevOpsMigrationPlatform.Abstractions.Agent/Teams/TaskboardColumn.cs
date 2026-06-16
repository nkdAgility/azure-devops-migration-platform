// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A sprint taskboard column definition.</summary>
public sealed record TaskboardColumn(
    string Name,
    BoardColumnType ColumnType,
    int Order,
    IReadOnlyList<BoardColumnStateMapping> StateMappings);

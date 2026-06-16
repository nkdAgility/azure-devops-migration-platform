// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A single colour-coding rule applied to cards on a Kanban board.</summary>
public sealed record CardRule(
    string Name,
    string? Color,
    bool IsEnabled,
    /// <summary>Raw filter expression, e.g. "[Priority] = 1". Validated at import time.</summary>
    string Filter);

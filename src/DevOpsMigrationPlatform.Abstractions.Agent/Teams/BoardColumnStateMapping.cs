// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Maps a work item type to a workflow state for a Kanban board column.</summary>
public sealed record BoardColumnStateMapping(
    string WorkItemType,
    string State);

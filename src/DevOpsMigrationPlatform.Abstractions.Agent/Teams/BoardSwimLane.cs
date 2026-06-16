// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// A horizontal row (swimlane) on a Kanban board.
/// Mirrors <c>Microsoft.TeamFoundation.Work.WebApi.BoardRow</c>, which exposes only Id and Name.
/// </summary>
public sealed record BoardSwimLane(
    /// <summary>Source-only metadata (ADO BoardRow.Id). NOT used as an import key.</summary>
    string? Id,
    /// <summary>Portable key — the lane name is the durable identity across systems.</summary>
    string Name);

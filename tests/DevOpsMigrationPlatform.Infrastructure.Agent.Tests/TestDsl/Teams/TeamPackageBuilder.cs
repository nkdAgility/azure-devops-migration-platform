// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Teams;

/// <summary>
/// Static factory methods for constructing <see cref="TeamPackage"/> instances in tests.
/// </summary>
internal static class TeamPackageBuilder
{
    /// <summary>
    /// Creates a <see cref="TeamPackage"/> with the supplied area paths and minimal other state.
    /// </summary>
    internal static TeamPackage WithAreaPaths(
        string teamId,
        string teamName,
        TeamAreaPaths areaPaths)
        => new()
        {
            Definition = new TeamDefinition(teamId, teamName, "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            AreaPaths = areaPaths,
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
}

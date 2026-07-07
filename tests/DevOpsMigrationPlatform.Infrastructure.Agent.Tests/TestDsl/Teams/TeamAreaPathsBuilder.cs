// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Teams;

/// <summary>
/// Fluent construction of <see cref="TeamAreaPaths"/> value objects for tests.
/// </summary>
internal static class TeamAreaPathsBuilder
{
    /// <summary>
    /// Creates a <see cref="TeamAreaPaths"/> with the given default path and a single
    /// additional entry (includeChildren=true unless stated otherwise).
    /// </summary>
    internal static TeamAreaPaths WithDefaultAndOneIncluded(
        string defaultPath,
        string includedPath,
        bool includeChildren = true)
        => new(defaultPath, new List<TeamFieldValueEntry> { new(includedPath, includeChildren) });

    /// <summary>
    /// Creates a <see cref="TeamAreaPaths"/> where the default and the sole entry are the same value.
    /// Models the "default-only" pattern used when only a default path exists.
    /// </summary>
    internal static TeamAreaPaths WithDefaultOnly(string defaultPath)
        => new(defaultPath, new List<TeamFieldValueEntry> { new(defaultPath) });
}

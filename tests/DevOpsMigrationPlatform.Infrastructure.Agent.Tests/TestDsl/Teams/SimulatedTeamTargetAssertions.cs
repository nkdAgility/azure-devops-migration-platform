// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Teams;

/// <summary>
/// Assertion helpers targeting <see cref="SimulatedTeamTarget.AreaPaths"/> state.
/// </summary>
internal static class SimulatedTeamTargetAssertions
{
    /// <summary>
    /// Asserts that <c>SetAreaPathsAsync</c> was never called for any team.
    /// </summary>
    internal static void AreaPathsNotCalled(SimulatedTeamTarget target, string because = "")
    {
        Assert.AreEqual(
            0,
            target.AreaPaths.Count,
            $"SetAreaPathsAsync should not have been called{(string.IsNullOrEmpty(because) ? "" : " because " + because)}.");
    }

    /// <summary>
    /// Asserts that <c>SetAreaPathsAsync</c> was called for at least one team.
    /// </summary>
    internal static void AreaPathsWereCalled(SimulatedTeamTarget target, string because = "")
    {
        Assert.IsTrue(
            target.AreaPaths.Count > 0,
            $"SetAreaPathsAsync should have been called{(string.IsNullOrEmpty(because) ? "" : " because " + because)}.");
    }

    /// <summary>
    /// Asserts that the given <paramref name="excludedPath"/> is absent from
    /// the <c>IncludedAreaPaths</c> of the first team in the target.
    /// </summary>
    internal static void AreaPathsExclude(
        SimulatedTeamTarget target,
        string excludedPath,
        string because = "")
    {
        Assert.AreEqual(1, target.AreaPaths.Count, "Exactly one team should have area paths set.");
        var teamId = new List<string>(target.AreaPaths.Keys)[0];
        var included = target.AreaPaths[teamId].IncludedAreaPaths;
        Assert.IsFalse(
            included.Contains(excludedPath, StringComparer.OrdinalIgnoreCase),
            $"IncludedAreaPaths should not contain '{excludedPath}'" +
            $"{(string.IsNullOrEmpty(because) ? "" : " because " + because)}.");
    }
}

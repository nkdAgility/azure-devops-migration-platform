// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// EC-L1 / ADR-0024: the <see cref="ITeamTarget"/> seam must not require callers to
/// supply a <c>MigrationEndpointOptions</c> endpoint parameter. Connector
/// implementations resolve their own target endpoint via <c>ITargetEndpointInfo</c>;
/// no caller may forge the endpoint with <c>null!</c>.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public sealed class TeamTargetSeamContractTests
{
    [TestMethod]
    public void ITeamTarget_Methods_DoNotTakeMigrationEndpointOptions()
    {
        var offenders = typeof(ITeamTarget).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType.Name == "MigrationEndpointOptions"))
            .Select(m => m.Name)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"ITeamTarget methods must not take MigrationEndpointOptions (EC-L1): {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void TeamsInfrastructure_DoesNotForgeEndpointArgumentsWithNullForgiveness()
    {
        var repoRoot = FindRepoRoot();
        var teamsDir = Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.Infrastructure.Agent", "Teams");
        var offenders = Directory.EnumerateFiles(teamsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("Async(null!"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"Teams infrastructure must not forge ITeamTarget endpoint arguments with null! (EC-L1): {string.Join(", ", offenders)}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repository root.");
        return dir!.FullName;
    }
}

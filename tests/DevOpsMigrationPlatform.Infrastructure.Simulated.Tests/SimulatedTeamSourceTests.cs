// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests;

[TestClass]
public class SimulatedTeamSourceTests
{
    [TestMethod]
    public async Task EnumerateTeamsAsync_ReturnsDeterministicTeams()
    {
        // Arrange
        var source = new SimulatedTeamSource();

        // Act — enumerate twice to verify determinism
        var first = await CollectTeamsAsync(source, "ProjectA");
        var second = await CollectTeamsAsync(source, "ProjectA");

        // Assert
        Assert.AreEqual(first.Count, second.Count, "Same team count on every run");
        for (var i = 0; i < first.Count; i++)
            Assert.AreEqual(first[i].Id, second[i].Id, $"Id mismatch at index {i}");
    }

    [TestMethod]
    public async Task EnumerateTeamsAsync_AllTeamsHaveRequiredFields()
    {
        // Arrange
        var source = new SimulatedTeamSource();

        // Act
        var teams = await CollectTeamsAsync(source, "ProjectA");

        // Assert
        Assert.IsTrue(teams.Count > 0, "At least one team expected");
        foreach (var t in teams)
        {
            Assert.IsFalse(string.IsNullOrEmpty(t.Id), "Team.Id should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(t.Name), "Team.Name should not be empty");
        }
    }

    [TestMethod]
    public async Task EnumerateTeamsAsync_ContainsDefaultTeam()
    {
        // Arrange
        var source = new SimulatedTeamSource();

        // Act
        var teams = await CollectTeamsAsync(source, "ProjectA");

        // Assert — at least one team must be flagged as default
        Assert.IsTrue(teams.Exists(t => t.IsDefault), "Expected at least one default team");
    }

    [TestMethod]
    public async Task GetTeamSettingsAsync_ReturnsSettings()
    {
        // Arrange
        var source = new SimulatedTeamSource();
        var teams = await CollectTeamsAsync(source, "ProjectA");

        // Act
        var settings = await source.GetTeamSettingsAsync("ProjectA", teams[0].Id, CancellationToken.None);

        // Assert
        Assert.IsNotNull(settings, "Settings should not be null");
        Assert.IsFalse(string.IsNullOrEmpty(settings!.BacklogNavigationLevel), "BacklogNavigationLevel should be set");
    }

    [TestMethod]
    public async Task GetTeamIterationsAsync_ReturnsIterations()
    {
        // Arrange
        var source = new SimulatedTeamSource();
        var teams = await CollectTeamsAsync(source, "ProjectA");
        var iterations = new List<TeamIteration>();

        // Act
        await foreach (var it in source.GetTeamIterationsAsync("ProjectA", teams[0].Id, CancellationToken.None))
            iterations.Add(it);

        // Assert
        Assert.IsTrue(iterations.Count > 0, "At least one iteration expected");
        foreach (var it in iterations)
        {
            Assert.IsFalse(string.IsNullOrEmpty(it.Id), "Iteration.Id should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(it.Path), "Iteration.Path should not be empty");
        }
    }

    [TestMethod]
    public async Task GetTeamMembersAsync_ReturnsMembers()
    {
        // Arrange
        var source = new SimulatedTeamSource();
        var teams = await CollectTeamsAsync(source, "ProjectA");
        var members = new List<TeamMember>();

        // Act
        await foreach (var m in source.GetTeamMembersAsync("ProjectA", teams[0].Id, CancellationToken.None))
            members.Add(m);

        // Assert
        Assert.IsTrue(members.Count > 0, "At least one member expected");
        foreach (var m in members)
        {
            Assert.IsFalse(string.IsNullOrEmpty(m.Descriptor), "Member.Descriptor should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(m.UniqueName), "Member.UniqueName should not be empty");
        }
    }

    [TestMethod]
    public async Task GetTeamAreaPathsAsync_ReturnsAreaPaths()
    {
        // Arrange
        var source = new SimulatedTeamSource();
        var teams = await CollectTeamsAsync(source, "ProjectA");

        // Act
        var areaPaths = await source.GetTeamAreaPathsAsync("ProjectA", teams[0].Id, CancellationToken.None);

        // Assert
        Assert.IsNotNull(areaPaths, "AreaPaths should not be null");
        Assert.IsFalse(string.IsNullOrEmpty(areaPaths!.DefaultAreaPath), "DefaultAreaPath should not be empty");
    }

    private static async Task<List<TeamDefinition>> CollectTeamsAsync(SimulatedTeamSource source, string project)
    {
        var result = new List<TeamDefinition>();
        await foreach (var t in source.EnumerateTeamsAsync(project, CancellationToken.None))
            result.Add(t);
        return result;
    }
}

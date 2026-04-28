using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests;

[TestClass]
public class SimulatedTeamTargetTests
{
    [TestMethod]
    public async Task CreateOrUpdateTeamAsync_StoresTeamAndReturnsId()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var team = new TeamDefinition("src-id", "Alpha Team", "Desc", false);

        // Act
        var returnedId = await target.CreateOrUpdateTeamAsync("Project", team, CancellationToken.None);

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(returnedId), "Returned ID should not be empty");
        Assert.IsTrue(target.Teams.ContainsKey(returnedId), "Team should be stored under returned ID");
        Assert.AreEqual("Alpha Team", target.Teams[returnedId].Name);
    }

    [TestMethod]
    public async Task SetTeamSettingsAsync_StoresSettings()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var settings = new TeamSettings("Sprint 1", false, new[] { "Monday", "Wednesday" });

        // Act
        await target.SetTeamSettingsAsync("Project", "team-1", settings, CancellationToken.None);

        // Assert
        Assert.IsTrue(target.TeamSettings.ContainsKey("team-1"));
        Assert.AreEqual("Sprint 1", target.TeamSettings["team-1"].BacklogNavigationLevel);
    }

    [TestMethod]
    public async Task AssignIterationAsync_AccumulatesIterations()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var it1 = new TeamIteration("iter-1", "ProjectA\\Sprint 1", "Sprint 1", null, null, false, false);
        var it2 = new TeamIteration("iter-2", "ProjectA\\Sprint 2", "Sprint 2", null, null, false, false);

        // Act
        await target.AssignIterationAsync("Project", "team-1", it1, CancellationToken.None);
        await target.AssignIterationAsync("Project", "team-1", it2, CancellationToken.None);

        // Assert
        Assert.AreEqual(2, target.Iterations["team-1"].Count);
    }

    [TestMethod]
    public async Task AddMemberAsync_AccumulatesMembers()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var m1 = new TeamMember("descriptor-1", "User One", "user1@test.com", false);
        var m2 = new TeamMember("descriptor-2", "User Two", "user2@test.com", true);

        // Act
        await target.AddMemberAsync("Project", "team-1", m1, CancellationToken.None);
        await target.AddMemberAsync("Project", "team-1", m2, CancellationToken.None);

        // Assert
        Assert.AreEqual(2, target.Members["team-1"].Count);
        Assert.IsTrue(target.Members["team-1"].Exists(m => m.IsAdmin));
    }

    [TestMethod]
    public async Task SetCapacityAsync_StoresCapacityUnderCompositeKey()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var entries = new[]
        {
            new TeamCapacityEntry("descriptor-1", "User One", new[] { new ActivityEntry("Dev", 6) }, 0),
            new TeamCapacityEntry("descriptor-2", "User Two", new[] { new ActivityEntry("Test", 4) }, 1)
        };

        // Act
        await target.SetCapacityAsync("Project", "team-1", "sprint-1", entries, CancellationToken.None);

        // Assert
        var key = "team-1/sprint-1";
        Assert.IsTrue(target.Capacity.ContainsKey(key));
        Assert.AreEqual(2, target.Capacity[key].Length);
    }

    [TestMethod]
    public async Task SetAreaPathsAsync_StoresAreaPaths()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var areaPaths = new TeamAreaPaths("ProjectA", new[] { "ProjectA", "ProjectA\\Sub" });

        // Act
        await target.SetAreaPathsAsync("ProjectA", "team-1", areaPaths, CancellationToken.None);

        // Assert
        Assert.IsTrue(target.AreaPaths.ContainsKey("team-1"));
        Assert.AreEqual("ProjectA", target.AreaPaths["team-1"].DefaultAreaPath);
        Assert.AreEqual(2, target.AreaPaths["team-1"].IncludedAreaPaths.Count);
    }

    [TestMethod]
    public async Task CreateOrUpdateTeamAsync_MultipleTeams_AllStored()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        // Act
        var id1 = await target.CreateOrUpdateTeamAsync("P", new TeamDefinition("s1", "Team Alpha", "d", true), CancellationToken.None);
        var id2 = await target.CreateOrUpdateTeamAsync("P", new TeamDefinition("s2", "Team Beta", "d", false), CancellationToken.None);
        var id3 = await target.CreateOrUpdateTeamAsync("P", new TeamDefinition("s3", "Team Gamma", "d", false), CancellationToken.None);

        // Assert
        Assert.AreEqual(3, target.Teams.Count, "All three teams should be stored");
        Assert.AreNotEqual(id1, id2);
        Assert.AreNotEqual(id2, id3);
    }
}

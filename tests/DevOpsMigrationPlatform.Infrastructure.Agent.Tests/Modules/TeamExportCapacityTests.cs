// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Intent-derived tests for TeamCapacityTeamExtension (Phase 1 extension model).
/// Capacity is written to Teams/{slug}/capacity.json — not to team.json.
/// </summary>
[TestClass]
public class TeamExportCapacityTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static (ITeamSource teamSource, Mock<IPackageAccess> package) CreateCapacityTeamSource(
        TeamCapacityEntry[] capacity,
        string iterationId = "sprint-1")
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamSettings?)null);
        teamSource.Setup(s => s.GetTeamIterationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldIteration(iterationId, "Sprint 1"));
        teamSource.Setup(s => s.GetTeamMembersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEmpty<TeamMember>());
        teamSource.Setup(s => s.GetTeamCapacityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(capacity);
        teamSource.Setup(s => s.GetTeamAreaPathsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamAreaPaths?)null);

        var package = PackageTestFactory.CreateLooseMock();
        return (teamSource.Object, package);
    }

    private static async IAsyncEnumerable<T> YieldEmpty<T>([EnumeratorCancellation] CancellationToken _ = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<TeamIteration> YieldIteration(
        string id, string name, [EnumeratorCancellation] CancellationToken _ = default)
    {
        yield return new TeamIteration(id, $"Project\\{name}", name, null, null, false, false);
        await Task.CompletedTask;
    }

    // ── Scenario: Export includes capacity for each assigned iteration ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CapacityExtension_WhenCapacityEnabled_WritesCapacityToSeparateArtifact()
    {
        // After Phase 1 refactor, capacity is written to Teams/{slug}/capacity.json by
        // TeamCapacityTeamExtension — not to team.json.
        var Extensions = new DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions.TeamCapacityTeamExtension(
            Microsoft.Extensions.Options.Options.Create(new DevOpsMigrationPlatform.Abstractions.Agent.Teams.TeamCapacityExtensionOptions()),
            teamSource: CreateCapacityTeamSource(
                new[] { new TeamCapacityEntry("desc-alice", "Alice", new[] { new ActivityEntry("Development", 6.0) }, 0) }).teamSource,
            teamTarget: null,
            logger: NullLogger<DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions.TeamCapacityTeamExtension>.Instance);

        var writtenPaths = new List<string>();
        var writtenJsons = new List<string>();
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((ctx, payload, _) =>
            {
                writtenPaths.Add(ctx.Address?.RelativePath ?? string.Empty);
                payload.Content.Position = 0;
                writtenJsons.Add(new StreamReader(payload.Content, Encoding.UTF8).ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);
        // Seed iterations.json so TeamCapacityTeamExtension can read iteration IDs
        var iterationsJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new TeamIteration("sprint-1", "Project\\Sprint 1", "Sprint 1", null, null, false, false)
        });
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("iterations.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(iterationsJson))));
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address == null || !c.Address.RelativePath.EndsWith("iterations.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var ctx = new DevOpsMigrationPlatform.Abstractions.Agent.Teams.TeamExtensionContext
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "team-1",
            TargetEntityId = null,
            Package = package.Object,
            Team = new TeamDefinition("team-1", "Alpha Team", string.Empty, true),
            Slug = "alpha-team",
            SourceProjectName = "Proj",
        };

        await Extensions.ExportAsync(ctx, CancellationToken.None);

        Assert.IsTrue(writtenPaths.Any(p => p.Replace('\\', '/').EndsWith("capacity.json")), "Expected capacity.json to be written.");
    }

}

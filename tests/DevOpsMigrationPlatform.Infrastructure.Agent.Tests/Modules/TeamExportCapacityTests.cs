// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Intent-derived tests for export-team-capacity feature.
/// Verifies TeamExportOrchestrator writes capacityByIteration correctly.
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

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportTeamAsync_WhenCapacityEnabled_WritesCapacityByIteration()
    {
        var capacity = new[] { new TeamCapacityEntry("desc-alice", "Alice", new[] { new ActivityEntry("Development", 6.0) }, 0) };
        var (teamSource, package) = CreateCapacityTeamSource(capacity);

        string? writtenJson = null;
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                writtenJson = new StreamReader(payload.Content, Encoding.UTF8).ReadToEnd();
            })
            .Returns(ValueTask.CompletedTask);

        var source = Mock.Of<ISourceEndpointInfo>(s => s.Url == "https://example.com" && s.Project == "Proj" && s.OrganisationSlug == "org");
        var orchestrator = new TeamExportOrchestrator(teamSource, NullLogger<TeamExportOrchestrator>.Instance, source);
        var team = new TeamDefinition("team-1", "Alpha Team", string.Empty, true);
        var extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true, TeamIterations = true };

        await orchestrator.ExportTeamAsync("org", "Proj", team, "alpha-team", package.Object, extensions, CancellationToken.None);

        Assert.IsNotNull(writtenJson, "Expected team.json to be written.");
        using var doc = JsonDocument.Parse(writtenJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("capacityByIteration", out var cap), "Expected capacityByIteration property.");
        Assert.AreNotEqual(0, cap.EnumerateObject().Count(), "Expected non-empty capacityByIteration.");
    }

    // ── Scenario: Team with no capacity data exports empty capacityByIteration ─

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportTeamAsync_WhenNoCapacityData_WritesEmptyCapacityByIteration()
    {
        var (teamSource, package) = CreateCapacityTeamSource(Array.Empty<TeamCapacityEntry>());

        string? writtenJson = null;
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                writtenJson = new StreamReader(payload.Content, Encoding.UTF8).ReadToEnd();
            })
            .Returns(ValueTask.CompletedTask);

        var source = Mock.Of<ISourceEndpointInfo>(s => s.Url == "https://example.com" && s.Project == "Proj" && s.OrganisationSlug == "org");
        var orchestrator = new TeamExportOrchestrator(teamSource, NullLogger<TeamExportOrchestrator>.Instance, source);
        var team = new TeamDefinition("team-1", "Ops Team", string.Empty, true);
        var extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true, TeamIterations = true };

        await orchestrator.ExportTeamAsync("org", "Proj", team, "ops-team", package.Object, extensions, CancellationToken.None);

        Assert.IsNotNull(writtenJson);
        using var doc = JsonDocument.Parse(writtenJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("capacityByIteration", out var cap));
        Assert.AreEqual(0, cap.EnumerateObject().Count(), "Expected empty capacityByIteration when no capacity data.");
    }

    // ── Scenario: Capacity not supported gracefully skipped ───────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportTeamAsync_WhenCapacityThrowsNotSupported_SkipsWithoutError()
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamSettings?)null);
        teamSource.Setup(s => s.GetTeamIterationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldIteration("sprint-1", "Sprint 1"));
        teamSource.Setup(s => s.GetTeamMembersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEmpty<TeamMember>());
        teamSource.Setup(s => s.GetTeamCapacityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Capacity setting is not supported on this target."));
        teamSource.Setup(s => s.GetTeamAreaPathsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamAreaPaths?)null);

        var package = PackageTestFactory.CreateLooseMock();
        string? writtenJson = null;
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                writtenJson = new StreamReader(payload.Content, Encoding.UTF8).ReadToEnd();
            })
            .Returns(ValueTask.CompletedTask);

        var source = Mock.Of<ISourceEndpointInfo>(s => s.Url == "https://example.com" && s.Project == "Proj" && s.OrganisationSlug == "org");
        var orchestrator = new TeamExportOrchestrator(teamSource.Object, NullLogger<TeamExportOrchestrator>.Instance, source);
        var team = new TeamDefinition("team-1", "Alpha Team", string.Empty, true);
        var extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true, TeamIterations = true };

        // Should not throw
        await orchestrator.ExportTeamAsync("org", "Proj", team, "alpha-team", package.Object, extensions, CancellationToken.None);

        Assert.IsNotNull(writtenJson, "team.json should still be written when capacity is unsupported.");
        using var doc = JsonDocument.Parse(writtenJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("capacityByIteration", out var cap));
        Assert.AreEqual(0, cap.EnumerateObject().Count(), "capacityByIteration should be empty when capacity API is unsupported.");
    }
}

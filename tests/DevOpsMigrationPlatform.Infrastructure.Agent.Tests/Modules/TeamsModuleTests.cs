// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Logging;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public class TeamsModuleTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static IAgentJobContext CreateAgentJobContext()
    {
        var mock = new Mock<IAgentJobContext>();
        mock.SetupGet(x => x.PackagePath).Returns("/tmp/test-package");
        mock.SetupGet(x => x.Mode).Returns("Export");
        mock.SetupGet(x => x.ConfigVersion).Returns("2.0");
        return mock.Object;
    }

    private static ISourceEndpointInfo CreateSourceEndpointInfo(string sourceProject = "TestProject")
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mock.SetupGet(x => x.Project).Returns(sourceProject);
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        return mock.Object;
    }

    private static ITargetEndpointInfo CreateTargetEndpointInfo(string targetProject = "TargetProject")
    {
        var mock = new Mock<ITargetEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/target");
        mock.SetupGet(x => x.Project).Returns(targetProject);
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        return mock.Object;
    }

    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    private static PackageContentContext ContentAt(string path)
        => new(PackageContentKind.Artefact, "test-org", "test-project", "Teams", Address: new TestPackageAddress(path));

    private static ExportContext CreateExportContext(IPackageAccess package)
        => new()
        {
            Job = new Job { Kind = JobKind.Export },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };

    private static ImportContext CreateImportContext(IPackageAccess package)
        => new()
        {
            Job = new Job { Kind = JobKind.Import },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };

    private static ValidationContext CreateValidationContext(IPackageAccess package)
        => new()
        {
            Job = new Job(),
            Package = package
        };

    private static TeamsOrchestrator CreateTeamsOrchestrator(
        IPackageAccess package,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null,
        IEnumerable<IModuleExtension>? extensions = null)
        => new(
            NullLogger<TeamsOrchestrator>.Instance,
            exportOrchestrator: exportOrchestrator,
            importOrchestrator: importOrchestrator,
            slugGenerator: new TeamSlugGenerator(),
            package: package,
            extensions: extensions);

    /// <summary>
    /// Creates a TeamsOrchestrator with standard per-capability extensions built from the
    /// supplied tools. Mirrors what DI would wire in production. Only extensions whose
    /// tools are non-null are enabled; the rest default to Enabled=true (capability enabled,
    /// tool absent → extension still wires in but skips gracefully).
    /// </summary>
    private static TeamsOrchestrator CreateTeamsOrchestratorWithExtensions(
        IPackageAccess package,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        INodeTranslationTool? nodeTranslationTool = null,
        IIdentityTranslationTool? identityTranslationTool = null,
        IReferencedPathTracker? referencedPathTracker = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null)
    {
        // EC-M3: team settings are core Teams pipeline behaviour (no extension seam).
        // EC-H1: extensions take non-nullable seams and gate on connector capability.
        teamSource ??= new Mock<ITeamSource>(MockBehavior.Loose).Object;
        teamTarget ??= new Mock<ITeamTarget>(MockBehavior.Loose).Object;
        var extensions = new IModuleExtension[]
        {
            new TeamIterationsTeamExtension(
                Options.Create(new TeamIterationsExtensionOptions { Enabled = true }),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
                teamSource, teamTarget, nodeTranslationTool, referencedPathTracker),
            new TeamMembersTeamExtension(
                Options.Create(new TeamMembersExtensionOptions { Enabled = true }),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
                teamSource, teamTarget, identityTranslationTool),
            new TeamCapacityTeamExtension(
                Options.Create(new TeamCapacityExtensionOptions { Enabled = true }),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
                teamSource, teamTarget),
            new TeamAreaPathsTeamExtension(
                Options.Create(new TeamAreaPathsExtensionOptions { Enabled = true }),
                DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
                teamTarget, nodeTranslationTool),
        };
        return CreateTeamsOrchestrator(package, exportOrchestrator, importOrchestrator, extensions);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Strict);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        var source = new SimulatedTeamSource();
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = false }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object),
            teamSource: source);

        // Act — no store calls expected
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_Skips_WhenNoTeamSourceRegistered()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Strict);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object)); // no teamSource

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_WritesTeamJson_PerTeam()
    {
        // Arrange
        var writtenPaths = new List<string>();
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(
            source,
            NullLogger<TeamExportOrchestrator>.Instance,
            endpointInfo: CreateSourceEndpointInfo());

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch),
            teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — two teams from SimulatedTeamSource → two team.json files, and the core
        // pipeline also writes settings.json per team (EC-M3 / ADR-0024).
        Assert.AreEqual(2, writtenPaths.Count(p => p.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected 2 team files. Written: {string.Join(", ", writtenPaths)}");
        Assert.AreEqual(2, writtenPaths.Count(p => p.EndsWith("/settings.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected 2 settings files (core pipeline). Written: {string.Join(", ", writtenPaths)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_AppliesFilter_WhenScopeIsTeams()
    {
        // Arrange
        var writtenPaths = new List<string>();
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo());

        // Filter to only teams matching "Alpha"
        var opts = new TeamsModuleOptions { Enabled = true, Scope = "teams", Filter = "^Alpha" };
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(opts),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch), teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — only "Alpha Team" matches → 1 team.json (plus its core-pipeline settings.json)
        Assert.AreEqual(1, writtenPaths.Count(p => p.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected 1 filtered team. Written: {string.Join(", ", writtenPaths)}");
        Assert.IsTrue(writtenPaths.TrueForAll(p => p.Contains("alpha-team", StringComparison.OrdinalIgnoreCase)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_SkipsExistingTeam_WhenAlwaysExportFalse()
    {
        // Arrange — team.json already exists in the store
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.ExistsAsync(It.Is<string>(p => p.EndsWith("/team.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already exported

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo());

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, AlwaysExport = false }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch), teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — no team.json writes (all skipped)
        storeMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Expected zero writes when all teams are already present and AlwaysExport=false.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_ReexportsExistingTeam_WhenAlwaysExportTrue()
    {
        // Arrange — team.json already exists, but AlwaysExport forces a fresh export
        var writtenPaths = new List<string>();
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.ExistsAsync(It.Is<string>(p => p.EndsWith("/team.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already exported
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo());

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, AlwaysExport = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch), teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — both teams written despite artefacts already existing
        Assert.AreEqual(2, writtenPaths.Count(p => p.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected 2 team writes with AlwaysExport=true. Written: {string.Join(", ", writtenPaths)}");
    }

    // ── Content verification tests (from export-team-definitions, iterations, members features) ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_TeamJson_ContainsTeamNameAndIsDefault()
    {
        var writtenContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => writtenContent[path] = content)
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo());
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch),
            teamSource: source);

        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        var alphaEntry = writtenContent.FirstOrDefault(kv =>
            kv.Key.Contains("alpha-team", StringComparison.OrdinalIgnoreCase) &&
            kv.Key.EndsWith("team.json", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(alphaEntry.Value, "Expected alpha-team/team.json to be written.");

        using var doc = JsonDocument.Parse(alphaEntry.Value);
        var definition = doc.RootElement.GetProperty("definition");
        Assert.AreEqual("Alpha Team", definition.GetProperty("name").GetString());
        Assert.IsTrue(definition.GetProperty("isDefault").GetBoolean(), "Alpha Team should be the default team.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_TeamJson_ContainsIterationsAndMembers()
    {
        // With the extension-based architecture, iterations and members are written to
        // separate artifact files: Teams/{slug}/iterations.json and Teams/{slug}/members.json.
        var writtenContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => writtenContent[path] = content)
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo());
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true, TeamMembers = true }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamSource: source, exportOrchestrator: exportOrch),
            teamSource: source);

        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Iterations are now in a separate iterations.json artifact
        var iterEntry = writtenContent.FirstOrDefault(kv => kv.Key.Contains("alpha-team", StringComparison.OrdinalIgnoreCase) && kv.Key.Contains("iterations.json", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(iterEntry.Value, "Expected alpha-team/iterations.json to be written.");

        using var iterDoc = JsonDocument.Parse(iterEntry.Value);
        var iterations = iterDoc.RootElement;
        Assert.AreEqual(JsonValueKind.Array, iterations.ValueKind, "iterations.json should be an array.");
        Assert.IsTrue(iterations.GetArrayLength() > 0, "Expected iterations array to be non-empty.");
        var firstIter = iterations[0];
        Assert.IsTrue(firstIter.TryGetProperty("id", out _), "Iteration should have 'id' field.");
        Assert.IsTrue(firstIter.TryGetProperty("path", out _), "Iteration should have 'path' field.");
        Assert.IsTrue(firstIter.TryGetProperty("name", out _), "Iteration should have 'name' field.");

        // Members are now in a separate members.json artifact
        var memberEntry = writtenContent.FirstOrDefault(kv => kv.Key.Contains("alpha-team", StringComparison.OrdinalIgnoreCase) && kv.Key.Contains("members.json", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(memberEntry.Value, "Expected alpha-team/members.json to be written.");

        using var memberDoc = JsonDocument.Parse(memberEntry.Value);
        var members = memberDoc.RootElement;
        Assert.AreEqual(JsonValueKind.Array, members.ValueKind, "members.json should be an array.");
        Assert.IsTrue(members.GetArrayLength() > 0, "Expected members array to be non-empty.");
        var firstMember = members[0];
        Assert.IsTrue(firstMember.TryGetProperty("displayName", out _), "Member should have 'displayName' field.");
        Assert.IsTrue(firstMember.TryGetProperty("uniqueName", out _), "Member should have 'uniqueName' field.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Strict);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = false }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object));

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CreatesTeams_FromPackageFiles()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        // Build a minimal team.json in the store
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Settings = null,
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            AreaPaths = null,
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var paths = new[] { "Teams/alpha-team/team.json" };
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(paths));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — team was created in the target
        Assert.AreEqual(1, target.Teams.Count, "Expected exactly one team to be imported");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CreatesNonDefaultTeams_ByName_WhenTwoTeamsInPackage()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var devTeam = new TeamPackage
        {
            Definition = new TeamDefinition("src-2", "Dev Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var testTeam = new TeamPackage
        {
            Definition = new TeamDefinition("src-3", "Test Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/dev-team/team.json", "Teams/test-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/dev-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(devTeam, s_jsonOptions));
        storeMock.Setup(s => s.ReadAsync("Teams/test-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(testTeam, s_jsonOptions));

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — both non-default teams created by name
        Assert.AreEqual(2, target.Teams.Count, "Expected both non-default teams to be imported");
        Assert.IsTrue(target.Teams.Values.Any(t => t.Name == "Dev Team"), "Dev Team should be created on target");
        Assert.IsTrue(target.Teams.Values.Any(t => t.Name == "Test Team"), "Test Team should be created on target");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CreatesAllTeams_WhenFiveNonDefaultTeamsInPackage()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamNames = new[] { "Team Alpha", "Team Beta", "Team Gamma", "Team Delta", "Team Epsilon" };
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);

        var paths = teamNames.Select(n => $"Teams/{n.ToLowerInvariant().Replace(' ', '-')}/team.json").ToArray();
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(paths));

        for (var i = 0; i < teamNames.Length; i++)
        {
            var tp = new TeamPackage
            {
                Definition = new TeamDefinition($"src-{i + 1}", teamNames[i], "", false),
                Iterations = new List<TeamIteration>(),
                Members = new List<TeamMember>(),
                CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
            };
            var path = paths[i];
            storeMock.Setup(s => s.ReadAsync(path, It.IsAny<CancellationToken>()))
                .ReturnsAsync(JsonSerializer.Serialize(tp, s_jsonOptions));
        }

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — all 5 non-default teams created on target
        Assert.AreEqual(5, target.Teams.Count,
            $"Expected 5 teams. Got: {string.Join(", ", target.Teams.Values.Select(t => t.Name))}");
        foreach (var name in teamNames)
            Assert.IsTrue(target.Teams.Values.Any(t => t.Name == name), $"Team '{name}' should be on target");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenNoTeamFilesFound()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(Array.Empty<string>()));

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object));

        var context = CreateValidationContext(package.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "No team files");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenTeamJsonMissingDefinitionField()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"name\":\"Alpha Team\"}"); // no "definition" key

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object));

        var context = CreateValidationContext(package.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "definition");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenTeamJsonIsMalformed()
    {
        // Arrange
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-valid-json");

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object));

        var context = CreateValidationContext(package.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "malformed JSON");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_Passes_WhenAllTeamFilesAreValid()
    {
        // Arrange
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("id-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object));

        var context = CreateValidationContext(package.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count, $"Unexpected errors: {string.Join("; ", context.Errors.ConvertAll(e => e.Message))}");
    }

    // ── Iteration Tests (T068) ────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_AssignsIterations_WithPathPassThrough_WhenNoTranslationTool()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        // No INodeTranslationTool → paths passed through as-is by TeamIterationsTeamExtension
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var iteration = new TeamIteration("iter-1", "ProjectA\\Sprint 1", "Sprint 1", null, null, false, false);
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration> { iteration },
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — iteration was assigned to the created team
        Assert.AreEqual(1, target.Teams.Count);
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        Assert.IsTrue(target.Iterations.ContainsKey(teamId), "Iteration should have been assigned");
        Assert.AreEqual("ProjectA\\Sprint 1", target.Iterations[teamId][0].Path);
    }

    // ── Member Tests (T075) ───────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_AddsMembersWithIdentityMapping()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var identityTranslationTool = Mock.Of<IIdentityTranslationTool>(m =>
            m.IsEnabled == true &&
            m.Translate("src-alice", It.IsAny<IdentityTranslationMap>()) == "tgt-alice@target.com" &&
            m.Translate("src-bob", It.IsAny<IdentityTranslationMap>()) == "tgt-bob@target.com");

        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>
            {
                new TeamMember("src-alice", "Alice", "alice@src.com", true),
                new TeamMember("src-bob", "Bob", "bob@src.com", false)
            },
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamMembers = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                identityTranslationTool: identityTranslationTool, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — both members added with resolved descriptors
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        Assert.IsTrue(target.Members.ContainsKey(teamId));
        Assert.AreEqual(2, target.Members[teamId].Count);
        Assert.IsTrue(target.Members[teamId].Exists(m => m.Descriptor == "tgt-alice@target.com" && m.IsAdmin));
        Assert.IsTrue(target.Members[teamId].Exists(m => m.Descriptor == "tgt-bob@target.com"));
    }

    // ── Capacity Tests (T082) ─────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_SetsCapacity_ForEachIteration()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var capacity = new TeamCapacityEntry[]
        {
            new TeamCapacityEntry("desc-alice", "Alice", new[] { new ActivityEntry("Dev", 6) }, 0)
        };
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>
            {
                ["sprint-1"] = capacity
            }
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — capacity was set
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        var key = $"{teamId}/sprint-1";
        Assert.IsTrue(target.Capacity.ContainsKey(key), $"Expected capacity for key '{key}'");
        Assert.AreEqual(1, target.Capacity[key].Length);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_SkipsCapacity_WhenCapacityExtensionDisabled()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>
            {
                ["sprint-1"] = new[] { new TeamCapacityEntry("d", "U", new[] { new ActivityEntry("Dev", 6) }, 0) }
            }
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = false } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — no capacity entries stored
        Assert.AreEqual(0, target.Capacity.Count, "Capacity should be empty when extension is disabled");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CompletesWithoutError_WhenTargetThrowsNotSupportedForCapacity()
    {
        // Arrange — target throws "not supported" from SetCapacityAsync
        var teamTarget = new Mock<ITeamTarget>(MockBehavior.Loose);
        teamTarget
            .Setup(t => t.CreateOrUpdateTeamAsync( It.IsAny<string>(), It.IsAny<TeamDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("target-alpha-team");
        teamTarget
            .Setup(t => t.SetCapacityAsync( It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TeamCapacityEntry[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Capacity setting is not supported on this target."));

        var importOrch = new TeamImportOrchestrator(teamTarget.Object, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>
            {
                ["sprint-1"] = new[] { new TeamCapacityEntry("d", "Alice", new[] { new ActivityEntry("Dev", 6) }, 0) }
            }
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: teamTarget.Object, importOrchestrator: importOrch),
            teamTarget: teamTarget.Object);

        // Act — must not throw; "not supported" is caught and logged informationally
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — SetCapacityAsync was attempted but no exception propagated
        teamTarget.Verify(t => t.SetCapacityAsync( It.IsAny<string>(), It.IsAny<string>(),
            "sprint-1", It.IsAny<TeamCapacityEntry[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_NeverCallsSetCapacity_WhenCapacityByIterationIsEmpty()
    {
        // Arrange — team package has TeamCapacity extension enabled but no capacity entries
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()  // empty
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — empty capacity map: SetCapacityAsync never called
        Assert.AreEqual(0, target.Capacity.Count, "No capacity calls expected when CapacityByIteration is empty");
    }

    // ── NodeTranslation Extension Tests (T069) ─────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_RecordsAreaAndIterationPaths_WhenNodeTranslationExtensionEnabled()
    {
        // Arrange
        var recordedAreaPaths = new List<string>();
        var recordedIterPaths = new List<string>();

        var trackerMock = new Mock<IReferencedPathTracker>(MockBehavior.Loose);
        trackerMock
            .Setup(t => t.RecordAreaPathAsync(It.IsAny<string>(), It.IsAny<IPackageAccess>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, IPackageAccess, string, string, CancellationToken>((path, _, __, ___, ____) => recordedAreaPaths.Add(path))
            .Returns(Task.CompletedTask);
        trackerMock
            .Setup(t => t.RecordIterationPathAsync(It.IsAny<string>(), It.IsAny<IPackageAccess>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, IPackageAccess, string, string, CancellationToken>((path, _, __, ___, ____) => recordedIterPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo(),
            referencedPathTracker: trackerMock.Object);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new TeamsModuleOptions
        {
            Enabled = true,
            Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = true, TeamIterations = true }
        };
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(opts),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(
                package.Object,
                teamSource: source,
                referencedPathTracker: trackerMock.Object,
                exportOrchestrator: exportOrch),
            teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — iteration paths should be recorded (SimulatedTeamSource has sprint iterations)
        Assert.IsTrue(recordedIterPaths.Count > 0, "Expected at least one iteration path to be recorded.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExportAsync_DoesNotRecordPaths_WhenNodeTranslationExtensionDisabled()
    {
        // Arrange
        var trackerMock = new Mock<IReferencedPathTracker>(MockBehavior.Strict);
        // Strict: no calls should be made

        var source = new SimulatedTeamSource();
        var exportOrch = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance, endpointInfo: CreateSourceEndpointInfo(),
            referencedPathTracker: trackerMock.Object);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new TeamsModuleOptions
        {
            Enabled = true,
            Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = false, TeamIterations = true }
        };
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(opts),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch), teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — no path tracker calls when NodeTranslation extension is disabled
        trackerMock.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_TranslatesIterationPaths_ViaNodeTranslationTool()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        var translatedPaths = new List<string>();
        var translationToolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        translationToolMock.Setup(t => t.IsEnabled).Returns(true);
        translationToolMock
            .Setup(t => t.TranslatePath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectMapping>()))
            .Returns<string, string, ProjectMapping>((field, path, mapping) =>
            {
                var targetPath = path.Replace("SourceProject", "TargetProject");
                translatedPaths.Add(targetPath);
                return new PathTranslation(targetPath, false, true, false);
            });

        var importOrch = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", false),
            Iterations = new List<TeamIteration>
            {
                new TeamIteration("i1", "SourceProject\\Sprint 1", "Sprint 1", null, null, false, false)
            },
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                nodeTranslationTool: translationToolMock.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — TranslatePath was called and path was translated
        translationToolMock.Verify(t => t.TranslatePath(
            It.IsAny<string>(), "SourceProject\\Sprint 1", It.IsAny<ProjectMapping>()), Times.Once);
        Assert.IsTrue(translatedPaths.Contains("TargetProject\\Sprint 1"),
            "Expected translated path 'TargetProject\\Sprint 1'.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_SkipsIteration_WhenPathUntranslatable_GAP005()
    {
        // GAP-005: when the tool cannot map the path (TargetPath is null), the iteration must be
        // SKIPPED — not silently assigned with the untranslated source path.
        var target = new SimulatedTeamTarget();

        var translationToolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        translationToolMock.Setup(t => t.IsEnabled).Returns(true);
        translationToolMock
            .Setup(t => t.TranslatePath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(null!, false, false, false)); // untranslatable

        var importOrch = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", false),
            Iterations = new List<TeamIteration>
            {
                new TeamIteration("i1", "SourceProject\\Sprint 1", "Sprint 1", null, null, false, false)
            },
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory = new InMemoryArtefactStore();
        inMemory.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                nodeTranslationTool: translationToolMock.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — no iteration was assigned (untranslatable path skipped, not passed through).
        var assignedCount = target.Iterations.Values.Sum(list => list.Count);
        Assert.AreEqual(0, assignedCount, "Untranslatable iteration must be skipped, not assigned.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportTeam_SkipsMember_WhenIdentityResolvesToDefault_GAP006()
    {
        // GAP-006/FR-010: a member whose identity resolves to the configured default is
        // unresolvable and must be SKIPPED, not imported under the default identity.
        var target = new SimulatedTeamTarget();
        var idTool = new Mock<IIdentityTranslationTool>(MockBehavior.Loose);
        idTool.Setup(t => t.IsEnabled).Returns(true);
        idTool.Setup(t => t.DefaultIdentity).Returns("default@target.com");
        idTool.Setup(t => t.Translate("src-unknown", It.IsAny<IdentityTranslationMap>())).Returns("default@target.com");

        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("t1", "Alpha", "", false),
            Members = new List<TeamMember> { new TeamMember("src-unknown", "Unknown User", "unknown@src.com", false) },
            Iterations = new List<TeamIteration>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory1 = new InMemoryArtefactStore();
        inMemory1.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemory1);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamMembers = true, IdentityLookup = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                identityTranslationTool: idTool.Object, importOrchestrator: importOrch),
            teamTarget: target);

        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        Assert.AreEqual(0, target.Members.Values.Sum(m => m.Count),
            "Member resolving to the default identity must be skipped.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportTeam_AddsMember_WhenIdentityResolvesToNonDefault_GAP006()
    {
        var target = new SimulatedTeamTarget();
        var idTool = new Mock<IIdentityTranslationTool>(MockBehavior.Loose);
        idTool.Setup(t => t.IsEnabled).Returns(true);
        idTool.Setup(t => t.DefaultIdentity).Returns("default@target.com");
        idTool.Setup(t => t.Translate("src-bob", It.IsAny<IdentityTranslationMap>())).Returns("bob@target.com");

        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("t1", "Alpha", "", false),
            Members = new List<TeamMember> { new TeamMember("src-bob", "Bob", "bob@src.com", false) },
            Iterations = new List<TeamIteration>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemory2 = new InMemoryArtefactStore();
        inMemory2.Seed("Teams/alpha-team/team.json", json);
        var package2 = PackageTestFactory.CreateDelegatingMock(inMemory2);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamMembers = true, IdentityLookup = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package2.Object, teamTarget: target,
                identityTranslationTool: idTool.Object, importOrchestrator: importOrch),
            teamTarget: target);

        await module.ImportAsync(CreateImportContext(package2.Object), CancellationToken.None);

        Assert.AreEqual(1, target.Members.Values.Sum(m => m.Count), "Resolved member must be added.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportTeam_LogsStructuredWarning_ForDefaultTeam_GAP004()
    {
        // GAP-004/FR-011: a default team logs a structured warning and is still created
        // on the target using the source team name (no name-matching substitution occurs).
        //
        // KNOWN LIMITATION (BLOCKED): ITeamTarget has no explicit default-team-assignment
        // API. The platform logs a warning and creates the team; it cannot assign it as
        // the project's default. Assert the warning + creation; do not assert settings
        // applied to target default team (that outcome is blocked by the target API gap).
        // See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:64-71
        var target = new SimulatedTeamTarget();
        var logger = new Mock<ILogger<TeamImportOrchestrator>>();
        var orch = new TeamImportOrchestrator(target, logger.Object, CreateTargetEndpointInfo());

        var pkg = new TeamPackage
        {
            Definition = new TeamDefinition("t1", "The Default Team", "", true),
            Members = new List<TeamMember>(),
            Iterations = new List<TeamIteration>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };

        await orch.ImportTeamAsync("TargetProject", "SourceProject", pkg, new TeamsModuleExtensionsOptions(), CancellationToken.None);

        // B1: structured warning was emitted
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("does not support explicit default team assignment")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // B2: team was still created on the target despite the API limitation
        Assert.AreEqual(1, target.Teams.Count,
            "Default team must still be created on the target despite the warning.");

        // B3: the team was created with the source name, not substituted with any target team name
        Assert.IsTrue(
            target.Teams.Values.Any(t => t.Name == "The Default Team"),
            "Team should be created using the source team name; no name-matching substitution must occur.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_UsesIterationPathField_ForIterationTranslation()
    {
        // Regression guard: TranslatePath for iterations must pass "System.IterationPath",
        // not "System.AreaPath". Using the wrong field applies area-path rules/overrides to
        // iterations and produces incorrect assignments.
        var target = new SimulatedTeamTarget();

        string? capturedIterationField = null;
        string? capturedAreaField = null;

        var translationToolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        translationToolMock.Setup(t => t.IsEnabled).Returns(true);
        translationToolMock
            .Setup(t => t.TranslatePath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectMapping>()))
            .Returns<string, string, ProjectMapping>((field, path, _) =>
            {
                // Record the first call per field type
                if (path.Contains("Sprint"))
                    capturedIterationField = field;
                else if (path.Contains("\\Area"))
                    capturedAreaField = field;
                return new PathTranslation(path, false, true, false);
            });

        var importOrch = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", false),
            Iterations = new List<TeamIteration>
            {
                new TeamIteration("i1", "SrcProject\\Sprint 1", "Sprint 1", null, null, false, false)
            },
            Members = new List<TeamMember>(),
            AreaPaths = new TeamAreaPaths("SrcProject\\Area", new List<string> { "SrcProject\\Area" }),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemoryA = new InMemoryArtefactStore();
        inMemoryA.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemoryA);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions
                {
                    TeamIterations = true,
                    NodeTranslation = true,
                }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                nodeTranslationTool: translationToolMock.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — iteration translation used System.IterationPath, area used System.AreaPath
        Assert.AreEqual("System.IterationPath", capturedIterationField,
            "Iteration path translation must use 'System.IterationPath', not 'System.AreaPath'.");
        Assert.AreEqual("System.AreaPath", capturedAreaField,
            "Area path translation must use 'System.AreaPath'.");
    }

    // ── Iteration Path Tests ──────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_TranslatesAndAssignsBothIterations_WhenTwoIterationsInPackage()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        var translationToolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        translationToolMock.Setup(t => t.IsEnabled).Returns(true);
        translationToolMock
            .Setup(t => t.TranslatePath("System.IterationPath", It.IsAny<string>(), It.IsAny<ProjectMapping>()))
            .Returns<string, string, ProjectMapping>((_, path, _) =>
                new PathTranslation(path.Replace("ProjectA", "TargetProject"), false, true, false));

        var importOrch = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "", false),
            Iterations = new List<TeamIteration>
            {
                new TeamIteration("i1", "ProjectA\\Sprint 1", "Sprint 1", null, null, false, false),
                new TeamIteration("i2", "ProjectA\\Sprint 2", "Sprint 2", null, null, false, false)
            },
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemoryB = new InMemoryArtefactStore();
        inMemoryB.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemoryB);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                nodeTranslationTool: translationToolMock.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — both iterations translated and assigned
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        Assert.IsTrue(target.Iterations.ContainsKey(teamId), "Iterations should have been assigned");
        Assert.AreEqual(2, target.Iterations[teamId].Count, "Both iterations should be assigned");
        Assert.IsTrue(target.Iterations[teamId].Exists(i => i.Path == "TargetProject\\Sprint 1"), "Sprint 1 should be translated");
        Assert.IsTrue(target.Iterations[teamId].Exists(i => i.Path == "TargetProject\\Sprint 2"), "Sprint 2 should be translated");
    }

    // ── Area Path Tests ───────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_TranslatesDefaultAndIncludedAreaPaths_ViaNodeTranslationTool()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        var translationToolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        translationToolMock.Setup(t => t.IsEnabled).Returns(true);
        translationToolMock
            .Setup(t => t.TranslatePath("System.AreaPath", It.IsAny<string>(), It.IsAny<ProjectMapping>()))
            .Returns<string, string, ProjectMapping>((_, path, _) =>
                new PathTranslation(path.Replace("SourceProject", "TargetProject"), false, true, false));

        var importOrch = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            AreaPaths = new TeamAreaPaths("SourceProject", new List<string> { "SourceProject", "SourceProject\\Sub" }),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var inMemoryC = new InMemoryArtefactStore();
        inMemoryC.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemoryC);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = true }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestratorWithExtensions(package.Object, teamTarget: target,
                nodeTranslationTool: translationToolMock.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — area paths translated from SourceProject → TargetProject
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        Assert.IsTrue(target.AreaPaths.ContainsKey(teamId), "SetAreaPathsAsync should have been called");
        Assert.AreEqual("TargetProject", target.AreaPaths[teamId].DefaultAreaPath);
        Assert.IsTrue(target.AreaPaths[teamId].IncludedAreaPaths.Contains("TargetProject"), "TargetProject should be in included paths");
        Assert.IsTrue(target.AreaPaths[teamId].IncludedAreaPaths.Contains("TargetProject\\Sub"), "TargetProject\\Sub should be in included paths");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_DoesNotSetAreaPaths_WhenNodeTranslationExtensionDisabled()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo());

        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "", false),
            Iterations = new List<TeamIteration>(),
            Members = new List<TeamMember>(),
            AreaPaths = new TeamAreaPaths("SourceProject\\TeamArea", new List<string> { "SourceProject\\TeamArea" }),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = false }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — NodeTranslation disabled: SetAreaPathsAsync must not be called
        Assert.AreEqual(0, target.AreaPaths.Count, "SetAreaPathsAsync should not be called when NodeTranslation extension is disabled");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportTeamAreaPaths_SkipsIncludedPath_WhenUntranslatable()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var loggerMock = new Mock<ILogger<TeamAreaPathsTeamExtension>>();

        // Default path "ProjectA" translates successfully; included "ProjectA\ObsoleteArea" returns null.
        var translationToolMock = NodeTranslationToolMock.ReturningNullFor(
            nullPath: "ProjectA\\ObsoleteArea",
            sourceProject: "ProjectA",
            targetProject: "TargetProject");

        var importOrch = new TeamImportOrchestrator(
            target,
            NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo(targetProject: "TargetProject"));

        // Build the area paths extension with the logger mock so we can verify warnings
        var areaPathsExtension = new TeamAreaPathsTeamExtension(
            Options.Create(new TeamAreaPathsExtensionOptions { Enabled = true }),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            target,
            translationToolMock.Object,
            loggerMock.Object);

        var teamPackage = TeamPackageBuilder.WithAreaPaths(
            teamId: "src-1",
            teamName: "Alpha Team",
            areaPaths: TeamAreaPathsBuilder.WithDefaultAndOneIncluded(
                defaultPath: "ProjectA",
                includedPath: "ProjectA\\ObsoleteArea"));

        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);
        var inMemoryD = new InMemoryArtefactStore();
        inMemoryD.Seed("Teams/alpha-team/team.json", json);
        var package = PackageTestFactory.CreateDelegatingMock(inMemoryD);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = true }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(sourceProject: "ProjectA"),
            targetEndpointInfo: CreateTargetEndpointInfo(targetProject: "TargetProject"),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch,
                extensions: new IModuleExtension[] { areaPathsExtension }),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — SetAreaPathsAsync was still called (default translated successfully)
        SimulatedTeamTargetAssertions.AreaPathsWereCalled(target,
            because: "the default path translated successfully");

        // Assert — untranslatable included path is absent from the result
        SimulatedTeamTargetAssertions.AreaPathsExclude(target,
            excludedPath: "ProjectA\\ObsoleteArea",
            because: "null-translated included paths must be filtered out");

        // Assert — a warning was logged for the skipped path
        LoggerAssertions.VerifyWarningContaining(loggerMock, "ObsoleteArea");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportTeamAreaPaths_DoesNotCallSetAreaPaths_WhenDefaultPathUntranslatable()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        // All paths — including the default "UnknownProject" — translate to null.
        var translationToolMock = NodeTranslationToolMock.ReturningNullForAll();

        var importOrch = new TeamImportOrchestrator(
            target,
            NullLogger<TeamImportOrchestrator>.Instance,
            endpointInfo: CreateTargetEndpointInfo(),
            nodeTranslationTool: translationToolMock.Object);

        var teamPackage = TeamPackageBuilder.WithAreaPaths(
            teamId: "src-2",
            teamName: "Beta Team",
            areaPaths: TeamAreaPathsBuilder.WithDefaultOnly("UnknownProject"));

        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/beta-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/beta-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions
            {
                Enabled = true,
                Extensions = new TeamsModuleExtensionsOptions { NodeTranslation = true }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch),
            teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — SetAreaPathsAsync must never have been called
        SimulatedTeamTargetAssertions.AreaPathsNotCalled(target,
            because: "a null-translated default path must suppress the entire SetAreaPathsAsync call");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A simple in-memory ITestArtefactStore that stores writes and returns them on reads.
    /// Used for tests that exercise the legacy-to-extension upgrade path, where the upgrader
    /// writes split artifact files that extensions then read back.
    /// </summary>
    private sealed class InMemoryArtefactStore : ITestArtefactStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _binaryStore = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string path, string content) => _store[path] = content;

        public Task<string?> ReadAsync(string path, CancellationToken ct)
        {
            _store.TryGetValue(path, out var content);
            return Task.FromResult(content);
        }

        public Task WriteAsync(string path, string content, CancellationToken ct)
        {
            _store[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct)
            => Task.FromResult(_store.ContainsKey(path) || _binaryStore.ContainsKey(path));

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken ct)
        {
            _binaryStore[path] = content;
            return Task.CompletedTask;
        }

        public Task<System.IO.Stream?> ReadBinaryAsync(string path, CancellationToken ct)
        {
            _binaryStore.TryGetValue(path, out var bytes);
            return Task.FromResult(bytes is null ? (System.IO.Stream?)null : new System.IO.MemoryStream(bytes, writable: false));
        }

        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                ct.ThrowIfCancellationRequested();
                yield return key;
                await Task.Yield();
            }
        }

        public Task WriteStreamAsync(string path, System.IO.Stream content, CancellationToken ct)
        {
            using var ms = new System.IO.MemoryStream();
            content.CopyTo(ms);
            _binaryStore[path] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task AppendAsync(string path, string content, CancellationToken ct)
        {
            _store.TryGetValue(path, out var existing);
            _store[path] = (existing ?? string.Empty) + content;
            return Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<string> ToAsyncEnum(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}



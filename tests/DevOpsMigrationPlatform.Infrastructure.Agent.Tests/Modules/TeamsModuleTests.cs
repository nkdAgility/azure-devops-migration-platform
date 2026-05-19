// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
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
        => new(PackageContentKind.Artefact, Address: new TestPackageAddress(path));

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
        TeamImportOrchestrator? importOrchestrator = null)
        => new(
            NullLogger<TeamsOrchestrator>.Instance,
            exportOrchestrator: exportOrchestrator,
            importOrchestrator: importOrchestrator,
            slugGenerator: new TeamSlugGenerator(),
            package: package);

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

        // Assert — two teams from SimulatedTeamSource → two team.json files
        Assert.AreEqual(2, writtenPaths.Count, $"Expected 2 team files. Written: {string.Join(", ", writtenPaths)}");
        Assert.IsTrue(writtenPaths.Exists(p => p.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase)));
    }

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

        // Assert — only "Alpha Team" matches → 1 file
        Assert.AreEqual(1, writtenPaths.Count, $"Expected 1 filtered team. Written: {string.Join(", ", writtenPaths)}");
        Assert.IsTrue(writtenPaths[0].Contains("alpha-team", StringComparison.OrdinalIgnoreCase));
    }

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
        Assert.AreEqual(2, writtenPaths.Count,
            $"Expected 2 team writes with AlwaysExport=true. Written: {string.Join(", ", writtenPaths)}");
    }

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

    [TestMethod]
    public async Task ImportAsync_AssignsIterations_WithPathPassThrough_WhenNoTranslationTool()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        // No INodeTranslationTool → paths passed through as-is
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

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — iteration was assigned to the created team
        Assert.AreEqual(1, target.Teams.Count);
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        Assert.IsTrue(target.Iterations.ContainsKey(teamId), "Iteration should have been assigned");
        Assert.AreEqual("ProjectA\\Sprint 1", target.Iterations[teamId][0].Path);
    }

    // ── Member Tests (T075) ───────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_AddsMembersWithIdentityMapping()
    {
        // Arrange
        var target = new SimulatedTeamTarget();
        var identityLookupTool = Mock.Of<IIdentityLookupTool>(m =>
            m.IsEnabled == true &&
            m.Resolve("src-alice") == "tgt-alice@target.com" &&
            m.Resolve("src-bob") == "tgt-bob@target.com");

        var importOrch = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, endpointInfo: CreateTargetEndpointInfo(), identityLookupTool: identityLookupTool);

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

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamMembers = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

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

        // Assert — capacity was set
        var teamId = new System.Collections.Generic.List<string>(target.Teams.Keys)[0];
        var key = $"{teamId}/sprint-1";
        Assert.IsTrue(target.Capacity.ContainsKey(key), $"Expected capacity for key '{key}'");
        Assert.AreEqual(1, target.Capacity[key].Length);
    }

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

    // ── NodeTranslation Extension Tests (T069) ─────────────────────────────────

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
            orchestrator: CreateTeamsOrchestrator(package.Object, exportOrchestrator: exportOrch), teamSource: source);

        // Act
        await module.ExportAsync(CreateExportContext(package.Object), CancellationToken.None);

        // Assert — iteration paths should be recorded (SimulatedTeamSource has sprint iterations)
        Assert.IsTrue(recordedIterPaths.Count > 0, "Expected at least one iteration path to be recorded.");
    }

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

    [TestMethod]
    public async Task ImportAsync_TranslatesIterationPaths_ViaNodeTransformTool()
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
            endpointInfo: CreateTargetEndpointInfo(),
            NodeTransformTool: translationToolMock.Object);

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

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — TranslatePath was called and path was translated
        translationToolMock.Verify(t => t.TranslatePath(
            It.IsAny<string>(), "SourceProject\\Sprint 1", It.IsAny<ProjectMapping>()), Times.Once);
        Assert.IsTrue(translatedPaths.Contains("TargetProject\\Sprint 1"),
            "Expected translated path 'TargetProject\\Sprint 1'.");
    }

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
            endpointInfo: CreateTargetEndpointInfo(),
            NodeTransformTool: translationToolMock.Object);

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
                Extensions = new TeamsModuleExtensionsOptions
                {
                    TeamIterations = true,
                    NodeTranslation = true,
                }
            }),
            sourceEndpointInfo: CreateSourceEndpointInfo(),
            targetEndpointInfo: CreateTargetEndpointInfo(),
            orchestrator: CreateTeamsOrchestrator(package.Object, importOrchestrator: importOrch), teamTarget: target);

        // Act
        await module.ImportAsync(CreateImportContext(package.Object), CancellationToken.None);

        // Assert — iteration translation used System.IterationPath, area used System.AreaPath
        Assert.AreEqual("System.IterationPath", capturedIterationField,
            "Iteration path translation must use 'System.IterationPath', not 'System.AreaPath'.");
        Assert.AreEqual("System.AreaPath", capturedAreaField,
            "Area path translation must use 'System.AreaPath'.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

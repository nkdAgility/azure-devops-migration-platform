using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
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

    private static ExportContext CreateExportContext(IArtefactStore store)
        => new()
        {
            Job = new MigrationJob { Mode = "Export", Source = new SimulatedEndpointOptions { Project = "TestProject" } },
            ArtefactStore = store,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };

    private static ImportContext CreateImportContext(IArtefactStore store)
        => new()
        {
            Job = new MigrationJob { Mode = "Import", Target = new SimulatedEndpointOptions { Project = "TargetProject" } },
            ArtefactStore = store,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };

    private static ValidationContext CreateValidationContext(Mock<IArtefactStore> store)
        => new()
        {
            Job = new MigrationJob(),
            ArtefactStore = store.Object
        };

    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);
        var source = new SimulatedTeamSource();
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = false }),
            new TeamSlugGenerator(),
            teamSource: source);

        // Act — no store calls expected
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ExportAsync_Skips_WhenNoTeamSourceRegistered()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator()); // no teamSource

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ExportAsync_WritesTeamJson_PerTeam()
    {
        // Arrange
        var writtenPaths = new List<string>();
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

        // Assert — two teams from SimulatedTeamSource → two team.json files
        Assert.AreEqual(2, writtenPaths.Count, $"Expected 2 team files. Written: {string.Join(", ", writtenPaths)}");
        Assert.IsTrue(writtenPaths.Exists(p => p.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ExportAsync_AppliesFilter_WhenScopeIsTeams()
    {
        // Arrange
        var writtenPaths = new List<string>();
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance);

        // Filter to only teams matching "Alpha"
        var opts = new TeamsModuleOptions { Enabled = true, Scope = "teams", Filter = "^Alpha" };
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(opts),
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

        // Assert — only "Alpha Team" matches → 1 file
        Assert.AreEqual(1, writtenPaths.Count, $"Expected 1 filtered team. Written: {string.Join(", ", writtenPaths)}");
        Assert.IsTrue(writtenPaths[0].Contains("alpha-team", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ExportAsync_SkipsExistingTeam_WhenAlwaysExportFalse()
    {
        // Arrange — team.json already exists in the store
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync(It.Is<string>(p => p.EndsWith("/team.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already exported

        var source = new SimulatedTeamSource();
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, AlwaysExport = false }),
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync(It.Is<string>(p => p.EndsWith("/team.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already exported
        storeMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, __) => writtenPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, AlwaysExport = true }),
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

        // Assert — both teams written despite artefacts already existing
        Assert.AreEqual(2, writtenPaths.Count,
            $"Expected 2 team writes with AlwaysExport=true. Written: {string.Join(", ", writtenPaths)}");
    }

    [TestMethod]
    public async Task ImportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);
        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = false }),
            new TeamSlugGenerator());

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ImportAsync_CreatesTeams_FromPackageFiles()
    {
        // Arrange
        var target = new SimulatedTeamTarget();

        var orchestrator = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(paths));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

        // Assert — team was created in the target
        Assert.AreEqual(1, target.Teams.Count, "Expected exactly one team to be imported");
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenNoTeamFilesFound()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(Array.Empty<string>()));

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator());

        var context = CreateValidationContext(storeMock);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"name\":\"Alpha Team\"}"); // no "definition" key

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator());

        var context = CreateValidationContext(storeMock);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-valid-json");

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator());

        var context = CreateValidationContext(storeMock);

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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock
            .Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            new TeamSlugGenerator());

        var context = CreateValidationContext(storeMock);

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
        var orchestrator = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance);

        var iteration = new TeamIteration("iter-1", "ProjectA\\Sprint 1", "Sprint 1", null, null, false, false);
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("src-1", "Alpha Team", "desc", true),
            Iterations = new List<TeamIteration> { iteration },
            Members = new List<TeamMember>(),
            CapacityByIteration = new Dictionary<string, TeamCapacityEntry[]>()
        };
        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

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

        var orchestrator = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance, identityLookupTool: identityLookupTool);

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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamMembers = true } }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

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
        var orchestrator = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance);

        var capacity= new TeamCapacityEntry[]
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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = true } }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

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
        var orchestrator = new TeamImportOrchestrator(target, NullLogger<TeamImportOrchestrator>.Instance);

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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamCapacity = false } }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

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
            .Setup(t => t.RecordAreaPathAsync(It.IsAny<string>(), It.IsAny<IArtefactStore>(), It.IsAny<CancellationToken>()))
            .Callback<string, IArtefactStore, CancellationToken>((path, _, __) => recordedAreaPaths.Add(path))
            .Returns(Task.CompletedTask);
        trackerMock
            .Setup(t => t.RecordIterationPathAsync(It.IsAny<string>(), It.IsAny<IArtefactStore>(), It.IsAny<CancellationToken>()))
            .Callback<string, IArtefactStore, CancellationToken>((path, _, __) => recordedIterPaths.Add(path))
            .Returns(Task.CompletedTask);

        var source = new SimulatedTeamSource();
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance,
            referencedPathTracker: trackerMock.Object);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
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
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

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
        var orchestrator = new TeamExportOrchestrator(source, NullLogger<TeamExportOrchestrator>.Instance,
            referencedPathTracker: trackerMock.Object);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
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
            new TeamSlugGenerator(),
            teamSource: source,
            exportOrchestrator: orchestrator);

        // Act
        await module.ExportAsync(CreateExportContext(storeMock.Object), CancellationToken.None);

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

        var orchestrator = new TeamImportOrchestrator(
            target, NullLogger<TeamImportOrchestrator>.Instance,
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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.EnumerateAsync("Teams/", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(new[] { "Teams/alpha-team/team.json" }));
        storeMock.Setup(s => s.ReadAsync("Teams/alpha-team/team.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var module = new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true, Extensions = new TeamsModuleExtensionsOptions { TeamIterations = true } }),
            new TeamSlugGenerator(),
            teamTarget: target,
            importOrchestrator: orchestrator);

        // Act
        await module.ImportAsync(CreateImportContext(storeMock.Object), CancellationToken.None);

        // Assert — TranslatePath was called and path was translated
        translationToolMock.Verify(t => t.TranslatePath(
            It.IsAny<string>(), "SourceProject\\Sprint 1", It.IsAny<ProjectMapping>()), Times.Once);
        Assert.IsTrue(translatedPaths.Contains("TargetProject\\Sprint 1"),
            "Expected translated path 'TargetProject\\Sprint 1'.");
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

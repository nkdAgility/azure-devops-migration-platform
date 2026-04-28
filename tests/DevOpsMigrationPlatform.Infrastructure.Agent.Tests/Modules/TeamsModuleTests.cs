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
        var identityMapping = Mock.Of<IIdentityMappingService>(
            m => m.Resolve(It.IsAny<string>()) == "target-user");

        var orchestrator = new TeamImportOrchestrator(target, identityMapping, NullLogger<TeamImportOrchestrator>.Instance);

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

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// Acceptance tests for BoardConfigTeamExtension — US1 (Export Board Columns).
/// All test methods are [TestCategory("CodeTest")][TestCategory("DomainTests")].
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("DomainTests")]
public class BoardConfigTeamExtensionTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TeamExtensionContext BuildContext(Mock<IPackageAccess> package, string slug = "alpha")
        => new()
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "team-1",
            TargetEntityId = null,
            Package = package.Object,
            Team = new TeamDefinition("team-1", "Alpha", string.Empty, true),
            Slug = slug,
            SourceProjectName = "Proj",
        };

    private static (Mock<IPackageAccess> package, List<(string path, string json)> written)
        CreateTrackingPackage()
    {
        var written = new List<(string, string)>();
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package
            .Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>(async (ctx, payload, _) =>
            {
                using var reader = new StreamReader(payload.Content, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();
                written.Add((ctx.Address?.RelativePath ?? string.Empty, json));
            })
            .Returns(ValueTask.CompletedTask);
        return (package, written);
    }

    private static async IAsyncEnumerable<BoardConfig> BoardsFrom(params BoardConfig[] boards)
    {
        foreach (var b in boards)
        {
            yield return b;
            await Task.CompletedTask;
        }
    }

    private static BoardConfigTeamExtension BuildExtension(
        Mock<ITeamBoardAdapter> adapter,
        Mock<IConnectorCapabilityProvider> capProvider,
        BoardConfigExtensionOptions? options = null)
        => new(
            Options.Create(options ?? new BoardConfigExtensionOptions()),
            adapter.Object,
            capProvider.Object,
            metrics: null,
            logger: NullLogger<BoardConfigTeamExtension>.Instance);

    private static BoardColumn MakeColumn(string name, int? itemLimit = null, bool isSplit = false)
        => new(name, BoardColumnType.InProgress, itemLimit, isSplit, null, []);

    private static BoardConfig MakeBoard(string name, params BoardColumn[] columns)
        => new(name, columns, []);

    // ---------------------------------------------------------------------------
    // (a) T022: Export_WhenBoardColumnsEnabled_WritesBoardConfigJson
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBoardColumnsEnabled_WritesBoardConfigJson()
    {
        // Arrange
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var boards = new[]
        {
            MakeBoard("Stories", MakeColumn("Proposed"), MakeColumn("Active", 5), MakeColumn("Resolved")),
            MakeBoard("Epics",   MakeColumn("New"),      MakeColumn("In Progress"), MakeColumn("Done")),
        };
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(boards));

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);

        var (package, written) = CreateTrackingPackage();
        var ext = BuildExtension(adapter, cap);

        // Act
        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        // Assert
        Assert.AreEqual(1, written.Count, "Expected exactly one artifact to be written.");
        Assert.IsTrue(
            written[0].path.Replace('\\', '/').EndsWith("board-config.json"),
            $"Expected board-config.json but got: {written[0].path}");

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(2, config.Boards.Count, "Expected 2 boards.");
        Assert.AreEqual(3, config.Boards[0].Columns.Count, "Expected 3 columns on board 0.");
        Assert.AreEqual(3, config.Boards[1].Columns.Count, "Expected 3 columns on board 1.");
    }

    // ---------------------------------------------------------------------------
    // (b) T022: Export_WhenColumnHasNoWipLimit_SerializesNullItemLimit
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenColumnHasNoWipLimit_SerializesNullItemLimit()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Proposed", itemLimit: null))));

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(1, written.Count);
        // null itemLimit must not appear in JSON (WhenWritingNull)
        Assert.IsFalse(written[0].json.Contains("\"itemLimit\""),
            "Null itemLimit should be omitted from the JSON.");
    }

    // ---------------------------------------------------------------------------
    // (c) T022: Export_WhenTfsConnector_CapabilityAbsent_SkipsExport
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenTfsConnector_CapabilityAbsent_SkipsExport()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(false);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(0, written.Count, "PersistContentAsync must NOT be called when capability is absent.");
        adapter.Verify(a => a.GetBoardsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------
    // (d) T074: Export_WhenBoardColumnsDisabled_WritesJsonWithoutColumnData
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBoardColumnsDisabled_WritesJsonWithoutColumnData()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(
                   MakeBoard("Stories", MakeColumn("Proposed"), MakeColumn("Active", 5), MakeColumn("Done"))));

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);

        var options = new BoardConfigExtensionOptions { Columns = false };
        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap, options).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(1, written.Count, "board-config.json should still be written.");
        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(1, config.Boards.Count);
        Assert.AreEqual(0, config.Boards[0].Columns.Count,
            "Columns should be empty when Columns option is disabled.");
    }

    // ---------------------------------------------------------------------------
    // (e) T075: Export_WhenAtProcessDefaultColumns_CapturesDefaultLayout
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenAtProcessDefaultColumns_CapturesDefaultLayout()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories",
                   new BoardColumn("Proposed",  BoardColumnType.Incoming,   null, false, null, []),
                   new BoardColumn("Active",    BoardColumnType.InProgress, null, false, null, []),
                   new BoardColumn("Resolved",  BoardColumnType.Outgoing,   null, false, null, []))));

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(1, written.Count);
        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(3, config.Boards[0].Columns.Count, "All 3 default columns should be captured.");
        Assert.AreEqual("Proposed", config.Boards[0].Columns[0].Name);
        Assert.AreEqual("Active",   config.Boards[0].Columns[1].Name);
        Assert.AreEqual("Resolved", config.Boards[0].Columns[2].Name);
    }

    // ===========================================================================
    // Phase 5 — US2: Swimlanes (T032–T036)
    // ===========================================================================

    private static BoardConfig MakeBoardWithLanes(string name, BoardSwimLane[] lanes, params BoardColumn[] columns)
        => new(name, columns.Length > 0 ? columns : [MakeColumn("Col")], lanes);

    private static void SetupEmptyBacklogs(Mock<ITeamBoardAdapter> adapter)
        => adapter.Setup(a => a.GetBacklogsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(BacklogsFrom());

    private static void SetupEmptyTaskboard(Mock<ITeamBoardAdapter> adapter)
        => adapter.Setup(a => a.GetTaskboardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(TaskboardColumnsFrom());

    // ---------------------------------------------------------------------------
    // T032a: Export_WhenSwimLanesExist_WritesBoardConfigWithSwimLanes
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenSwimLanesExist_WritesBoardConfigWithSwimLanes()
    {
        var lanes = new BoardSwimLane[]
        {
            new("lane-1", "Expedite"),
            new("lane-2", "Normal"),
        };
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoardWithLanes("Stories", lanes, MakeColumn("Active"))));
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(1, written.Count);
        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(2, config.Boards[0].SwimLanes.Count, "Expected 2 swimlanes.");
        Assert.AreEqual("Expedite", config.Boards[0].SwimLanes[0].Name);
        Assert.AreEqual("Normal",   config.Boards[0].SwimLanes[1].Name);
    }

    // ---------------------------------------------------------------------------
    // T032b: Export_WhenBoardHasNoSwimLanes_WritesEmptySwimLanes
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBoardHasNoSwimLanes_WritesEmptySwimLanes()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(new BoardConfig("Stories", [MakeColumn("Active")], [])));
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.Boards[0].SwimLanes.Count, "Expected empty swimlanes.");
    }

    // ---------------------------------------------------------------------------
    // T074b: Export_WhenSwimLanesDisabled_WritesJsonWithoutSwimLaneData
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenSwimLanesDisabled_WritesJsonWithoutSwimLaneData()
    {
        var lanes = new BoardSwimLane[] { new("lane-1", "Expedite"), new("lane-2", "Normal") };
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoardWithLanes("Stories", lanes, MakeColumn("Active"))));
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var options = new BoardConfigExtensionOptions { SwimLanes = false };
        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap, options).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.Boards[0].SwimLanes.Count, "SwimLanes must be empty when option disabled.");
    }

    // ===========================================================================
    // Phase 6 — US3: Card Rules (T037–T041)
    // ===========================================================================

    // ---------------------------------------------------------------------------
    // T037a: Export_WhenCardRulesExist_WritesCardRulesToBoardConfig
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenCardRulesExist_WritesCardRulesToBoardConfig()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetCardRuleSettingsAsync("Proj", "team-1", "Stories", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CardRuleSettings([new CardRule("High Priority", "#ff0000", true, "[Priority] = 1")]));
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.IsNotNull(config.CardRules, "CardRules should be present.");
        Assert.AreEqual(1, config.CardRules.Rules.Count, "Expected 1 card rule.");
        Assert.AreEqual("High Priority", config.CardRules.Rules[0].Name);
    }

    // ---------------------------------------------------------------------------
    // T037b: Export_WhenBoardHasNoCardRules_WritesNullCardRules
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBoardHasNoCardRules_WritesNullCardRules()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetCardRuleSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((CardRuleSettings?)null);
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.IsNull(config.CardRules, "CardRules should be null when board has no rules.");
    }

    // ---------------------------------------------------------------------------
    // T074c: Export_WhenCardRulesDisabled_OmitsCardRules
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenCardRulesDisabled_OmitsCardRules()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        SetupEmptyBacklogs(adapter);
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var options = new BoardConfigExtensionOptions { CardRules = false };
        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap, options).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.IsNull(config.CardRules, "CardRules must be null when option is disabled.");
        adapter.Verify(a => a.GetCardRuleSettingsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "GetCardRuleSettingsAsync must not be called when CardRules option is false.");
    }

    // ===========================================================================
    // Phase 7 — US4: Backlogs (T042–T046)
    // ===========================================================================

    private static async IAsyncEnumerable<BacklogMetadata> BacklogsFrom(params BacklogMetadata[] items)
    {
        foreach (var b in items) { yield return b; await Task.CompletedTask; }
    }

    // ---------------------------------------------------------------------------
    // T042a: Export_WhenBacklogsExist_WritesBacklogMetadata
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBacklogsExist_WritesBacklogMetadata()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetBacklogsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BacklogsFrom(
                   new BacklogMetadata("Epics",   "Microsoft.EpicCategory",       BacklogLevelType.Portfolio,   1),
                   new BacklogMetadata("Stories", "Microsoft.RequirementCategory", BacklogLevelType.Requirement, 2)));
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(2, config.Backlogs.Count, "Expected 2 backlog entries.");
        Assert.AreEqual("Epics",   config.Backlogs[0].Name);
        Assert.AreEqual("Stories", config.Backlogs[1].Name);
    }

    // ---------------------------------------------------------------------------
    // T042b: Export_WhenBacklogsCapabilityAbsent_SkipsBacklogs
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBacklogsCapabilityAbsent_SkipsBacklogs()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);
        cap.Setup(c => c.Has(Cap.Backlogs)).Returns(false);
        cap.Setup(c => c.Has(Cap.TaskboardColumns)).Returns(false);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.Backlogs.Count, "Backlogs must be empty when capability absent.");
        adapter.Verify(a => a.GetBacklogsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------
    // T074d: Export_WhenBacklogsDisabled_OmitsBacklogs
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenBacklogsDisabled_OmitsBacklogs()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var options = new BoardConfigExtensionOptions { Backlogs = false };
        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap, options).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.Backlogs.Count, "Backlogs must be empty when option disabled.");
    }

    // ---------------------------------------------------------------------------
    // T046: Export_DoesNotWriteBacklogVisibilityFlags
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_DoesNotWriteBacklogVisibilityFlags()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetBacklogsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BacklogsFrom(new BacklogMetadata("Stories", "Microsoft.RequirementCategory", BacklogLevelType.Requirement, 1)));
        SetupEmptyTaskboard(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.IsFalse(written[0].json.Contains("backlogVisibilities", StringComparison.OrdinalIgnoreCase),
            "board-config.json must not contain backlogVisibilities.");
    }

    // ===========================================================================
    // Phase 8 — US5: Taskboard Columns (T047–T051)
    // ===========================================================================

    private static async IAsyncEnumerable<TaskboardColumn> TaskboardColumnsFrom(params TaskboardColumn[] items)
    {
        foreach (var c in items) { yield return c; await Task.CompletedTask; }
    }

    // ---------------------------------------------------------------------------
    // T047a: Export_WhenTaskboardColumnsExist_WritesTaskboardColumns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenTaskboardColumnsExist_WritesTaskboardColumns()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetTaskboardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(TaskboardColumnsFrom(
                   new TaskboardColumn("To Do",       BoardColumnType.Incoming,   0, []),
                   new TaskboardColumn("In Progress", BoardColumnType.InProgress, 1, []),
                   new TaskboardColumn("Done",        BoardColumnType.Outgoing,   2, [])));
        SetupEmptyBacklogs(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(3, config.TaskboardColumns.Count, "Expected 3 taskboard columns.");
        Assert.AreEqual("To Do",       config.TaskboardColumns[0].Name);
        Assert.AreEqual("In Progress", config.TaskboardColumns[1].Name);
        Assert.AreEqual("Done",        config.TaskboardColumns[2].Name);
    }

    // ---------------------------------------------------------------------------
    // T047b: Export_WhenTaskboardCapabilityAbsent_SkipsTaskboard
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenTaskboardCapabilityAbsent_SkipsTaskboard()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        SetupEmptyBacklogs(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(true);
        cap.Setup(c => c.Has(Cap.Backlogs)).Returns(true);
        cap.Setup(c => c.Has(Cap.TaskboardColumns)).Returns(false);

        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.TaskboardColumns.Count, "TaskboardColumns must be empty when capability absent.");
        adapter.Verify(a => a.GetTaskboardColumnsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------
    // T074e: Export_WhenTaskboardColumnsDisabled_OmitsTaskboard
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Export_WhenTaskboardColumnsDisabled_OmitsTaskboard()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        SetupEmptyBacklogs(adapter);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var options = new BoardConfigExtensionOptions { TaskboardColumns = false };
        var (package, written) = CreateTrackingPackage();
        await BuildExtension(adapter, cap, options).ExportAsync(BuildContext(package), CancellationToken.None);

        var config = JsonSerializer.Deserialize<TeamBoardConfig>(written[0].json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(config);
        Assert.AreEqual(0, config.TaskboardColumns.Count, "TaskboardColumns must be empty when option disabled.");
    }

    // ===========================================================================
    // Phase 9 — US6: Import Board Configuration (T052–T073)
    // ===========================================================================

    private static readonly JsonSerializerOptions s_camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions s_caseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static TeamExtensionContext BuildImportContext(Mock<IPackageAccess> package, string slug = "alpha")
        => new()
        {
            Organisation    = "org",
            ProjectName     = "Proj",
            EntityId        = "team-1",
            TargetEntityId  = "target-team-1",
            Package         = package.Object,
            Team            = new TeamDefinition("team-1", "Alpha", string.Empty, true),
            Slug            = slug,
            SourceProjectName = "Proj",
        };

    private static Mock<IPackageAccess> PackageWith(TeamBoardConfig boardConfig)
    {
        var json     = JsonSerializer.Serialize(boardConfig, s_camelCase);
        var bytes    = Encoding.UTF8.GetBytes(json);
        var package  = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null &&
                    c.Address.RelativePath.Replace('\\', '/').EndsWith("board-config.json")),
                It.IsAny<CancellationToken>()))
               .ReturnsAsync(new PackagePayload(new MemoryStream(bytes)));
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address == null ||
                    !c.Address.RelativePath.Replace('\\', '/').EndsWith("board-config.json")),
                It.IsAny<CancellationToken>()))
               .ReturnsAsync((PackagePayload?)null);
        return package;
    }

    private static Mock<IConnectorCapabilityProvider> AllCapabilities()
    {
        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);
        return cap;
    }

    private static TeamBoardConfig MakeBoardConfig(
        string boardName = "Stories",
        IReadOnlyList<BoardColumn>? columns   = null,
        IReadOnlyList<BoardSwimLane>? lanes   = null,
        CardRuleSettings? cardRules            = null,
        IReadOnlyList<TaskboardColumn>? taskboard = null)
        => new()
        {
            TeamName   = "Alpha",
            ExportedAt = DateTimeOffset.UtcNow,
            Boards =
            [
                new BoardConfig(boardName,
                    columns  ?? [MakeColumn("Active")],
                    lanes    ?? [])
            ],
            CardRules        = cardRules,
            Backlogs         = [],
            TaskboardColumns = taskboard ?? [],
        };

    // ---------------------------------------------------------------------------
    // T052a: Import_WhenReplaceMode_UpdatesBoardColumnsPerBoard
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceMode_UpdatesBoardColumnsPerBoard()
    {
        var columns = new[] { MakeColumn("To Do"), MakeColumn("Doing"), MakeColumn("Done") };
        var boardConfig = MakeBoardConfig(columns: columns);

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(1, adapter.Invocations.Count(i => i.Method.Name == nameof(ITeamBoardAdapter.UpdateBoardColumnsAsync)),
            "UpdateBoardColumnsAsync must be called once per board.");
    }

    // ---------------------------------------------------------------------------
    // T052b: Import_WhenReplaceMode_UpdatesSwimLanesPerBoard
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceMode_UpdatesSwimLanesPerBoard()
    {
        var lanes = new BoardSwimLane[] { new("l1", "Expedite"), new("l2", "Normal") };
        var boardConfig = MakeBoardConfig(lanes: lanes);

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(1, adapter.Invocations.Count(i => i.Method.Name == nameof(ITeamBoardAdapter.UpdateSwimLanesAsync)),
            "UpdateSwimLanesAsync must be called once per board.");
    }

    // ---------------------------------------------------------------------------
    // T052c: Import_WhenReplaceMode_UpdatesCardRulesPerBoard
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceMode_UpdatesCardRulesPerBoard()
    {
        var rules = new CardRuleSettings([new CardRule("High Priority", "#ff0000", true, "[Priority] = 1")]);
        var boardConfig = MakeBoardConfig(cardRules: rules);

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(1, adapter.Invocations.Count(i => i.Method.Name == nameof(ITeamBoardAdapter.UpdateCardRuleSettingsAsync)),
            "UpdateCardRuleSettingsAsync must be called once per board.");
    }

    // ---------------------------------------------------------------------------
    // T052d: Import_WhenReplaceMode_UpdatesTaskboardColumns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceMode_UpdatesTaskboardColumns()
    {
        var taskCols = new TaskboardColumn[]
        {
            new("To Do",       BoardColumnType.Incoming,   0, []),
            new("In Progress", BoardColumnType.InProgress, 1, []),
            new("Done",        BoardColumnType.Outgoing,   2, []),
        };
        var boardConfig = MakeBoardConfig(taskboard: taskCols);

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(1, adapter.Invocations.Count(i => i.Method.Name == nameof(ITeamBoardAdapter.UpdateTaskboardColumnsAsync)),
            "UpdateTaskboardColumnsAsync must be called once.");
    }

    // ---------------------------------------------------------------------------
    // T052e: Import_WhenCapabilityAbsent_SkipsAllUpdates
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenCapabilityAbsent_SkipsAllUpdates()
    {
        var boardConfig = MakeBoardConfig();
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package     = PackageWith(boardConfig);

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(false);

        await BuildExtension(adapter, cap)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(0, adapter.Invocations.Count(i =>
            i.Method.Name.StartsWith("Update")), "No Update* calls when capability absent.");
    }

    // ---------------------------------------------------------------------------
    // T052f: Import_WhenNoPackageArtifact_ReturnsEarly
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenNoPackageArtifact_ReturnsEarly()
    {
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.RequestContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((PackagePayload?)null);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(0, adapter.Invocations.Count(i =>
            i.Method.Name.StartsWith("Update")), "No Update* calls when artifact is absent.");
    }

    // ---------------------------------------------------------------------------
    // T052g: Import_WhenColumnsDisabled_SkipsColumnUpdates
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenColumnsDisabled_SkipsColumnUpdates()
    {
        var boardConfig = MakeBoardConfig();
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package     = PackageWith(boardConfig);

        var options = new BoardConfigExtensionOptions { Columns = false };
        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(0, adapter.Invocations.Count(i =>
            i.Method.Name == nameof(ITeamBoardAdapter.UpdateBoardColumnsAsync)),
            "UpdateBoardColumnsAsync must not be called when Columns option is false.");
    }

    // ---------------------------------------------------------------------------
    // T052h: Import_WhenSkipModeAndTargetHasConfig_SkipsAllUpdates
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenSkipModeAndTargetHasConfig_SkipsAllUpdates()
    {
        var boardConfig = MakeBoardConfig("Stories", [MakeColumn("Active")]);
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);

        // Target has existing boards — Skip mode should not update
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom(MakeBoard("Stories", MakeColumn("Active"))));
        adapter.Setup(a => a.GetCurrentTaskboardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { new TaskboardColumn("To Do", BoardColumnType.Incoming, 0, []) });

        var package = PackageWith(boardConfig);
        var options = new BoardConfigExtensionOptions { ImportMode = BoardConfigImportMode.Skip };

        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(0, adapter.Invocations.Count(i =>
            i.Method.Name.StartsWith("Update")), "No Update* calls in Skip mode when target already has board config.");
    }

    // ---------------------------------------------------------------------------
    // T052i: Import_WhenReplaceMode_ColumnDataMatchesPackage
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceMode_ColumnDataMatchesPackage()
    {
        var columns = new[] { MakeColumn("Todo", 3), MakeColumn("Doing", 5), MakeColumn("Done") };
        var boardConfig = MakeBoardConfig("Stories", columns: columns);

        IReadOnlyList<BoardColumn>? captured = null;
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.UpdateBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardColumn>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, string, IReadOnlyList<BoardColumn>, CancellationToken>(
                   (_, _, _, cols, _) => captured = cols)
               .Returns(Task.CompletedTask);

        var package = PackageWith(boardConfig);
        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.IsNotNull(captured, "UpdateBoardColumnsAsync must have been called.");
        Assert.AreEqual(3, captured.Count, "Expected 3 columns from the package.");
        Assert.AreEqual("Todo",  captured[0].Name);
        Assert.AreEqual("Doing", captured[1].Name);
        Assert.AreEqual("Done",  captured[2].Name);
    }

    // ---------------------------------------------------------------------------
    // T070: Import_WhenBoardAbsentFromTarget_LogsWarningAndContinuesOtherBoards
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenBoardAbsentFromTarget_LogsWarningAndContinuesOtherBoards()
    {
        var boardConfig = new TeamBoardConfig
        {
            TeamName   = "Alpha",
            ExportedAt = DateTimeOffset.UtcNow,
            Boards =
            [
                new BoardConfig("Stories",   [MakeColumn("Active")], []),
                new BoardConfig("GhostBoard", [MakeColumn("Active")], []),
            ],
            CardRules        = null,
            Backlogs         = [],
            TaskboardColumns = [],
        };

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(2, adapter.Invocations.Count(i =>
            i.Method.Name == nameof(ITeamBoardAdapter.UpdateBoardColumnsAsync)),
            "Both boards attempted in Replace mode — adapter handles missing-board at API level.");
    }

    // ---------------------------------------------------------------------------
    // T071: Import_WhenTargetEntityIdNull_ReturnsEarlyWithoutUpdates
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenTargetEntityIdNull_ReturnsEarlyWithoutUpdates()
    {
        var boardConfig = MakeBoardConfig();
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        var package     = PackageWith(boardConfig);

        // TargetEntityId = null simulates the team not existing on target
        var ctx = new TeamExtensionContext
        {
            Organisation     = "org",
            ProjectName      = "Proj",
            EntityId         = "team-1",
            TargetEntityId   = null,
            Package          = package.Object,
            Team             = new TeamDefinition("team-1", "Alpha", string.Empty, true),
            Slug             = "alpha",
            SourceProjectName = "Proj",
        };

        await BuildExtension(adapter, AllCapabilities()).ImportAsync(ctx, CancellationToken.None);

        Assert.AreEqual(0, adapter.Invocations.Count(i => i.Method.Name.StartsWith("Update")),
            "No Update* calls when TargetEntityId is null.");
    }

    // ---------------------------------------------------------------------------
    // T057a: Import_WhenMergeMode_PreservesTargetOnlyColumns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenMergeMode_PreservesTargetOnlyColumns()
    {
        // Package has [Active]; target currently has [Active, Resolved]
        // After merge: [Active (from package), Resolved (target-only, preserved)]
        var packageColumns = new[] { MakeColumn("Active", 5) };
        var targetColumns  = new[] { MakeColumn("Active"), MakeColumn("Resolved") };

        var boardConfig = MakeBoardConfig(columns: packageColumns);
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<BoardColumn>)targetColumns);

        IReadOnlyList<BoardColumn>? merged = null;
        adapter.Setup(a => a.UpdateBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardColumn>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, string, IReadOnlyList<BoardColumn>, CancellationToken>(
                   (_, _, _, cols, _) => merged = cols)
               .Returns(Task.CompletedTask);

        var options = new BoardConfigExtensionOptions { ImportMode = BoardConfigImportMode.Merge };
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.IsNotNull(merged);
        Assert.AreEqual(2, merged.Count, "Merged list should have 2 columns.");
        Assert.AreEqual("Active",   merged[0].Name, "Package column comes first.");
        Assert.AreEqual(5,          merged[0].ItemLimit, "Package column's WIP limit applied.");
        Assert.AreEqual("Resolved", merged[1].Name, "Target-only column preserved.");
    }

    // ---------------------------------------------------------------------------
    // T057b: Import_WhenMergeMode_PreservesTargetOnlySwimLanes
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenMergeMode_PreservesTargetOnlySwimLanes()
    {
        var packageLanes = new BoardSwimLane[] { new("l1", "Expedite") };
        var targetLanes  = new BoardSwimLane[] { new("l1", "Expedite"), new("l2", "Normal") };

        var boardConfig = MakeBoardConfig(lanes: packageLanes);
        var adapter     = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardSwimLanesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<BoardSwimLane>)targetLanes);
        adapter.Setup(a => a.GetBoardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<BoardColumn>());

        IReadOnlyList<BoardSwimLane>? merged = null;
        adapter.Setup(a => a.UpdateSwimLanesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardSwimLane>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, string, IReadOnlyList<BoardSwimLane>, CancellationToken>(
                   (_, _, _, lanes, _) => merged = lanes)
               .Returns(Task.CompletedTask);

        var options = new BoardConfigExtensionOptions { ImportMode = BoardConfigImportMode.Merge };
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.IsNotNull(merged);
        Assert.AreEqual(2, merged.Count, "Merged list should have 2 lanes.");
        Assert.AreEqual("Expedite", merged[0].Name);
        Assert.AreEqual("Normal",   merged[1].Name, "Target-only lane preserved.");
    }

    // ---------------------------------------------------------------------------
    // T057c: Import_WhenMergeMode_PreservesTargetOnlyTaskboardColumns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenMergeMode_PreservesTargetOnlyTaskboardColumns()
    {
        var pkgTaskCols = new TaskboardColumn[]
        {
            new("To Do", BoardColumnType.Incoming, 0, []),
        };
        var targetTaskCols = new TaskboardColumn[]
        {
            new("To Do",       BoardColumnType.Incoming,   0, []),
            new("In Progress", BoardColumnType.InProgress, 1, []),
        };

        var boardConfig = new TeamBoardConfig
        {
            TeamName   = "Alpha",
            ExportedAt = DateTimeOffset.UtcNow,
            Boards     = [MakeBoard("Stories", MakeColumn("Active"))],
            CardRules  = null,
            Backlogs   = [],
            TaskboardColumns = pkgTaskCols,
        };

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<BoardColumn>());
        adapter.Setup(a => a.GetBoardSwimLanesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<BoardSwimLane>());
        adapter.Setup(a => a.GetCurrentTaskboardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<TaskboardColumn>)targetTaskCols);

        IReadOnlyList<TaskboardColumn>? merged = null;
        adapter.Setup(a => a.UpdateTaskboardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<TaskboardColumn>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, IReadOnlyList<TaskboardColumn>, CancellationToken>(
                   (_, _, cols, _) => merged = cols)
               .Returns(Task.CompletedTask);

        var options = new BoardConfigExtensionOptions { ImportMode = BoardConfigImportMode.Merge };
        var package = PackageWith(boardConfig);

        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.IsNotNull(merged);
        Assert.AreEqual(2, merged.Count, "Merged list should have 2 taskboard columns.");
        Assert.AreEqual("To Do",       merged[0].Name);
        Assert.AreEqual("In Progress", merged[1].Name, "Target-only column preserved.");
    }

    // ---------------------------------------------------------------------------
    // T060: Import_WhenStateMappingReferencesAbsentTargetState_OmitsMappingAndContinues
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenStateMappingReferencesAbsentTargetState_OmitsMappingAndContinues()
    {
        // Package column maps Bug→"Closed" which doesn't exist in target; Bug→"Active" is valid.
        var packageColumns = new[]
        {
            new BoardColumn("Active", BoardColumnType.InProgress, null, false, null,
            [
                new BoardColumnStateMapping("Bug", "Active"),
                new BoardColumnStateMapping("Bug", "Closed"),
            ])
        };

        // Target board currently has Bug→"Active" only — "Closed" is absent from the target process.
        var targetColumns = new[]
        {
            new BoardColumn("Active", BoardColumnType.InProgress, null, false, null,
            [
                new BoardColumnStateMapping("Bug", "Active"),
            ])
        };

        var boardConfig = MakeBoardConfig(columns: packageColumns);
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.GetBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<BoardColumn>)targetColumns);

        IReadOnlyList<BoardColumn>? captured = null;
        adapter.Setup(a => a.UpdateBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardColumn>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, string, IReadOnlyList<BoardColumn>, CancellationToken>(
                   (_, _, _, cols, _) => captured = cols)
               .Returns(Task.CompletedTask);

        var package = PackageWith(boardConfig);
        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.IsNotNull(captured, "UpdateBoardColumnsAsync must have been called.");
        Assert.AreEqual(1, captured.Count, "Column should be imported.");
        Assert.AreEqual(1, captured[0].StateMappings.Count,
            "Only the valid 'Active' mapping should remain; 'Closed' must be omitted.");
        Assert.AreEqual("Active", captured[0].StateMappings[0].State,
            "The remaining mapping must be the valid 'Active' state.");
    }

    // ---------------------------------------------------------------------------
    // T072: Import_WhenUpdateThrowsUnauthorized_LogsWarningAndContinues
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenUpdateThrowsUnauthorized_LogsWarningAndContinues()
    {
        var boardConfig = new TeamBoardConfig
        {
            TeamName   = "Alpha",
            ExportedAt = DateTimeOffset.UtcNow,
            Boards =
            [
                new BoardConfig("Stories",  [MakeColumn("Active")], []),
                new BoardConfig("Epics",    [MakeColumn("New")], []),
            ],
            CardRules        = null,
            Backlogs         = [],
            TaskboardColumns = [],
        };

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        // First board throws permission denied; second board should still be attempted
        adapter.SetupSequence(a => a.UpdateBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardColumn>>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new UnauthorizedAccessException("403 Forbidden"))
               .Returns(Task.CompletedTask);

        var package = PackageWith(boardConfig);

        // Must not throw — permission errors are caught and logged as warnings
        await BuildExtension(adapter, AllCapabilities())
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(2, adapter.Invocations.Count(i =>
            i.Method.Name == nameof(ITeamBoardAdapter.UpdateBoardColumnsAsync)),
            "Second board must still be attempted after permission error on first.");
    }

    // ---------------------------------------------------------------------------
    // US6 scenario 6e: Import_WhenSkipModeAndTargetHasNoConfig_AppliesPackageAsReplace
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenSkipModeAndTargetHasNoConfig_AppliesPackageAsReplace()
    {
        var columns = new[] { MakeColumn("To Do"), MakeColumn("Done") };
        var boardConfig = MakeBoardConfig("Stories", columns: columns,
            taskboard: [new TaskboardColumn("To Do", BoardColumnType.Incoming, 0, [])]);

        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);

        // Target reports no existing boards and no taskboard columns → Skip behaves like Replace
        adapter.Setup(a => a.GetBoardsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(BoardsFrom());
        adapter.Setup(a => a.GetCurrentTaskboardColumnsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<TaskboardColumn>());

        var package = PackageWith(boardConfig);
        var options = new BoardConfigExtensionOptions { ImportMode = BoardConfigImportMode.Skip };

        await BuildExtension(adapter, AllCapabilities(), options)
            .ImportAsync(BuildImportContext(package), CancellationToken.None);

        Assert.AreEqual(1, adapter.Invocations.Count(i =>
            i.Method.Name == nameof(ITeamBoardAdapter.UpdateBoardColumnsAsync)),
            "Skip mode with no existing target config must apply columns as Replace.");
        Assert.AreEqual(1, adapter.Invocations.Count(i =>
            i.Method.Name == nameof(ITeamBoardAdapter.UpdateTaskboardColumnsAsync)),
            "Skip mode with no existing taskboard must apply taskboard columns as Replace.");
    }

    // ---------------------------------------------------------------------------
    // SC-004: Import_WhenReplaceModeRunTwice_ProducesIdenticalTargetState
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task Import_WhenReplaceModeRunTwice_ProducesIdenticalTargetState()
    {
        var columns = new[] { MakeColumn("To Do", 2), MakeColumn("Doing", 3), MakeColumn("Done") };
        var boardConfig = MakeBoardConfig("Stories", columns: columns);

        // Use a factory-returning setup so each RequestContentAsync call gets a fresh stream
        var packageMock = new Mock<IPackageAccess>(MockBehavior.Loose);
        packageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null &&
                    c.Address.RelativePath.Replace('\\', '/').EndsWith("board-config.json")),
                It.IsAny<CancellationToken>()))
               .ReturnsAsync(() =>
               {
                   var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(boardConfig, s_camelCase));
                   return new PackagePayload(new MemoryStream(bytes));
               });
        packageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address == null ||
                    !c.Address.RelativePath.Replace('\\', '/').EndsWith("board-config.json")),
                It.IsAny<CancellationToken>()))
               .ReturnsAsync((PackagePayload?)null);

        var allCaptures = new List<string[]>();
        var adapter = new Mock<ITeamBoardAdapter>(MockBehavior.Loose);
        adapter.Setup(a => a.UpdateBoardColumnsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BoardColumn>>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, string, IReadOnlyList<BoardColumn>, CancellationToken>(
                   (_, _, _, cols, _) => allCaptures.Add(cols.Select(c => c.Name).ToArray()))
               .Returns(Task.CompletedTask);

        var ext = BuildExtension(adapter, AllCapabilities());
        var ctx = BuildImportContext(packageMock);

        await ext.ImportAsync(ctx, CancellationToken.None);
        await ext.ImportAsync(ctx, CancellationToken.None);

        Assert.AreEqual(2, allCaptures.Count,
            "UpdateBoardColumnsAsync must be called once per run (2 runs total).");
        CollectionAssert.AreEqual(allCaptures[0], allCaptures[1],
            "Column names sent to the adapter must be identical on both runs (SC-004).");
    }
}

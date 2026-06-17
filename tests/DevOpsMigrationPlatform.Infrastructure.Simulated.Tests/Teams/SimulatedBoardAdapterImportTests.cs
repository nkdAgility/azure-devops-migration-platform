// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Teams;

/// <summary>
/// System tests — full import acceptance scenarios using SimulatedBoardAdapter as both
/// the package source (via a round-trip export) and the target sink (captured calls).
/// Modes: Replace, Merge, Skip.
/// </summary>
[TestClass]
[TestCategory("SystemTest")]
[TestCategory("SystemTest_Simulated")]
public sealed class SimulatedBoardAdapterImportTests
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TeamExtensionContext BuildContext(Mock<IPackageAccess> package, string targetId = "target-team")
        => new()
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "source-team",
            TargetEntityId = targetId,
            Package = package.Object,
            Team = new TeamDefinition("source-team", "SimTeam", string.Empty, true),
            Slug = "simteam",
            SourceProjectName = "Proj",
        };

    private static IConnectorCapabilityProvider AllCapabilities()
    {
        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);
        return cap.Object;
    }

    private static BoardConfigTeamExtension BuildExtension(
        BoardConfigExtensionOptions options,
        ITeamBoardAdapter adapter,
        IConnectorCapabilityProvider? capProvider = null)
        => new(
            Options.Create(options),
            adapter,
            capProvider ?? AllCapabilities(),
            metrics: null,
            logger: NullLogger<BoardConfigTeamExtension>.Instance);

    /// <summary>
    /// Builds an IPackageAccess mock that returns the given TeamBoardConfig as its artefact content.
    /// </summary>
    private static Mock<IPackageAccess> PackageWithConfig(TeamBoardConfig config)
    {
        var json = JsonSerializer.Serialize(config, s_writeOptions);
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                return new PackagePayload(new MemoryStream(bytes, writable: false), "application/json");
            });
        return package;
    }

    /// <summary>
    /// Builds a TeamBoardConfig from the SimulatedBoardAdapter's seeded data (mirroring ExportAsync).
    /// </summary>
    private static async Task<TeamBoardConfig> BuildPackageConfigAsync(
        BoardConfigExtensionOptions options,
        CancellationToken ct = default)
    {
        var adapter = new SimulatedBoardAdapter();
        var boards = new List<BoardConfig>();
        var cardRulesPerBoard = new Dictionary<string, CardRuleSettings?>();

        await foreach (var board in adapter.GetBoardsAsync("Proj", "source-team", ct))
        {
            var columns = options.Columns ? board.Columns : (IReadOnlyList<BoardColumn>)[];
            var lanes = options.SwimLanes ? board.SwimLanes : (IReadOnlyList<BoardSwimLane>)[];
            boards.Add(new BoardConfig(board.BoardName, columns, lanes));
            if (options.CardRules)
                cardRulesPerBoard[board.BoardName] =
                    await adapter.GetCardRuleSettingsAsync("Proj", "source-team", board.BoardName, ct);
        }

        CardRuleSettings? aggRules = null;
        if (options.CardRules && cardRulesPerBoard.Count > 0)
        {
            var all = cardRulesPerBoard.Values
                .Where(r => r is not null)
                .SelectMany(r => r!.Rules)
                .ToList();
            if (all.Count > 0) aggRules = new CardRuleSettings(all);
        }

        var backlogs = new List<BacklogMetadata>();
        if (options.Backlogs)
            await foreach (var b in adapter.GetBacklogsAsync("Proj", "source-team", ct))
                backlogs.Add(b);

        var taskCols = new List<TaskboardColumn>();
        if (options.TaskboardColumns)
            await foreach (var c in adapter.GetTaskboardColumnsAsync("Proj", "source-team", ct))
                taskCols.Add(c);

        return new TeamBoardConfig
        {
            TeamName = "SimTeam",
            ExportedAt = DateTimeOffset.UtcNow,
            Boards = boards,
            CardRules = aggRules,
            Backlogs = backlogs,
            TaskboardColumns = taskCols,
        };
    }

    // ---------------------------------------------------------------------------
    // (a) Replace mode — all boards written to target
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_Replace_CallsUpdateForAllBoards()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            SwimLanes = true,
            CardRules = true,
            TaskboardColumns = true,
            ImportMode = BoardConfigImportMode.Replace,
        };

        var packageConfig = await BuildPackageConfigAsync(options);
        var package = PackageWithConfig(packageConfig);
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(2, target.UpdateBoardColumnsCalls.Count,
            "UpdateBoardColumnsAsync called once per board (2 boards seeded)");
        Assert.AreEqual(2, target.UpdateSwimLanesCalls.Count,
            "UpdateSwimLanesAsync called once per board");
        Assert.AreEqual(2, target.UpdateCardRuleSettingsCalls.Count,
            "UpdateCardRuleSettingsAsync called once per board");
        Assert.AreEqual(1, target.UpdateTaskboardColumnsCalls.Count,
            "UpdateTaskboardColumnsAsync called once");
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_Replace_ColumnNamesMatchSourceBoards()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Replace,
        };

        var packageConfig = await BuildPackageConfigAsync(options);
        var package = PackageWithConfig(packageConfig);
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        var storiesCall = target.UpdateBoardColumnsCalls.FirstOrDefault(c => c.BoardName == "Stories");
        Assert.IsNotNull(storiesCall.BoardName, "UpdateBoardColumnsAsync called for Stories board");

        var colNames = storiesCall.Columns.Select(c => c.Name).ToList();
        CollectionAssert.Contains(colNames, "Proposed");
        CollectionAssert.Contains(colNames, "Active");
        CollectionAssert.Contains(colNames, "Resolved");
    }

    // ---------------------------------------------------------------------------
    // (b) Merge mode — source columns merged with existing target columns
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_Merge_IncludesSourceColumnsInUpdate()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Merge,
        };

        var packageConfig = await BuildPackageConfigAsync(options);
        var package = PackageWithConfig(packageConfig);
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        // Merge combines source + target; source columns must appear
        var storiesCall = target.UpdateBoardColumnsCalls.FirstOrDefault(c => c.BoardName == "Stories");
        Assert.IsNotNull(storiesCall.BoardName, "Stories board updated in Merge mode");
        var colNames = storiesCall.Columns.Select(c => c.Name).ToList();
        CollectionAssert.Contains(colNames, "Proposed");
    }

    // ---------------------------------------------------------------------------
    // (c) Skip mode — boards that already exist on target are not updated
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_Skip_OmitsUpdateForBoardsThatAlreadyExist()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Skip,
        };

        var packageConfig = await BuildPackageConfigAsync(options);
        var package = PackageWithConfig(packageConfig);

        // The SimulatedBoardAdapter returns Stories + Epics boards from GetBoardsAsync.
        // In Skip mode the extension queries these as "existing" and skips all of them.
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        // Both boards already exist in simulated target → zero columns updates expected
        Assert.AreEqual(0, target.UpdateBoardColumnsCalls.Count,
            "No column updates when all boards exist in Skip mode");
    }

    // ---------------------------------------------------------------------------
    // (d) Capability gate — no-op when BoardConfig capability absent
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_WhenCapabilityAbsent_MakesNoTargetCalls()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Replace,
        };

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(false);

        var packageConfig = await BuildPackageConfigAsync(new BoardConfigExtensionOptions { Columns = true });
        var package = PackageWithConfig(packageConfig);
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target, cap.Object);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(0, target.UpdateBoardColumnsCalls.Count, "No target calls when capability absent");
    }

    // ---------------------------------------------------------------------------
    // (e) TargetEntityId null — no-op
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_WhenTargetEntityIdNull_MakesNoTargetCalls()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Replace,
        };

        var packageConfig = await BuildPackageConfigAsync(options);
        var package = PackageWithConfig(packageConfig);
        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        var ctxNoTarget = BuildContext(package, targetId: null!);
        await ext.ImportAsync(ctxNoTarget, CancellationToken.None);

        Assert.AreEqual(0, target.UpdateBoardColumnsCalls.Count, "No calls when TargetEntityId is null");
    }

    // ---------------------------------------------------------------------------
    // (f) Package missing — no-op when board-config.json not present
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Import_WhenPackageMissing_MakesNoTargetCalls()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            ImportMode = BoardConfigImportMode.Replace,
        };

        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var target = new SimulatedBoardAdapter();
        var ext = BuildExtension(options, target);

        await ext.ImportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(0, target.UpdateBoardColumnsCalls.Count, "No calls when package artefact missing");
    }
}

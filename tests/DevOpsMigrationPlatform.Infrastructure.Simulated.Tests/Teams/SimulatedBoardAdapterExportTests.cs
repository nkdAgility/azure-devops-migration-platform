// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Text.Json;
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
/// System tests — full export round-trip: SimulatedBoardAdapter (seeded source) →
/// BoardConfigTeamExtension.ExportAsync → board-config.json payload → verify JSON shape.
/// </summary>
[TestClass]
[TestCategory("SystemTest")]
[TestCategory("SystemTest_Simulated")]
public sealed class SimulatedBoardAdapterExportTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static (Mock<IPackageAccess> package, List<(string Path, string Json)> written)
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

    private static TeamExtensionContext BuildContext(Mock<IPackageAccess> package)
        => new()
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "team-sim",
            TargetEntityId = null,
            Package = package.Object,
            Team = new TeamDefinition("team-sim", "SimTeam", string.Empty, true),
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
        IConnectorCapabilityProvider? capProvider = null)
        => new(
            Options.Create(options),
            new SimulatedBoardAdapter(),
            capProvider ?? AllCapabilities(),
            new DevOpsMigrationPlatform.Infrastructure.Agent.Teams.BoardConfigMergeTool(),
            metrics: null,
            logger: NullLogger<BoardConfigTeamExtension>.Instance);

    // ---------------------------------------------------------------------------
    // (a) boards / columns
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenColumnsEnabled_WritesBoardConfigJsonWithBoards()
    {
        var options = new BoardConfigExtensionOptions { Columns = true };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(1, written.Count, "Exactly one artefact written");
        Assert.IsTrue(written[0].Path.EndsWith("board-config.json", StringComparison.OrdinalIgnoreCase));

        using var doc = JsonDocument.Parse(written[0].Json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("boards", out var boards), "boards property present");
        Assert.AreEqual(2, boards.GetArrayLength(), "SimulatedBoardAdapter seeds 2 boards");

        var boardNames = Enumerable.Range(0, boards.GetArrayLength())
            .Select(i => boards[i].GetProperty("boardName").GetString())
            .ToList();
        CollectionAssert.Contains(boardNames, "Stories");
        CollectionAssert.Contains(boardNames, "Epics");

        // Columns are included when Columns = true
        foreach (var board in boards.EnumerateArray())
        {
            Assert.IsTrue(board.TryGetProperty("columns", out var cols), "columns present on each board");
            Assert.IsTrue(cols.GetArrayLength() > 0, $"board '{board.GetProperty("boardName")}' has at least one column");
        }
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenColumnsDisabled_WritesEmptyColumnsArrays()
    {
        var options = new BoardConfigExtensionOptions { Columns = false };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        var boards = doc.RootElement.GetProperty("boards");
        foreach (var board in boards.EnumerateArray())
        {
            var cols = board.GetProperty("columns");
            Assert.AreEqual(0, cols.GetArrayLength(), "columns suppressed when Columns=false");
        }
    }

    // ---------------------------------------------------------------------------
    // (b) swimlanes
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenSwimLanesEnabled_WritesSwimLanesForStoriesBoard()
    {
        var options = new BoardConfigExtensionOptions { SwimLanes = true };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        var boards = doc.RootElement.GetProperty("boards");
        var storiesBoard = boards.EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("boardName").GetString() == "Stories");

        Assert.IsTrue(storiesBoard.ValueKind != JsonValueKind.Undefined, "Stories board found");
        Assert.IsTrue(storiesBoard.TryGetProperty("swimLanes", out var lanes), "swimLanes property present");
        Assert.AreEqual(2, lanes.GetArrayLength(), "SimulatedBoardAdapter seeds 2 swim lanes for Stories");
    }

    // ---------------------------------------------------------------------------
    // (c) card rules
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenCardRulesEnabled_WritesCardRulesProperty()
    {
        var options = new BoardConfigExtensionOptions { CardRules = true };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("cardRules", out var rules), "cardRules property present");
        Assert.IsTrue(rules.TryGetProperty("rules", out var ruleArr), "cardRules.rules array present");
        Assert.IsTrue(ruleArr.GetArrayLength() > 0, "At least one card rule seeded");

        var first = ruleArr[0];
        Assert.IsTrue(first.TryGetProperty("name", out _), "Rule has name");
        Assert.IsTrue(first.TryGetProperty("color", out _), "Rule has color");
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenCardRulesDisabled_OmitsCardRulesFromJson()
    {
        var options = new BoardConfigExtensionOptions { CardRules = false };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("cardRules", out _),
            "cardRules omitted when CardRules=false (WhenWritingNull serializer option)");
    }

    // ---------------------------------------------------------------------------
    // (d) backlogs
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenBacklogsEnabled_WritesBacklogsArray()
    {
        var options = new BoardConfigExtensionOptions { Backlogs = true };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("backlogs", out var backlogs), "backlogs present");
        Assert.AreEqual(2, backlogs.GetArrayLength(), "SimulatedBoardAdapter seeds 2 backlog levels");

        foreach (var b in backlogs.EnumerateArray())
        {
            Assert.IsTrue(b.TryGetProperty("name", out _), "Backlog has name");
            Assert.IsTrue(b.TryGetProperty("witCategory", out _), "Backlog has witCategory");
        }
    }

    // ---------------------------------------------------------------------------
    // (e) taskboard columns
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenTaskboardColumnsEnabled_WritesTaskboardColumnsArray()
    {
        var options = new BoardConfigExtensionOptions { TaskboardColumns = true };
        var ext = BuildExtension(options);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        using var doc = JsonDocument.Parse(written[0].Json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("taskboardColumns", out var cols), "taskboardColumns present");
        Assert.AreEqual(3, cols.GetArrayLength(), "SimulatedBoardAdapter seeds 3 taskboard columns");

        foreach (var col in cols.EnumerateArray())
            Assert.IsTrue(col.TryGetProperty("name", out _), "TaskboardColumn has name");
    }

    // ---------------------------------------------------------------------------
    // (f) capability gate
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_WhenCapabilityAbsent_WritesNoArtifact()
    {
        var options = new BoardConfigExtensionOptions();
        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(Cap.BoardConfig)).Returns(false);
        var ext = BuildExtension(options, cap.Object);
        var (package, written) = CreateTrackingPackage();

        await ext.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.AreEqual(0, written.Count, "No artefact written when capability absent");
    }

    // ---------------------------------------------------------------------------
    // (g) determinism — two exports produce identical JSON
    // ---------------------------------------------------------------------------

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Export_IsIdempotent_SameBoardStructureOnEveryRun()
    {
        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            SwimLanes = true,
            CardRules = true,
            Backlogs = true,
            TaskboardColumns = true,
        };

        var (package1, written1) = CreateTrackingPackage();
        await BuildExtension(options).ExportAsync(BuildContext(package1), CancellationToken.None);

        var (package2, written2) = CreateTrackingPackage();
        await BuildExtension(options).ExportAsync(BuildContext(package2), CancellationToken.None);

        Assert.AreEqual(1, written1.Count);
        Assert.AreEqual(1, written2.Count);

        // Parse both and compare board count + names (timestamps will differ)
        using var doc1 = JsonDocument.Parse(written1[0].Json);
        using var doc2 = JsonDocument.Parse(written2[0].Json);

        var boards1 = doc1.RootElement.GetProperty("boards");
        var boards2 = doc2.RootElement.GetProperty("boards");
        Assert.AreEqual(boards1.GetArrayLength(), boards2.GetArrayLength(), "Board count identical");

        Assert.AreEqual(
            doc1.RootElement.GetProperty("backlogs").GetArrayLength(),
            doc2.RootElement.GetProperty("backlogs").GetArrayLength(),
            "Backlog count identical");

        Assert.AreEqual(
            doc1.RootElement.GetProperty("taskboardColumns").GetArrayLength(),
            doc2.RootElement.GetProperty("taskboardColumns").GetArrayLength(),
            "TaskboardColumn count identical");
    }
}

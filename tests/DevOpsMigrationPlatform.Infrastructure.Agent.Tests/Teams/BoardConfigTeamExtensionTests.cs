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
            NullLogger<BoardConfigTeamExtension>.Instance);

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
        // 3 standard ADO columns — no WIP limits (process defaults)
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
        Assert.AreEqual(1, config.Boards.Count);
        Assert.AreEqual(3, config.Boards[0].Columns.Count, "All 3 default columns should be captured.");
        Assert.AreEqual("Proposed", config.Boards[0].Columns[0].Name);
        Assert.AreEqual("Active",   config.Boards[0].Columns[1].Name);
        Assert.AreEqual("Resolved", config.Boards[0].Columns[2].Name);
    }
}

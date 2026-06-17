// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using AbcBoardColumn = DevOpsMigrationPlatform.Abstractions.Agent.Teams.BoardColumn;
using AbcBoardColumnType = DevOpsMigrationPlatform.Abstractions.Agent.Teams.BoardColumnType;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Teams;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Adob = Microsoft.TeamFoundation.Work.WebApi;
using WorkContext = Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests.Teams;

/// <summary>
/// Contract tests for <see cref="AzureDevOpsBoardAdapter"/>:
/// wires the real adapter to a mocked <see cref="WorkHttpClient"/> — no network.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public sealed class AzureDevOpsBoardAdapterTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Mock<WorkHttpClient> BuildWorkClient()
        => new(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

    private static Mock<IAzureDevOpsClientFactory> BuildFactory(Mock<WorkHttpClient> workClient)
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Loose);
        factory
            .Setup(f => f.CreateWorkClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workClient.Object);
        return factory;
    }

    private static Mock<ISourceEndpointInfo> BuildSource()
    {
        var src = new Mock<ISourceEndpointInfo>(MockBehavior.Loose);
        src.Setup(s => s.ToOrganisationEndpoint())
           .Returns(new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg" });
        return src;
    }

    private static Mock<ITargetEndpointInfo> BuildTarget()
    {
        var tgt = new Mock<ITargetEndpointInfo>(MockBehavior.Loose);
        tgt.Setup(t => t.ToOrganisationEndpoint())
           .Returns(new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg-target" });
        return tgt;
    }

    private static AzureDevOpsBoardAdapter BuildAdapter(
        Mock<WorkHttpClient> workClient,
        Mock<ISourceEndpointInfo>? source = null,
        Mock<ITargetEndpointInfo>? target = null)
    {
        var factory = BuildFactory(workClient);
        return new AzureDevOpsBoardAdapter(
            factory.Object,
            (source ?? BuildSource()).Object,
            (target ?? BuildTarget()).Object);
    }

    // ---------------------------------------------------------------------------
    // (a) GetBoardsAsync — maps ADO board model to BoardConfig
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBoardsAsync_ReturnsMappedBoards()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardsAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Adob.BoardReference { Name = "Stories" },
                new Adob.BoardReference { Name = "Epics" },
            ]);

        var storiesBoard = new Adob.Board
        {
            Name = "Stories",
            Columns =
            [
                new Adob.BoardColumn { Name = "New",  ColumnType = Adob.BoardColumnType.Incoming },
                new Adob.BoardColumn { Name = "Active", ColumnType = Adob.BoardColumnType.InProgress, ItemLimit = 5 },
                new Adob.BoardColumn { Name = "Done", ColumnType = Adob.BoardColumnType.Outgoing },
            ],
            Rows = [],
        };
        var epicsBoard = new Adob.Board { Name = "Epics", Columns = [], Rows = [] };

        workClient
            .Setup(c => c.GetBoardAsync(It.IsAny<WorkContext>(), "Stories", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storiesBoard);
        workClient
            .Setup(c => c.GetBoardAsync(It.IsAny<WorkContext>(), "Epics", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(epicsBoard);

        var adapter = BuildAdapter(workClient);

        var boards = new List<BoardConfig>();
        await foreach (var b in adapter.GetBoardsAsync("Proj", "team-1", CancellationToken.None))
            boards.Add(b);

        Assert.AreEqual(2, boards.Count);
        var stories = boards.First(b => b.BoardName == "Stories");
        Assert.AreEqual(3, stories.Columns.Count);
        Assert.AreEqual("Active", stories.Columns[1].Name);
        Assert.AreEqual(5, stories.Columns[1].ItemLimit);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBoardsAsync_WhenGetBoardsFails_YieldsNone()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardsAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var adapter = BuildAdapter(workClient);

        var boards = new List<BoardConfig>();
        await foreach (var b in adapter.GetBoardsAsync("Proj", "team-1", CancellationToken.None))
            boards.Add(b);

        Assert.AreEqual(0, boards.Count, "Graceful empty when GetBoardsAsync throws");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBoardsAsync_WhenGetBoardFails_SkipsFailedBoard()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardsAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Adob.BoardReference { Name = "Stories" },
                new Adob.BoardReference { Name = "Epics" },
            ]);

        workClient
            .Setup(c => c.GetBoardAsync(It.IsAny<WorkContext>(), "Stories", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("access denied"));
        workClient
            .Setup(c => c.GetBoardAsync(It.IsAny<WorkContext>(), "Epics", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adob.Board { Name = "Epics", Columns = [], Rows = [] });

        var adapter = BuildAdapter(workClient);

        var boards = new List<BoardConfig>();
        await foreach (var b in adapter.GetBoardsAsync("Proj", "team-1", CancellationToken.None))
            boards.Add(b);

        Assert.AreEqual(1, boards.Count, "Failed board skipped, remaining boards still returned");
        Assert.AreEqual("Epics", boards[0].BoardName);
    }

    // ---------------------------------------------------------------------------
    // (b) GetCardRuleSettingsAsync — maps ADO card rules
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetCardRuleSettingsAsync_ReturnsMappedRules()
    {
        var workClient = BuildWorkClient();
        var adoRules = new Adob.BoardCardRuleSettings
        {
            rules = new Dictionary<string, List<Adob.Rule>>
            {
                ["fill"] =
                [
                    new Adob.Rule
                    {
                        name = "High Priority",
                        filter = "[Priority] = 1",
                        isEnabled = "true",
                        settings = new Adob.attribute { ["background-color"] = "#ff0000" },
                    },
                ],
            },
        };

        workClient
            .Setup(c => c.GetBoardCardRuleSettingsAsync(It.IsAny<WorkContext>(), "Stories", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adoRules);

        var adapter = BuildAdapter(workClient);
        var result = await adapter.GetCardRuleSettingsAsync("Proj", "team-1", "Stories", CancellationToken.None);

        Assert.IsNotNull(result, "CardRuleSettings returned");
        Assert.AreEqual(1, result.Rules.Count);
        Assert.AreEqual("High Priority", result.Rules[0].Name);
        Assert.AreEqual("#ff0000", result.Rules[0].Color);
        Assert.IsTrue(result.Rules[0].IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetCardRuleSettingsAsync_WhenEmpty_ReturnsNull()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardCardRuleSettingsAsync(It.IsAny<WorkContext>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adob.BoardCardRuleSettings { rules = [] });

        var adapter = BuildAdapter(workClient);
        var result = await adapter.GetCardRuleSettingsAsync("Proj", "team-1", "Stories", CancellationToken.None);

        Assert.IsNull(result, "Null returned when no rules exist");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetCardRuleSettingsAsync_WhenFails_ReturnsNull()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardCardRuleSettingsAsync(It.IsAny<WorkContext>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var adapter = BuildAdapter(workClient);
        var result = await adapter.GetCardRuleSettingsAsync("Proj", "team-1", "Stories", CancellationToken.None);

        Assert.IsNull(result, "Null returned on exception (graceful degradation)");
    }

    // ---------------------------------------------------------------------------
    // (c) GetBacklogsAsync — maps ADO BacklogLevelConfiguration
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBacklogsAsync_ReturnsMappedBacklogs()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBacklogsAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Adob.BacklogLevelConfiguration
                {
                    Name = "Stories",
                    Id = "Microsoft.RequirementCategory",
                    Type = Adob.BacklogType.Requirement,
                    Rank = 2,
                },
            ]);

        var adapter = BuildAdapter(workClient);
        var backlogs = new List<BacklogMetadata>();
        await foreach (var b in adapter.GetBacklogsAsync("Proj", "team-1", CancellationToken.None))
            backlogs.Add(b);

        Assert.AreEqual(1, backlogs.Count);
        Assert.AreEqual("Stories", backlogs[0].Name);
        Assert.AreEqual("Microsoft.RequirementCategory", backlogs[0].WitCategory);
        Assert.AreEqual(2, backlogs[0].Rank);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBacklogsAsync_WhenFails_YieldsNone()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBacklogsAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var adapter = BuildAdapter(workClient);
        var backlogs = new List<BacklogMetadata>();
        await foreach (var b in adapter.GetBacklogsAsync("Proj", "team-1", CancellationToken.None))
            backlogs.Add(b);

        Assert.AreEqual(0, backlogs.Count, "Graceful empty when GetBacklogsAsync throws");
    }

    // ---------------------------------------------------------------------------
    // (d) GetBoardColumnsAsync — reads target columns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBoardColumnsAsync_ReturnsMappedColumns()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardColumnsAsync(It.IsAny<WorkContext>(), "Stories", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Adob.BoardColumn { Name = "New", ColumnType = Adob.BoardColumnType.Incoming },
                new Adob.BoardColumn { Name = "Done", ColumnType = Adob.BoardColumnType.Outgoing },
            ]);

        var adapter = BuildAdapter(workClient);
        var cols = await adapter.GetBoardColumnsAsync("Proj", "team-1", "Stories", CancellationToken.None);

        Assert.AreEqual(2, cols.Count);
        Assert.AreEqual("New", cols[0].Name);
        Assert.AreEqual(AbcBoardColumnType.Incoming, cols[0].ColumnType);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetBoardColumnsAsync_WhenFails_ReturnsEmpty()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetBoardColumnsAsync(It.IsAny<WorkContext>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var adapter = BuildAdapter(workClient);
        var cols = await adapter.GetBoardColumnsAsync("Proj", "team-1", "Stories", CancellationToken.None);

        Assert.AreEqual(0, cols.Count, "Empty returned on exception");
    }

    // ---------------------------------------------------------------------------
    // (e) UpdateBoardColumnsAsync — calls ADO API with mapped columns
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task UpdateBoardColumnsAsync_CallsApiWithMappedColumns()
    {
        var workClient = BuildWorkClient();
        List<Adob.BoardColumn>? captured = null;
        workClient
            .Setup(c => c.UpdateBoardColumnsAsync(
                It.IsAny<IList<Adob.BoardColumn>>(),
                It.IsAny<WorkContext>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Adob.BoardColumn>, WorkContext, string, object?, CancellationToken>(
                (cols, _, _, _, _) => captured = new List<Adob.BoardColumn>(cols))
            .ReturnsAsync([]);

        var adapter = BuildAdapter(workClient);
        IReadOnlyList<AbcBoardColumn> columns =
        [
            new("New", AbcBoardColumnType.Incoming, null, false, null, []),
            new("Done", AbcBoardColumnType.Outgoing, null, false, null, []),
        ];

        await adapter.UpdateBoardColumnsAsync("Proj", "team-1", "Stories", columns, CancellationToken.None);

        Assert.IsNotNull(captured, "UpdateBoardColumnsAsync was called");
        Assert.AreEqual(2, captured!.Count);
        Assert.AreEqual("New", captured[0].Name);
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// EC-M4 / ADR-0024: the board-config merge/validation engine is a canonical seam
/// (<see cref="IBoardConfigMergeTool"/>). These tests pin the engine behaviour that was
/// previously private to BoardConfigTeamExtension.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public sealed class BoardConfigMergeToolTests
{
    private static BoardColumn Column(string name, params (string Wit, string State)[] mappings)
        => new(name, BoardColumnType.InProgress, null, false, null,
            mappings.Select(m => new BoardColumnStateMapping(m.Wit, m.State)).ToList());

    [TestMethod]
    public void ContractIsCanonical_InterfaceLivesInAbstractionsAgent()
    {
        Assert.AreEqual(
            "DevOpsMigrationPlatform.Abstractions.Agent",
            typeof(IBoardConfigMergeTool).Assembly.GetName().Name,
            "IBoardConfigMergeTool must be a canonical Abstractions.Agent seam (EC-M4).");
        Assert.IsInstanceOfType<IBoardConfigMergeTool>(new BoardConfigMergeTool());
    }

    [TestMethod]
    public void BuildValidStatesMap_CollectsStatesPerWorkItemType_CaseInsensitive()
    {
        var tool = new BoardConfigMergeTool();
        var map = tool.BuildValidStatesMap(new[]
        {
            Column("Doing", ("Bug", "Active"), ("Bug", "Resolved")),
            Column("Done", ("bug", "Closed"), ("Task", "Done")),
        });

        Assert.IsTrue(map["BUG"].Contains("active"));
        Assert.IsTrue(map["Bug"].Contains("Closed"));
        Assert.IsTrue(map["Task"].Contains("Done"));
        Assert.AreEqual(2, map.Count);
    }

    [TestMethod]
    public void FilterInvalidStateMappings_OmitsUnknownStates_AndReportsThem()
    {
        var tool = new BoardConfigMergeTool();
        var validStates = tool.BuildValidStatesMap(new[] { Column("Target", ("Bug", "Active")) });

        var result = tool.FilterInvalidStateMappings(
            new[] { Column("Doing", ("Bug", "Active"), ("Bug", "Missing")) },
            validStates);

        Assert.AreEqual(1, result.Columns[0].StateMappings.Count);
        Assert.AreEqual("Active", result.Columns[0].StateMappings[0].State);
        Assert.AreEqual(1, result.OmittedMappings.Count);
        Assert.AreEqual(new OmittedStateMapping("Doing", "Bug", "Missing"), result.OmittedMappings[0]);
    }

    [TestMethod]
    public void FilterInvalidStateMappings_UnknownWorkItemType_IsDroppedWithoutOmissionReport()
    {
        // Parity with the pre-extraction extension behaviour: mappings for WITs absent
        // from the target map are dropped from the column but not reported as omissions.
        var tool = new BoardConfigMergeTool();
        var validStates = tool.BuildValidStatesMap(new[] { Column("Target", ("Bug", "Active")) });

        var result = tool.FilterInvalidStateMappings(
            new[] { Column("Doing", ("Epic", "New")) },
            validStates);

        Assert.AreEqual(0, result.Columns[0].StateMappings.Count);
        Assert.AreEqual(0, result.OmittedMappings.Count);
    }

    [TestMethod]
    public void FilterInvalidStateMappings_EmptyValidStates_ReturnsInputUnchanged()
    {
        var tool = new BoardConfigMergeTool();
        var columns = new[] { Column("Doing", ("Bug", "Anything")) };

        var result = tool.FilterInvalidStateMappings(columns, tool.BuildValidStatesMap(null));

        Assert.AreSame(columns[0], result.Columns[0]);
        Assert.AreEqual(0, result.OmittedMappings.Count);
    }

    [TestMethod]
    public void MergeByName_PackageItemsWin_TargetOnlyItemsAppended()
    {
        var tool = new BoardConfigMergeTool();
        var packageLanes = new[] { new BoardSwimLane("1", "Expedite") };
        var targetLanes = new[] { new BoardSwimLane("2", "EXPEDITE"), new BoardSwimLane("3", "Standard") };

        var merged = tool.MergeByName(packageLanes, targetLanes, l => l.Name);

        Assert.AreEqual(2, merged.Count);
        Assert.AreEqual("1", merged[0].Id, "Package item must win on case-insensitive key collision.");
        Assert.AreEqual("Standard", merged[1].Name, "Target-only item must be appended.");
    }

    [TestMethod]
    public void MergeByName_EmptyInputs_HandleGracefully()
    {
        var tool = new BoardConfigMergeTool();
        Assert.AreEqual(0, tool.MergeByName<BoardSwimLane>(null, null, l => l.Name).Count);
        Assert.AreEqual(1, tool.MergeByName(new[] { new BoardSwimLane("1", "A") }, null, l => l.Name).Count);
        Assert.AreEqual(1, tool.MergeByName(null, new[] { new BoardSwimLane("1", "A") }, l => l.Name).Count);
    }
}

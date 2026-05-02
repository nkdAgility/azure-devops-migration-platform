// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class MapValueTransformTests
{
    private static FieldTransformContext MakeContext(string workItemType = "Bug")
        => new FieldTransformContext(1, 0, workItemType, FieldTransformPhase.Import);

    private static MapValueTransform BuildTransform(
        string field,
        IReadOnlyDictionary<string, string> valueMap,
        IReadOnlyList<string>? applyTo = null,
        string groupName = "TestGroup",
        string name = "TestTransform")
        => new MapValueTransform(
            name,
            groupName,
            field,
            valueMap,
            applyTo,
            NullLogger<MapValueTransform>.Instance);

    private static IReadOnlyDictionary<string, string> TwoEntryMap()
        => new Dictionary<string, string>
        {
            { "Active", "In Progress" },
            { "Resolved", "Done" }
        };

    [TestMethod]
    public void Apply_WhenValueFoundInMap_ReturnsReplacedValue()
    {
        var transform = BuildTransform("System.State", TwoEntryMap());
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("In Progress", result.Fields["System.State"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("Active", result.Actions[0].OldValue);
        Assert.AreEqual("In Progress", result.Actions[0].NewValue);
        Assert.AreEqual("System.State", result.Actions[0].Field);
        Assert.AreEqual("MapValue", result.Actions[0].TransformType);
    }

    [TestMethod]
    public void Apply_WhenValueNotFoundInMap_PreservesOriginalAndLogsWarning()
    {
        var transform = BuildTransform("System.State", TwoEntryMap());
        var fields = new Dictionary<string, object?> { ["System.State"] = "New" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("New", result.Fields["System.State"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsFalse(result.Actions[0].Modified);
        Assert.AreEqual("New", result.Actions[0].OldValue);
        Assert.AreEqual("New", result.Actions[0].NewValue);
    }

    [TestMethod]
    public void Apply_WhenFieldNotPresent_ReturnsFieldsUnchanged()
    {
        var transform = BuildTransform("System.State", TwoEntryMap());
        var fields = new Dictionary<string, object?> { ["System.Title"] = "Some title" };

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.State"));
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Apply_WhenValueIsNull_PreservesNullAndLogsWarning()
    {
        var transform = BuildTransform("System.State", TwoEntryMap());
        var fields = new Dictionary<string, object?> { ["System.State"] = null };

        // null maps to empty string; empty string is not in the map → preserve + warn
        var result = transform.Apply(fields, MakeContext());

        Assert.IsNull(result.Fields["System.State"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsFalse(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WithEmptyValueMap_PreservesOriginalAndLogsWarning()
    {
        var transform = BuildTransform(
            "System.State",
            new Dictionary<string, string>());
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Active", result.Fields["System.State"]);
        Assert.IsFalse(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WithApplyToFilter_SkipsNonMatchingWorkItemType()
    {
        var transform = BuildTransform(
            "System.State",
            TwoEntryMap(),
            applyTo: new List<string> { "Bug" });
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext(workItemType: "Task"));

        Assert.AreEqual("Active", result.Fields["System.State"]);
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Apply_WithApplyToFilter_ProcessesMatchingWorkItemType()
    {
        var transform = BuildTransform(
            "System.State",
            TwoEntryMap(),
            applyTo: new List<string> { "Bug" });
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext(workItemType: "Bug"));

        Assert.AreEqual("In Progress", result.Fields["System.State"]);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_GroupNameIsRecordedInAction()
    {
        var transform = BuildTransform("System.State", TwoEntryMap(), groupName: "MyGroup");
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("MyGroup", result.Actions[0].GroupName);
    }
}

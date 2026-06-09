// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class CopyFieldTransformTests
{
    private static FieldTransformContext MakeContext(string workItemType = "Bug")
        => new FieldTransformContext(1, 0, workItemType, FieldTransformPhase.Import);

    private static CopyFieldTransform BuildTransform(
        string sourceField,
        string targetField,
        string? defaultValue = null,
        string groupName = "TestGroup",
        string name = "TestTransform")
        => new CopyFieldTransform(name, groupName, sourceField, targetField, defaultValue);

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Apply_WhenSourceFieldExists_CopiesValueToTarget()
    {
        var transform = BuildTransform("Custom.OldField", "Custom.NewField");
        var fields = new Dictionary<string, object?> { ["Custom.OldField"] = "some value" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("some value", result.Fields["Custom.NewField"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("Custom.NewField", result.Actions[0].Field);
        Assert.AreEqual("some value", result.Actions[0].NewValue);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Apply_WhenSourceFieldAbsent_UsesDefaultValue()
    {
        var transform = BuildTransform("Custom.OldField", "Custom.NewField", defaultValue: "N/A");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("N/A", result.Fields["Custom.NewField"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("N/A", result.Actions[0].NewValue);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Apply_WhenSourceFieldAbsentAndNoDefault_LeavesTargetUnchanged()
    {
        var transform = BuildTransform("Custom.OldField", "Custom.NewField");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("Custom.NewField"));
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Apply_WhenSourceFieldIsEmptyString_CopiesEmptyString_NotDefault()
    {
        var transform = BuildTransform("Custom.OldField", "Custom.NewField", defaultValue: "N/A");
        var fields = new Dictionary<string, object?> { ["Custom.OldField"] = "" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("", result.Fields["Custom.NewField"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("", result.Actions[0].NewValue);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Apply_WhenTargetFieldExists_OverwritesExistingValue()
    {
        var transform = BuildTransform("Custom.Source", "Custom.Target");
        var fields = new Dictionary<string, object?>
        {
            ["Custom.Source"] = "new value",
            ["Custom.Target"] = "old value"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("new value", result.Fields["Custom.Target"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("old value", result.Actions[0].OldValue);
        Assert.AreEqual("new value", result.Actions[0].NewValue);
    }
}



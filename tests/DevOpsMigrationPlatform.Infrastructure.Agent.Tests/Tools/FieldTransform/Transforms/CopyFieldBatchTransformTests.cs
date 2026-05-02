// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class CopyFieldBatchTransformTests
{
    private static FieldTransformContext MakeContext(string workItemType = "Bug")
        => new FieldTransformContext(1, 0, workItemType, FieldTransformPhase.Import);

    private static CopyFieldBatchTransform BuildTransform(
        IReadOnlyDictionary<string, string> fieldMappings,
        string groupName = "TestGroup",
        string name = "TestBatchTransform")
        => new CopyFieldBatchTransform(name, groupName, fieldMappings);

    [TestMethod]
    public void Apply_WhenMultipleMappings_AllFieldsCopied()
    {
        var mappings = new Dictionary<string, string>
        {
            { "Custom.Field1", "Custom.New1" },
            { "Custom.Field2", "Custom.New2" }
        };
        var transform = BuildTransform(mappings);
        var fields = new Dictionary<string, object?>
        {
            ["Custom.Field1"] = "value1",
            ["Custom.Field2"] = "value2"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("value1", result.Fields["Custom.New1"]);
        Assert.AreEqual("value2", result.Fields["Custom.New2"]);
        Assert.AreEqual(2, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.IsTrue(result.Actions[1].Modified);
    }

    [TestMethod]
    public void Apply_WhenSomeSourceFieldsAbsent_SkipsAbsentFields()
    {
        var mappings = new Dictionary<string, string>
        {
            { "Custom.Present", "Custom.Target1" },
            { "Custom.Absent",  "Custom.Target2" }
        };
        var transform = BuildTransform(mappings);
        var fields = new Dictionary<string, object?> { ["Custom.Present"] = "exists" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("exists", result.Fields["Custom.Target1"]);
        Assert.IsFalse(result.Fields.ContainsKey("Custom.Target2"));
        Assert.AreEqual(1, result.Actions.Count);
        Assert.AreEqual("Custom.Target1", result.Actions[0].Field);
    }

    [TestMethod]
    public void Apply_WithEmptyFieldMappings_ReturnsInputUnchanged()
    {
        var transform = BuildTransform(new Dictionary<string, string>());
        var fields = new Dictionary<string, object?> { ["Custom.SomeField"] = "unchanged" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("unchanged", result.Fields["Custom.SomeField"]);
        Assert.AreEqual(0, result.Actions.Count);
    }
}

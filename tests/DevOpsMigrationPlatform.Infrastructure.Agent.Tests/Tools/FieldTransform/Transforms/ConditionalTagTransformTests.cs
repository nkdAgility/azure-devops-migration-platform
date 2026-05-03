// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class ConditionalTagTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static ConditionalTagTransform Build(string conditionField, string pattern, string tag)
        => new ConditionalTagTransform("TestTransform", "TestGroup", conditionField, pattern, tag);

    [TestMethod]
    public void Apply_WhenFieldMatchesPattern_AddsTag()
    {
        var transform = Build("System.State", "Resolved|Done|Closed", "Closed");
        var fields = new Dictionary<string, object?> { ["System.State"] = "Resolved" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Closed", result.Fields["System.Tags"]);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenFieldDoesNotMatchPattern_DoesNotAddTag()
    {
        var transform = Build("System.State", "Resolved|Done", "Closed");
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.Tags"));
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Apply_WhenConditionFieldAbsent_ReturnsInputUnchanged()
    {
        var transform = Build("System.State", "Resolved", "Closed");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.Tags"));
        Assert.AreEqual(0, result.Actions.Count);
    }
}

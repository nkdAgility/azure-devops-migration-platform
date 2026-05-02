// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class ExcludeFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static ExcludeFieldTransform Build(string field)
        => new ExcludeFieldTransform("TestTransform", "TestGroup", field);

    [TestMethod]
    public void Apply_WhenFieldExists_RemovesFieldFromDictionary()
    {
        var transform = Build("Custom.InternalOnly");
        var fields = new Dictionary<string, object?>
        {
            ["Custom.InternalOnly"] = "secret",
            ["System.Title"] = "My Bug"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("Custom.InternalOnly"),
            "Excluded field must not be present in result.");
        Assert.IsTrue(result.Fields.ContainsKey("System.Title"),
            "Other fields must be preserved.");
    }

    [TestMethod]
    public void Apply_WhenFieldAbsent_ReturnsInputUnchanged()
    {
        var transform = Build("Custom.Missing");
        var fields = new Dictionary<string, object?> { ["System.Title"] = "My Bug" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual(1, result.Fields.Count);
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Apply_RecordsActionWithModifiedFlag()
    {
        var transform = Build("Custom.InternalOnly");
        var fields = new Dictionary<string, object?> { ["Custom.InternalOnly"] = "secret" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("Custom.InternalOnly", result.Actions[0].Field);
        Assert.AreEqual("secret", result.Actions[0].OldValue);
        Assert.IsNull(result.Actions[0].NewValue);
    }
}

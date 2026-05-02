// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class FieldToTagTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static FieldToTagTransform Build(string sourceField)
        => new FieldToTagTransform("TestTransform", "TestGroup", sourceField);

    [TestMethod]
    public void Apply_WhenSourceFieldExists_AppendsValueAsTag()
    {
        var transform = Build("Custom.Priority");
        var fields = new Dictionary<string, object?> { ["Custom.Priority"] = "High" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("High", result.Fields["System.Tags"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenTagsAlreadyExist_AppendsSeparated()
    {
        var transform = Build("Custom.Priority");
        var fields = new Dictionary<string, object?>
        {
            ["Custom.Priority"] = "High",
            ["System.Tags"] = "Existing"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Existing; High", result.Fields["System.Tags"]);
    }

    [TestMethod]
    public void Apply_WhenSourceFieldAbsent_ReturnsInputUnchanged()
    {
        var transform = Build("Custom.Missing");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.Tags"));
        Assert.AreEqual(0, result.Actions.Count);
    }
}

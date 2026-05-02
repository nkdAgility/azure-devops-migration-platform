// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class MergeFieldsTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static MergeFieldsTransform Build(
        IReadOnlyList<string> sourceFields,
        string targetField,
        string formatString)
        => new MergeFieldsTransform("TestTransform", "TestGroup", sourceFields, targetField, formatString);

    [TestMethod]
    public void Apply_BothFieldsPresent_MergesWithFormatString()
    {
        var transform = Build(
            new List<string> { "Custom.FirstName", "Custom.LastName" },
            "Custom.FullName",
            "{0} {1}");

        var fields = new Dictionary<string, object?>
        {
            ["Custom.FirstName"] = "John",
            ["Custom.LastName"] = "Doe"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("John Doe", result.Fields["Custom.FullName"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_AbsentFieldTreatedAsEmptyString()
    {
        var transform = Build(
            new List<string> { "Custom.FirstName", "Custom.LastName" },
            "Custom.FullName",
            "{0} {1}");

        var fields = new Dictionary<string, object?> { ["Custom.FirstName"] = "John" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("John ", result.Fields["Custom.FullName"]);
    }

    [TestMethod]
    public void Apply_WithSingleSourceField_Works()
    {
        var transform = Build(
            new List<string> { "Custom.FirstName" },
            "Custom.FullName",
            "Hello, {0}!");

        var fields = new Dictionary<string, object?> { ["Custom.FirstName"] = "Jane" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Hello, Jane!", result.Fields["Custom.FullName"]);
    }
}

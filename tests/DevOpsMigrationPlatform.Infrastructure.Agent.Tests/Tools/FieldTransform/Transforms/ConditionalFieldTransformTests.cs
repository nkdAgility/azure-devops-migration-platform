// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class ConditionalFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static ConditionalFieldTransform Build(
        string conditionField,
        string condition,
        string targetField,
        string? trueValue,
        string? falseValue)
        => new ConditionalFieldTransform(
            "TestTransform", "TestGroup",
            conditionField, condition, targetField,
            trueValue, falseValue,
            NullLogger<ConditionalFieldTransform>.Instance);

    [TestMethod]
    public void Apply_WhenConditionMatches_SetsTrueValue()
    {
        var transform = Build("System.State", "^Active$", "Custom.IsActive", "Yes", "No");
        var fields = new Dictionary<string, object?> { ["System.State"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Yes", result.Fields["Custom.IsActive"]);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenConditionDoesNotMatch_SetsFalseValue()
    {
        var transform = Build("System.State", "^Active$", "Custom.IsActive", "Yes", "No");
        var fields = new Dictionary<string, object?> { ["System.State"] = "Closed" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("No", result.Fields["Custom.IsActive"]);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenSourceFieldAbsent_SetsFalseValue()
    {
        var transform = Build("System.State", "^Active$", "Custom.IsActive", "Yes", "No");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("No", result.Fields["Custom.IsActive"]);
    }
}

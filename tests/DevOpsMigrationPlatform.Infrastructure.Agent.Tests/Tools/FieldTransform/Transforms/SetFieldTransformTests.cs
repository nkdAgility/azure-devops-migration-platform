// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class SetFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static SetFieldTransform Build(string field, string? value)
        => new SetFieldTransform("TestTransform", "TestGroup", field, value);

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_SetsFieldToLiteralValue()
    {
        var transform = Build("Custom.MigratedBy", "migration-platform");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("migration-platform", result.Fields["Custom.MigratedBy"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_SubstitutesTimestampVariable()
    {
        var transform = Build("Custom.Stamp", "${migration.timestamp}");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        var value = result.Fields["Custom.Stamp"]?.ToString();
        Assert.IsNotNull(value);
        Assert.IsTrue(System.DateTimeOffset.TryParseExact(
            value,
            "o",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out _),
            $"Value '{value}' must be a valid ISO-8601 timestamp.");
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_OverwritesExistingValue()
    {
        var transform = Build("Custom.Status", "Done");
        var fields = new Dictionary<string, object?> { ["Custom.Status"] = "Active" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Done", result.Fields["Custom.Status"]);
        Assert.AreEqual("Active", result.Actions[0].OldValue);
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_WhenFieldAbsent_AddsNewField()
    {
        var transform = Build("Custom.New", "value");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsTrue(result.Fields.ContainsKey("Custom.New"));
        Assert.AreEqual("value", result.Fields["Custom.New"]);
        Assert.IsNull(result.Actions[0].OldValue);
    }
}


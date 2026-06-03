// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class ClearFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static ClearFieldTransform Build(string field)
        => new ClearFieldTransform("TestTransform", "TestGroup", field);

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_WhenFieldExists_SetsValueToNull()
    {
        var transform = Build("Custom.Notes");
        var fields = new Dictionary<string, object?> { ["Custom.Notes"] = "some notes" };

        var result = transform.Apply(fields, MakeContext());

        Assert.IsTrue(result.Fields.ContainsKey("Custom.Notes"),
            "Field key must still be present.");
        Assert.IsNull(result.Fields["Custom.Notes"],
            "Field value must be null after clear.");
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_WhenFieldAbsent_SetsFieldToNull()
    {
        var transform = Build("Custom.Notes");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsTrue(result.Fields.ContainsKey("Custom.Notes"),
            "Field must be added with null value.");
        Assert.IsNull(result.Fields["Custom.Notes"]);
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_RecordsActionWithModifiedFlag()
    {
        var transform = Build("Custom.Notes");
        var fields = new Dictionary<string, object?> { ["Custom.Notes"] = "some notes" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
        Assert.AreEqual("Custom.Notes", result.Actions[0].Field);
        Assert.AreEqual("some notes", result.Actions[0].OldValue);
        Assert.IsNull(result.Actions[0].NewValue);
    }
}


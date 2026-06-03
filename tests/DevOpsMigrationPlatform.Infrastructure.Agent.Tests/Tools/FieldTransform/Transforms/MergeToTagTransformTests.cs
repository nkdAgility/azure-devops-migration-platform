// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class MergeToTagTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_DeduplicatesTagsCaseInsensitively()
    {
        var transform = new MergeToTagTransform(
            "TestTransform", "TestGroup",
            new List<string> { "System.Tags" });

        var fields = new Dictionary<string, object?>
        {
            ["System.Tags"] = "High; high; MEDIUM"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("High; MEDIUM", result.Fields["System.Tags"]);
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_MergesMultipleSourceFields()
    {
        var transform = new MergeToTagTransform(
            "TestTransform", "TestGroup",
            new List<string> { "Custom.Tag1", "Custom.Tag2" });

        var fields = new Dictionary<string, object?>
        {
            ["Custom.Tag1"] = "Alpha",
            ["Custom.Tag2"] = "Beta"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Alpha; Beta", result.Fields["System.Tags"]);
    }

    [TestCategory("UnitTest")]

    [TestMethod]
    public void Apply_WhenSourceFieldAbsent_SkipsSilently()
    {
        var transform = new MergeToTagTransform(
            "TestTransform", "TestGroup",
            new List<string> { "Custom.Missing" });

        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual(string.Empty, result.Fields["System.Tags"]);
        Assert.AreEqual(1, result.Actions.Count);
    }
}


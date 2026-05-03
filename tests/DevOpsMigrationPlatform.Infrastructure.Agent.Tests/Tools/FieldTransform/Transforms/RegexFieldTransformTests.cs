// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class RegexFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static RegexFieldTransform Build(string field, string pattern, string replacement)
        => new RegexFieldTransform(
            "TestTransform", "TestGroup", field, pattern, replacement,
            NullLogger<RegexFieldTransform>.Instance);

    [TestMethod]
    public void Apply_WhenPatternMatches_ReplacesContent()
    {
        var transform = Build("System.Title", @"\[OLD\] ", string.Empty);
        var fields = new Dictionary<string, object?> { ["System.Title"] = "[OLD] Implement login page" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Implement login page", result.Fields["System.Title"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenPatternDoesNotMatch_LeavesFieldUnchanged()
    {
        var transform = Build("System.Title", @"\[OLD\] ", string.Empty);
        var fields = new Dictionary<string, object?> { ["System.Title"] = "Implement login page" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Implement login page", result.Fields["System.Title"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsFalse(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenFieldAbsent_ReturnsUnchanged()
    {
        var transform = Build("System.Title", @"\[OLD\] ", string.Empty);
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.Title"));
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Constructor_WithInvalidPattern_ThrowsArgumentException()
    {
        var ex = Assert.ThrowsExactly<System.ArgumentException>(
            () => Build("System.Title", "[invalid(", string.Empty));

        StringAssert.Contains(ex.Message, "invalid pattern");
    }

    [TestMethod]
    public void Apply_ReplacesAllMatchesInValue()
    {
        // Regex.Replace replaces all occurrences by default
        var transform = Build("System.Title", @"\[OLD\] ", string.Empty);
        var fields = new Dictionary<string, object?> { ["System.Title"] = "[OLD] [OLD] Duplicate" };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("Duplicate", result.Fields["System.Title"]);
    }
}

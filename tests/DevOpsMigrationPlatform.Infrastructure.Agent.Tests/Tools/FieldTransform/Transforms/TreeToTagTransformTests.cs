using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class TreeToTagTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static TreeToTagTransform Build(string field)
        => new TreeToTagTransform("TestTransform", "TestGroup", field);

    [TestMethod]
    public void Apply_SplitsPathSegmentsIntoTags()
    {
        var transform = Build("System.AreaPath");
        var fields = new Dictionary<string, object?>
        {
            ["System.AreaPath"] = @"Project\Team\Component"
        };

        var result = transform.Apply(fields, MakeContext());

        var tags = result.Fields["System.Tags"]?.ToString() ?? string.Empty;
        Assert.IsTrue(tags.Contains("Project"), $"Tags must contain 'Project' but was '{tags}'.");
        Assert.IsTrue(tags.Contains("Team"), $"Tags must contain 'Team' but was '{tags}'.");
        Assert.IsTrue(tags.Contains("Component"), $"Tags must contain 'Component' but was '{tags}'.");
    }

    [TestMethod]
    public void Apply_WhenFieldAbsent_ReturnsInputUnchanged()
    {
        var transform = Build("System.AreaPath");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("System.Tags"));
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Apply_EmptySegmentsAreFiltered()
    {
        var transform = Build("System.AreaPath");
        var fields = new Dictionary<string, object?>
        {
            ["System.AreaPath"] = @"Project\\Team"  // double-backslash produces empty segment
        };

        var result = transform.Apply(fields, MakeContext());

        var tags = result.Fields["System.Tags"]?.ToString() ?? string.Empty;
        Assert.IsFalse(tags.Contains("; ;"), "Empty segments must not produce empty tags.");
    }
}

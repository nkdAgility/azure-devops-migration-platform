// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class FieldTransformPipelineTests
{
    private static FieldTransformContext MakeContext(string workItemType = "Bug")
        => new FieldTransformContext(1, 0, workItemType, FieldTransformPhase.Import);

    private static IReadOnlyDictionary<string, object?> EmptyFields()
        => new Dictionary<string, object?>();

    private static FieldTransformPipeline BuildPipeline(
        IReadOnlyList<(FieldTransformGroupOptions Group, IReadOnlyList<(FieldTransformRuleOptions Rule, IFieldTransform Transform)> Transforms)> groups)
        => new FieldTransformPipeline(groups, NullLogger<FieldTransformPipeline>.Instance);

    [TestMethod]
    public void Execute_WithEmptyPipeline_ReturnsInputUnchanged()
    {
        var pipeline = BuildPipeline(
            new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>());

        var input = new Dictionary<string, object?> { ["System.Title"] = "Hello" };
        var result = pipeline.Execute(input, MakeContext());

        Assert.AreEqual(1, ((Dictionary<string, object?>)result.Fields).Count);
        Assert.AreEqual("Hello", result.Fields["System.Title"]);
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void Execute_WithDisabledGroup_SkipsGroup()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        // No Setup calls — MockBehavior.Strict will fail if Apply is ever called

        var group = new FieldTransformGroupOptions { Enabled = false };
        var rule = new FieldTransformRuleOptions { Type = "Mock", Enabled = true };

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>
        {
            (group, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, mockTransform.Object) })
        };

        var pipeline = BuildPipeline(groups);
        var result = pipeline.Execute(EmptyFields(), MakeContext());

        Assert.AreEqual(0, result.Actions.Count);
        mockTransform.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Execute_WithApplyToFilter_SkipsNonMatchingType()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        // Apply must NOT be called for non-matching type

        var group = new FieldTransformGroupOptions
        {
            Enabled = true,
            ApplyTo = new[] { "Task" }
        };
        var rule = new FieldTransformRuleOptions { Type = "Mock", Enabled = true };

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>
        {
            (group, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, mockTransform.Object) })
        };

        var pipeline = BuildPipeline(groups);
        var result = pipeline.Execute(EmptyFields(), MakeContext("Bug"));

        Assert.AreEqual(0, result.Actions.Count);
        mockTransform.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Execute_GroupsExecuteInOrder_OutputFeedsNextTransform()
    {
        // First transform sets System.Title to "First"
        // Second transform should receive "First" and set it to "Second"
        object? capturedValue = null;

        var firstTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        firstTransform.SetupGet(t => t.Type).Returns("Mock");
        firstTransform.SetupGet(t => t.Name).Returns("first");
        firstTransform.Setup(t => t.Apply(It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<FieldTransformContext>()))
            .Returns((IReadOnlyDictionary<string, object?> f, FieldTransformContext _) =>
            {
                var d = new Dictionary<string, object?>(f) { ["System.Title"] = "First" };
                return new FieldTransformResult(d, new[]
                {
                    new FieldTransformAction("G1", "first", "Mock", "System.Title", true, null, "First")
                });
            });

        var secondTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        secondTransform.SetupGet(t => t.Type).Returns("Mock");
        secondTransform.SetupGet(t => t.Name).Returns("second");
        secondTransform.Setup(t => t.Apply(It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<FieldTransformContext>()))
            .Returns((IReadOnlyDictionary<string, object?> f, FieldTransformContext _) =>
            {
                capturedValue = f.TryGetValue("System.Title", out var v) ? v : null;
                var d = new Dictionary<string, object?>(f) { ["System.Title"] = "Second" };
                return new FieldTransformResult(d, new[]
                {
                    new FieldTransformAction("G2", "second", "Mock", "System.Title", true, "First", "Second")
                });
            });

        var group1 = new FieldTransformGroupOptions { Enabled = true };
        var group2 = new FieldTransformGroupOptions { Enabled = true };
        var rule = new FieldTransformRuleOptions { Type = "Mock", Enabled = true };

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>
        {
            (group1, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, firstTransform.Object) }),
            (group2, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, secondTransform.Object) })
        };

        var pipeline = BuildPipeline(groups);
        var result = pipeline.Execute(new Dictionary<string, object?> { ["System.Title"] = "Original" }, MakeContext());

        Assert.AreEqual("First", capturedValue);
        Assert.AreEqual("Second", result.Fields["System.Title"]);
        Assert.AreEqual(2, result.Actions.Count);
    }

    [TestMethod]
    public void Execute_WithTagTransform_DeduplicatesTags()
    {
        var tagTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        tagTransform.SetupGet(t => t.Type).Returns("Mock");
        tagTransform.SetupGet(t => t.Name).Returns("tag-transform");
        tagTransform.Setup(t => t.Apply(It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<FieldTransformContext>()))
            .Returns((IReadOnlyDictionary<string, object?> f, FieldTransformContext _) =>
            {
                var d = new Dictionary<string, object?>(f) { ["System.Tags"] = "Bug; Feature; Bug" };
                return new FieldTransformResult(d, new[]
                {
                    new FieldTransformAction("G1", "tag-transform", "Mock", "System.Tags", true, null, "Bug; Feature; Bug")
                });
            });

        var group = new FieldTransformGroupOptions { Enabled = true };
        var rule = new FieldTransformRuleOptions { Type = "Mock", Enabled = true };

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>
        {
            (group, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, tagTransform.Object) })
        };

        var pipeline = BuildPipeline(groups);
        var result = pipeline.Execute(EmptyFields(), MakeContext());

        Assert.AreEqual("Bug; Feature", result.Fields["System.Tags"]);
    }

    [TestMethod]
    public void Execute_WithDisabledTransform_SkipsTransform()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        // Apply must NOT be called for disabled rule

        var group = new FieldTransformGroupOptions { Enabled = true };
        var rule = new FieldTransformRuleOptions { Type = "Mock", Enabled = false };

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>
        {
            (group, new List<(FieldTransformRuleOptions, IFieldTransform)> { (rule, mockTransform.Object) })
        };

        var pipeline = BuildPipeline(groups);
        var result = pipeline.Execute(EmptyFields(), MakeContext());

        Assert.AreEqual(0, result.Actions.Count);
        mockTransform.VerifyNoOtherCalls();
    }
}

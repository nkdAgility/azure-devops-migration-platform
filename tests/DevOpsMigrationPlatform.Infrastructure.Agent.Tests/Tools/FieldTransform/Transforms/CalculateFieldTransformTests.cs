using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class CalculateFieldTransformTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static CalculateFieldTransform Build(string field, string expression, IExpressionEvaluator? evaluator = null)
        => new CalculateFieldTransform(
            "TestTransform",
            "TestGroup",
            field,
            expression,
            evaluator ?? new SimpleExpressionEvaluator(),
            NullLogger<CalculateFieldTransform>.Instance);

    [TestMethod]
    public void Apply_WithArithmeticExpression_ComputesResult()
    {
        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock
            .Setup(e => e.Evaluate("10 + 5", It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns("15");

        var transform = Build("Custom.Result", "10 + 5", evaluatorMock.Object);
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("15", result.Fields["Custom.Result"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WithFieldReferences_ResolvesAndComputes()
    {
        var transform = Build("Custom.TotalHours", "Custom.Hours * Custom.Days");
        var fields = new Dictionary<string, object?>
        {
            ["Custom.Hours"] = "8",
            ["Custom.Days"] = "5"
        };

        var result = transform.Apply(fields, MakeContext());

        Assert.AreEqual("40", result.Fields["Custom.TotalHours"]);
        Assert.IsTrue(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WithMissingFieldReference_ReturnsUnmodifiedFields()
    {
        var transform = Build("Custom.Result", "Custom.Missing * 2");
        var fields = new Dictionary<string, object?>();

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("Custom.Result"),
            "Result field must not be added when expression fails.");
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsFalse(result.Actions[0].Modified);
    }

    [TestMethod]
    public void Apply_WhenEvaluatorThrows_ReturnsUnmodifiedFields()
    {
        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock
            .Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Throws(new InvalidOperationException("Evaluation failed"));

        var transform = Build("Custom.Result", "some expression", evaluatorMock.Object);
        var fields = new Dictionary<string, object?> { ["Other.Field"] = "value" };

        var result = transform.Apply(fields, MakeContext());

        Assert.IsFalse(result.Fields.ContainsKey("Custom.Result"));
        Assert.AreEqual("value", result.Fields["Other.Field"]);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.IsFalse(result.Actions[0].Modified);
    }
}

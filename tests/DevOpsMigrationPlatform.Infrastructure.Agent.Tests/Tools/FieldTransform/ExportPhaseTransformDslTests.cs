// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class ExportPhaseTransformDslTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WithEnabledExportConfiguration_ReturnsTrueForExport()
    {
        var result = FieldTransformPhaseScenario
            .Create()
            .WithEnabledTool()
            .WithEnabledTransform()
            .Evaluate();

        result.ShouldBeEnabledForExport();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WithDisabledConfiguration_ReturnsFalseForExport()
    {
        var result = FieldTransformPhaseScenario
            .Create()
            .WithDisabledTool()
            .WithEnabledTransform()
            .Evaluate();

        result.ShouldBeDisabledForExportAndImport();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WithEnabledConfiguration_ReturnsTrueForExportAndImport()
    {
        var result = FieldTransformPhaseScenario
            .Create()
            .WithEnabledTool()
            .WithEnabledTransform()
            .Evaluate();

        result.ShouldBeEnabledForExportAndImport();
    }
}

internal sealed class FieldTransformPhaseScenario
{
    private readonly bool _toolEnabled;
    private readonly bool _enabledTransform;

    private FieldTransformPhaseScenario(bool toolEnabled, bool enabledTransform)
    {
        _toolEnabled = toolEnabled;
        _enabledTransform = enabledTransform;
    }

    public static FieldTransformPhaseScenario Create()
    {
        return new FieldTransformPhaseScenario(toolEnabled: true, enabledTransform: false);
    }

    public FieldTransformPhaseScenario WithEnabledTool()
    {
        return new FieldTransformPhaseScenario(toolEnabled: true, enabledTransform: _enabledTransform);
    }

    public FieldTransformPhaseScenario WithDisabledTool()
    {
        return new FieldTransformPhaseScenario(toolEnabled: false, enabledTransform: _enabledTransform);
    }

    public FieldTransformPhaseScenario WithEnabledTransform()
    {
        return new FieldTransformPhaseScenario(toolEnabled: _toolEnabled, enabledTransform: true);
    }

    public FieldTransformPhaseEvaluation Evaluate()
    {
        var options = BuildOptions(_toolEnabled, _enabledTransform);
        var tool = BuildTool(options);
        return new FieldTransformPhaseEvaluation(
            tool.IsEnabledForPhase(FieldTransformPhase.Export),
            tool.IsEnabledForPhase(FieldTransformPhase.Import));
    }

    private static FieldTransformOptions BuildOptions(bool toolEnabled, bool enabledTransform)
    {
        return new FieldTransformOptions
        {
            Enabled = toolEnabled,
            TransformGroups = enabledTransform
                ? [new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms =
                    [
                        new FieldTransformRuleOptions
                        {
                            Type = "Mock",
                            Enabled = true,
                        },
                    ],
                }]
                : [],
        };
    }

    private static IFieldTransformTool BuildTool(FieldTransformOptions options)
    {
        var transform = new Mock<IFieldTransform>(MockBehavior.Strict);
        transform.SetupGet(t => t.Type).Returns("Mock");
        transform.SetupGet(t => t.Name).Returns("mock");
        transform.Setup(t => t.Apply(
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<FieldTransformContext>()))
            .Returns((IReadOnlyDictionary<string, object?> f, FieldTransformContext _) =>
                new FieldTransformResult(f, System.Array.Empty<FieldTransformAction>()));

        var factory = new Mock<IFieldTransformFactory>(MockBehavior.Strict);
        factory.Setup(f => f.Create(
                It.IsAny<FieldTransformRuleOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Returns(transform.Object);

        return new FieldTransformTool(Options.Create(options), factory.Object, NullLoggerFactory.Instance);
    }
}

internal readonly record struct FieldTransformPhaseEvaluation(bool IsEnabledForExport, bool IsEnabledForImport);

internal static class FieldTransformPhaseAssertions
{
    public static void ShouldBeEnabledForExport(this FieldTransformPhaseEvaluation evaluation)
    {
        Assert.IsTrue(evaluation.IsEnabledForExport, "Expected Export phase to be enabled.");
    }

    public static void ShouldBeDisabledForExport(this FieldTransformPhaseEvaluation evaluation)
    {
        Assert.IsFalse(evaluation.IsEnabledForExport, "Expected Export phase to be disabled.");
    }

    public static void ShouldBeDisabledForExportAndImport(this FieldTransformPhaseEvaluation evaluation)
    {
        Assert.IsFalse(evaluation.IsEnabledForExport, "Expected Export phase to be disabled.");
        Assert.IsFalse(evaluation.IsEnabledForImport, "Expected Import phase to be disabled.");
    }

    public static void ShouldBeEnabledForExportAndImport(this FieldTransformPhaseEvaluation evaluation)
    {
        Assert.IsTrue(evaluation.IsEnabledForExport, "Expected Export phase to be enabled.");
        Assert.IsTrue(evaluation.IsEnabledForImport, "Expected Import phase to be enabled.");
    }
}

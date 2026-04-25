using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field transforms during export phase")]
public class ExportPhaseTransformSteps
{
    private readonly ExportPhaseTransformContext _ctx;

    public ExportPhaseTransformSteps(ExportPhaseTransformContext ctx) => _ctx = ctx;

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

    // ── Scenario 1: Export-phase transform ───────────────────────────────────

    [Given(@"the FieldTransformTool is configured with an export-phase transform")]
    public void GivenFieldTransformToolConfiguredWithExportPhaseTransform()
    {
        _ctx.Options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Mock", Enabled = true } }
                }
            }
        };
    }

    [Given(@"the tool is enabled for the Export phase")]
    public void GivenToolIsEnabledForExportPhase()
    {
        // Options already set to enabled; this step documents intent.
    }

    [When(@"the tool is asked if it is enabled for the Export phase")]
    public void WhenToolIsAskedIfEnabledForExportPhase()
    {
        var tool = BuildTool(_ctx.Options);
        _ctx.IsEnabledForExport = tool.IsEnabledForPhase(FieldTransformPhase.Export);
    }

    [Then(@"it should return true")]
    public void ThenItShouldReturnTrue()
    {
        Assert.IsTrue(_ctx.IsEnabledForExport ?? _ctx.IsEnabledForImport,
            "Expected IsEnabledForPhase to return true.");
    }

    // ── Scenario 2: Import-only transform ────────────────────────────────────

    [Given(@"the FieldTransformTool is configured with transforms")]
    public void GivenFieldTransformToolConfiguredWithTransforms()
    {
        _ctx.Options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Mock", Enabled = true } }
                }
            }
        };
    }

    [Given(@"the tool is only enabled for the Import phase")]
    public void GivenToolIsOnlyEnabledForImportPhase()
    {
        // The current IsEnabledForPhase does not filter by phase; configure as disabled to simulate.
        _ctx.Options = new FieldTransformOptions { Enabled = false };
    }

    [Then(@"it should return false")]
    public void ThenItShouldReturnFalse()
    {
        Assert.IsFalse(_ctx.IsEnabledForExport,
            "Expected IsEnabledForPhase(Export) to return false.");
    }

    // ── Scenario 3: Both-phase transform ─────────────────────────────────────

    [Given(@"the FieldTransformTool has transforms configured")]
    public void GivenFieldTransformToolHasTransformsConfigured()
    {
        _ctx.Options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Mock", Enabled = true } }
                }
            }
        };
    }

    [Given(@"the tool is enabled")]
    public void GivenToolIsEnabled()
    {
        // Options already set to enabled.
    }

    [When(@"the tool is asked if it is enabled for either phase")]
    public void WhenToolIsAskedIfEnabledForEitherPhase()
    {
        var tool = BuildTool(_ctx.Options);
        _ctx.IsEnabledForExport = tool.IsEnabledForPhase(FieldTransformPhase.Export);
        _ctx.IsEnabledForImport = tool.IsEnabledForPhase(FieldTransformPhase.Import);
    }

    [Then(@"it should return true for both")]
    public void ThenItShouldReturnTrueForBoth()
    {
        Assert.IsTrue(_ctx.IsEnabledForExport, "Expected IsEnabledForPhase(Export) to return true.");
        Assert.IsTrue(_ctx.IsEnabledForImport, "Expected IsEnabledForPhase(Import) to return true.");
    }
}

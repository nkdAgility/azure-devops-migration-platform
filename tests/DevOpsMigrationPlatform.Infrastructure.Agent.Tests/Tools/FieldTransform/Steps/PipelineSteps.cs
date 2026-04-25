using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Field transform pipeline filtering and enabled flags")]
public class PipelineSteps
{
    private readonly PipelineContext _ctx;

    public PipelineSteps(PipelineContext ctx) => _ctx = ctx;

    // Tool-level enabled false

    [Given(@"the FieldTransformTool is configured with a SetField transform on ""([^""]*)""")]
    public void GivenToolConfiguredWithSetFieldTransform(string field)
        => _ctx.AddGroup(true, new[] { new PipelineContext.RuleDef("SetField", field, "set") });

    [Given(@"the tool-level enabled flag is set to false")]
    public void GivenToolLevelEnabledFalse()
        => _ctx.ToolEnabled = false;

    [When(@"I check whether the tool is enabled for the Import phase")]
    public void WhenICheckWhetherToolIsEnabledForImport()
    {
        var tool = _ctx.BuildTool();
        _ctx.IsEnabledResult = tool.IsEnabledForPhase(FieldTransformPhase.Import);
    }

    [Then(@"the tool should report it is not enabled")]
    public void ThenToolShouldReportNotEnabled()
    {
        Assert.IsNotNull(_ctx.IsEnabledResult);
        Assert.IsFalse(_ctx.IsEnabledResult!.Value, "Expected IsEnabledForPhase to return false.");
    }

    // Group-level enabled false

    [Given(@"the FieldTransformTool has a disabled group containing a SetField transform on ""([^""]*)""")]
    public void GivenToolHasDisabledGroupWithSetField(string field)
        => _ctx.AddGroup(false, new[] { new PipelineContext.RuleDef("SetField", field, "set") });

    [Given(@"a work item with no fields")]
    public void GivenWorkItemWithNoFields()
        => _ctx.InputFields = new Dictionary<string, object?>();

    [When(@"the field transform pipeline executes via the tool")]
    public void WhenPipelineExecutesViaTool()
    {
        var tool = _ctx.BuildTool();
        var context = new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);
        _ctx.Result = tool.ApplyTransforms(_ctx.InputFields, context);
    }

    [Then(@"the field ""([^""]*)"" should not be present in the output")]
    public void ThenFieldShouldNotBePresentInOutput(string field)
    {
        Assert.IsNotNull(_ctx.Result);
        Assert.IsFalse(_ctx.Result.Fields.ContainsKey(field), $"Field '{field}' must not be present.");
    }

    // Transform-level enabled false

    [Given(@"the FieldTransformTool has a group with two SetField transforms on ""([^""]*)"" and ""([^""]*)""")]
    public void GivenToolHasGroupWithTwoSetFieldTransforms(string field1, string field2)
        => _ctx.AddGroup(true, new[]
        {
            new PipelineContext.RuleDef("SetField", field1, "set"),
            new PipelineContext.RuleDef("SetField", field2, "set")
        });

    [Given(@"the second transform is disabled")]
    public void GivenSecondTransformIsDisabled()
        => _ctx.SetLastGroupSecondRuleDisabled();

    [Then(@"the field ""([^""]*)"" should have value ""([^""]*)""")]
    public void ThenFieldShouldHaveValue(string field, string expectedValue)
    {
        Assert.IsNotNull(_ctx.Result);
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual), $"Field '{field}' not found.");
        Assert.AreEqual(expectedValue, actual?.ToString(), $"Field '{field}': expected '{expectedValue}' but was '{actual}'.");
    }

    // Identity field rejection

    [Given(@"a transform factory")]
    public void GivenATransformFactory() { }

    [When(@"I try to create a SetField transform targeting ""([^""]*)""")]
    public void WhenITryToCreateSetFieldTargetingIdentityField(string field)
    {
        try
        {
            var factory = new FieldTransformFactory();
            factory.Create(
                new Abstractions.Options.FieldTransformRuleOptions { Type = "SetField", Field = field, Value = "x", Enabled = true },
                "BDD", 1);
        }
        catch (Exception ex)
        {
            _ctx.ExceptionCaught = ex;
        }
    }

    [Then(@"the factory should throw an identity field exception")]
    public void ThenFactoryShouldThrowIdentityFieldException()
    {
        Assert.IsNotNull(_ctx.ExceptionCaught, "Expected an exception when targeting an identity field.");
        StringAssert.Contains(_ctx.ExceptionCaught.Message, "identity field",
            "Exception message must mention 'identity field'.");
    }
}

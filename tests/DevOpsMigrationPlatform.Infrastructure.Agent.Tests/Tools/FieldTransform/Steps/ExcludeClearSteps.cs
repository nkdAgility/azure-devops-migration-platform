// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field exclusion and clearing")]
public class ExcludeClearSteps
{
    private readonly ExcludeClearContext _ctx;

    public ExcludeClearSteps(ExcludeClearContext ctx) => _ctx = ctx;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a work item without field ""(.*)""")]
    public void GivenWorkItemWithoutField(string field)
        => _ctx.InputFields.Remove(field);

    [Given(@"an ExcludeField transform is configured for field ""(.*)""")]
    public void GivenExcludeFieldTransform(string field)
        => _ctx.AddExcludeFieldTransform(field);

    [Given(@"a ClearField transform is configured for field ""(.*)""")]
    public void GivenClearFieldTransform(string field)
        => _ctx.AddClearFieldTransform(field);

    [When("the field transform pipeline executes")]
    public void WhenThePipelineExecutes() => _ctx.Execute();

    [Then(@"the field ""(.*)"" should not be present in the output")]
    public void ThenFieldShouldNotBePresent(string field)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsFalse(_ctx.Result.Fields.ContainsKey(field),
            $"Field '{field}' must not be present in the output.");
    }

    [Then(@"the field ""(.*)"" should have value null")]
    public void ThenFieldShouldHaveValueNull(string field)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.ContainsKey(field),
            $"Field '{field}' must be present in the output.");
        Assert.IsNull(_ctx.Result.Fields[field],
            $"Field '{field}' must have a null value.");
    }

    [Then("the pipeline should complete without error")]
    public void ThenPipelineShouldCompleteWithoutError()
    {
        Assert.IsNull(_ctx.ExceptionCaught,
            $"Pipeline must not throw but got: {_ctx.ExceptionCaught?.Message}");
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
    }
}

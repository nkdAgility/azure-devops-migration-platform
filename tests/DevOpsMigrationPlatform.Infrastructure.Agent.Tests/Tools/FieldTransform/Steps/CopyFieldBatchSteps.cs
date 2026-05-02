// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field batch copying")]
public class CopyFieldBatchSteps
{
    private readonly CopyFieldBatchContext _ctx;

    public CopyFieldBatchSteps(CopyFieldBatchContext ctx) => _ctx = ctx;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)"" and ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithTwoFields(string field1, string value1, string field2, string value2)
    {
        _ctx.InputFields[field1] = value1;
        _ctx.InputFields[field2] = value2;
    }

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a work item without field ""([^""]*)""")]
    public void GivenWorkItemWithoutField(string field)
        => _ctx.InputFields.Remove(field);

    [Given(@"a CopyFieldBatch transform is configured with mappings ""([^""]*)"" -> ""([^""]*)"" and ""([^""]*)"" -> ""([^""]*)""")]
    public void GivenCopyFieldBatchWithTwoMappings(string src1, string tgt1, string src2, string tgt2)
    {
        _ctx.AddMapping(src1, tgt1);
        _ctx.AddMapping(src2, tgt2);
    }

    [Given(@"a CopyFieldBatch transform is configured with mapping ""([^""]*)"" -> ""([^""]*)""")]
    public void GivenCopyFieldBatchWithOneMapping(string src, string tgt)
        => _ctx.AddMapping(src, tgt);

    [When("the field transform pipeline executes")]
    public void WhenThePipelineExecutes() => _ctx.Execute();

    [Then(@"the field ""([^""]*)"" should have value ""([^""]*)""")]
    public void ThenFieldShouldHaveValue(string field, string expectedValue)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual),
            $"Field '{field}' not found in result.");
        Assert.AreEqual(expectedValue, actual?.ToString(),
            $"Field '{field}': expected '{expectedValue}' but was '{actual}'.");
    }

    [Then(@"the field ""([^""]*)"" should not be present in the output")]
    public void ThenFieldShouldNotBePresent(string field)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsFalse(_ctx.Result.Fields.ContainsKey(field),
            $"Field '{field}' must not be present in the output.");
    }
}

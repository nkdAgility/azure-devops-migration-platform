// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field merging and conditional assignment")]
public class MergeFieldsSteps
{
    private readonly MergeFieldsContext _ctx;

    public MergeFieldsSteps(MergeFieldsContext ctx) => _ctx = ctx;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)"" and ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithTwoFields(string field1, string value1, string field2, string value2)
    {
        _ctx.InputFields[field1] = value1;
        _ctx.InputFields[field2] = value2;
    }

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)"" but without field ""([^""]*)""")]
    public void GivenWorkItemWithFieldButWithout(string field1, string value1, string absentField)
    {
        _ctx.InputFields[field1] = value1;
        _ctx.InputFields.Remove(absentField);
    }

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a MergeFields transform is configured for field ""(.*)"" with source fields ""(.*)"" and format ""(.*)""")]
    public void GivenMergeFieldsTransform(string targetField, string sourceFieldsCsv, string format)
    {
        var sourceFields = new List<string>();
        foreach (var f in sourceFieldsCsv.Split(','))
        {
            var trimmed = f.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                sourceFields.Add(trimmed);
        }
        _ctx.AddMergeFieldsTransform(sourceFields, targetField, format);
    }

    [Given(@"a ConditionalField transform is configured to set ""(.*)"" to ""(.*)"" when ""(.*)"" matches ""(.*)"" else ""(.*)""")]
    public void GivenConditionalFieldTransform(
        string targetField, string trueValue, string conditionField, string condition, string falseValue)
        => _ctx.AddConditionalFieldTransform(conditionField, condition, targetField, trueValue, falseValue);

    [When("the field transform pipeline executes")]
    public void WhenThePipelineExecutes() => _ctx.Execute();

    [Then(@"the field ""(.*)"" should have value ""(.*)""")]
    public void ThenFieldShouldHaveValue(string field, string expectedValue)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual),
            $"Field '{field}' not found in result.");
        Assert.AreEqual(expectedValue, actual?.ToString(),
            $"Field '{field}': expected '{expectedValue}' but was '{actual}'.");
    }
}

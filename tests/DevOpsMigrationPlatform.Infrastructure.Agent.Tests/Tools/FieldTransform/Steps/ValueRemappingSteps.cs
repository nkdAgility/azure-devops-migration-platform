// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field value remapping")]
public class ValueRemappingSteps
{
    private readonly ValueRemappingContext _ctx;

    public ValueRemappingSteps(ValueRemappingContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given(@"a MapValue transform is configured for field ""(.*)"" with mappings ""(.*)"" -> ""(.*)"" and ""(.*)"" -> ""(.*)""")]
    public void GivenMapValueTransformWithTwoMappings(
        string field, string from1, string to1, string from2, string to2)
    {
        var map = new Dictionary<string, string>
        {
            { from1, to1 },
            { from2, to2 }
        };
        _ctx.AddTransform(field, map);
    }

    // ── Scenario 3: applyTo filter ────────────────────────────────────────────

    [Given(@"a MapValue transform is configured for field ""(.*)"" with mappings ""(.*)"" -> ""(.*)"" and applyTo filter ""(.*)""")]
    public void GivenMapValueTransformWithApplyToFilter(
        string field, string from1, string to1, string applyTo)
    {
        _ctx.Transforms.Clear();
        var map = new Dictionary<string, string> { { from1, to1 } };
        _ctx.AddTransform(field, map, applyTo: new List<string> { applyTo });
    }

    // ── Scenario 4: sequential transforms ────────────────────────────────────

    [Given(@"two MapValue transforms: first maps ""(.*)"" -> ""(.*)"", second maps ""(.*)"" -> ""(.*)"" for field ""(.*)""")]
    public void GivenTwoSequentialMapValueTransforms(
        string from1, string to1, string from2, string to2, string field)
    {
        _ctx.Transforms.Clear();
        _ctx.AddTransform(field, new Dictionary<string, string> { { from1, to1 } });
        _ctx.AddTransform(field, new Dictionary<string, string> { { from2, to2 } });
    }

    // ── Common Given steps ────────────────────────────────────────────────────

    [Given(@"a work item of type ""(.*)"" with field ""(.*)"" set to ""(.*)""")]
    public void GivenWorkItemWithField(string workItemType, string field, string value)
    {
        _ctx.WorkItemType = workItemType;
        _ctx.InputFields[field] = value;
    }

    // Scenario 3 uses "And a work item of type ..." — same binding as above.

    // ── When ─────────────────────────────────────────────────────────────────

    [When("the field transform pipeline executes")]
    public void WhenThePipelineExecutes() => _ctx.Execute();

    // ── Then ─────────────────────────────────────────────────────────────────

    [Then(@"the field ""(.*)"" should have value ""(.*)""")]
    public void ThenFieldShouldHaveValue(string field, string expectedValue)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual),
            $"Field '{field}' not found in result.");
        Assert.AreEqual(expectedValue, actual?.ToString(),
            $"Field '{field}': expected '{expectedValue}' but was '{actual}'.");
    }

    [Then(@"the field ""(.*)"" should still have value ""(.*)""")]
    public void ThenFieldShouldStillHaveValue(string field, string expectedValue)
        => ThenFieldShouldHaveValue(field, expectedValue);
}

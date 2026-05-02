// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field regex cleanup")]
public class RegexCleanupSteps
{
    private readonly RegexCleanupContext _ctx;

    public RegexCleanupSteps(RegexCleanupContext ctx) => _ctx = ctx;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a RegexField transform is configured for field ""(.*)"" with pattern ""(.*)"" and replacement ""(.*)""")]
    public void GivenRegexFieldTransform(string field, string pattern, string replacement)
        => _ctx.AddRegexFieldTransform(field, pattern, replacement);

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

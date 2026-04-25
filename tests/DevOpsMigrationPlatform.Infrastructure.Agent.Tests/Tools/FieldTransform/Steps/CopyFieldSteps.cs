using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field copying and renaming")]
public class CopyFieldSteps
{
    private readonly CopyFieldContext _ctx;

    public CopyFieldSteps(CopyFieldContext ctx) => _ctx = ctx;

    // ── Given: field presence ─────────────────────────────────────────────────

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a work item without field ""([^""]*)""")]
    public void GivenWorkItemWithoutField(string field)
        => _ctx.InputFields.Remove(field);

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)"" and ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithTwoFields(string field1, string value1, string field2, string value2)
    {
        _ctx.InputFields[field1] = value1;
        _ctx.InputFields[field2] = value2;
    }

    // ── Given: transform configuration ────────────────────────────────────────

    [Given(@"a CopyField transform is configured to copy from ""([^""]*)"" to ""([^""]*)""")]
    public void GivenCopyFieldTransform(string sourceField, string targetField)
        => _ctx.AddCopyFieldTransform(sourceField, targetField);

    [Given(@"a CopyField transform is configured to copy from ""([^""]*)"" to ""([^""]*)"" with default ""([^""]*)""")]
    public void GivenCopyFieldTransformWithDefault(string sourceField, string targetField, string defaultValue)
        => _ctx.AddCopyFieldTransform(sourceField, targetField, defaultValue);

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
}

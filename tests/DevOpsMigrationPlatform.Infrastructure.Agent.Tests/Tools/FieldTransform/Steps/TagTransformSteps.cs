using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field to tag transforms")]
public class TagTransformSteps
{
    private readonly TagTransformContext _ctx;

    public TagTransformSteps(TagTransformContext ctx) => _ctx = ctx;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a work item without field ""(.*)""")]
    public void GivenWorkItemWithoutField(string field)
        => _ctx.InputFields.Remove(field);

    [Given(@"a FieldToTag transform is configured for field ""(.*)""")]
    public void GivenFieldToTagTransform(string field)
        => _ctx.AddFieldToTagTransform(field);

    [Given(@"a ConditionalTag transform is configured to add tag ""(.*)"" when field ""(.*)"" matches ""(.*)""")]
    public void GivenConditionalTagTransform(string tag, string conditionField, string pattern)
        => _ctx.AddConditionalTagTransform(conditionField, pattern, tag);

    [Given(@"a MergeToTag transform is configured for source fields ""(.*)""")]
    public void GivenMergeToTagTransform(string sourceFieldsCsv)
    {
        var fields = new List<string>();
        foreach (var f in sourceFieldsCsv.Split(','))
        {
            var trimmed = f.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                fields.Add(trimmed);
        }
        _ctx.AddMergeToTagTransform(fields);
    }

    [Given(@"a TreeToTag transform is configured for field ""(.*)""")]
    public void GivenTreeToTagTransform(string field)
        => _ctx.AddTreeToTagTransform(field);

    [When("the field transform pipeline executes")]
    public void WhenThePipelineExecutes() => _ctx.Execute();

    [Then(@"the field ""(.*)"" should contain tag ""(.*)""")]
    public void ThenFieldShouldContainTag(string field, string expectedTag)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual),
            $"Field '{field}' not found in result.");
        var tags = actual?.ToString() ?? string.Empty;
        Assert.IsTrue(
            ContainsTag(tags, expectedTag),
            $"Field '{field}' (value='{tags}') must contain tag '{expectedTag}'.");
    }

    [Then(@"the field ""(.*)"" should not contain tag ""(.*)""")]
    public void ThenFieldShouldNotContainTag(string field, string unexpectedTag)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        if (!_ctx.Result.Fields.TryGetValue(field, out var actual))
            return; // field absent ⇒ tag definitely not present

        var tags = actual?.ToString() ?? string.Empty;
        Assert.IsFalse(
            ContainsTag(tags, unexpectedTag),
            $"Field '{field}' (value='{tags}') must NOT contain tag '{unexpectedTag}'.");
    }

    [Then(@"the field ""(.*)"" should have deduplicated tags ""(.*)""")]
    public void ThenFieldShouldHaveDeduplicatedTags(string field, string expectedTags)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        Assert.IsTrue(_ctx.Result.Fields.TryGetValue(field, out var actual),
            $"Field '{field}' not found in result.");
        Assert.AreEqual(expectedTags, actual?.ToString(),
            $"Field '{field}' deduplicated tags mismatch.");
    }

    private static bool ContainsTag(string tagString, string tag)
    {
        foreach (var part in tagString.Split(';'))
        {
            if (string.Equals(part.Trim(), tag, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

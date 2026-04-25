using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

[Binding]
[Scope(Feature = "Work item field value assignment and calculation")]
public class SetCalculateSteps
{
    private readonly SetCalculateContext _ctx;

    public SetCalculateSteps(SetCalculateContext ctx) => _ctx = ctx;

    [Given(@"a work item of type ""(.*)""")]
    public void GivenWorkItemOfType(string workItemType)
        => _ctx.WorkItemType = workItemType;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithField(string field, string value)
        => _ctx.InputFields[field] = value;

    [Given(@"a work item with field ""([^""]*)"" set to ""([^""]*)"" and ""([^""]*)"" set to ""([^""]*)""")]
    public void GivenWorkItemWithTwoFields(string field1, string value1, string field2, string value2)
    {
        _ctx.InputFields[field1] = value1;
        _ctx.InputFields[field2] = value2;
    }

    [Given(@"a work item without field ""(.*)""")]
    public void GivenWorkItemWithoutField(string field)
        => _ctx.InputFields.Remove(field);

    [Given(@"a SetField transform is configured for field ""(.*)"" with value ""(.*)""")]
    public void GivenSetFieldTransform(string field, string value)
        => _ctx.AddSetFieldTransform(field, value);

    [Given(@"a CalculateField transform is configured for field ""(.*)"" with expression ""(.*)""")]
    public void GivenCalculateFieldTransform(string field, string expression)
        => _ctx.AddCalculateFieldTransform(field, expression);

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

    [Then(@"the field ""(.*)"" should not be modified")]
    public void ThenFieldShouldNotBeModified(string field)
    {
        Assert.IsNotNull(_ctx.Result, "Pipeline result must not be null.");
        // Field was not in input and should not have been added
        bool wasInInput = _ctx.InputFields.ContainsKey(field);
        if (!wasInInput)
        {
            Assert.IsFalse(_ctx.Result.Fields.ContainsKey(field),
                $"Field '{field}' must not be added to output when expression fails.");
        }
        else
        {
            Assert.AreEqual(
                _ctx.InputFields[field]?.ToString(),
                _ctx.Result.Fields[field]?.ToString(),
                $"Field '{field}' must retain original value when expression fails.");
        }
    }
}

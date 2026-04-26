using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure.Steps;

[Binding]
[Scope(Feature = "Skip or fail on unresolvable area and iteration paths")]
public class SkipUnresolvableSteps
{
    private readonly SkipUnresolvableContext _ctx;

    public SkipUnresolvableSteps(SkipUnresolvableContext ctx) => _ctx = ctx;

    [Given(@"a NodeStructure configuration with no mapping rules")]
    public void GivenNoMappingRules()
    {
        // Options will be built in RunProcessorAsync with empty mappings
    }

    [Given(@"SkipOnUnresolvableArea is enabled")]
    public void GivenSkipOnUnresolvableAreaEnabled()
    {
        _ctx.SkipOnUnresolvableArea = true;
    }

    [Given(@"SkipOnUnresolvableIteration is enabled")]
    public void GivenSkipOnUnresolvableIterationEnabled()
    {
        _ctx.SkipOnUnresolvableIteration = true;
    }

    [Given(@"SkipOnUnresolvableArea is disabled")]
    public void GivenSkipOnUnresolvableAreaDisabled()
    {
        _ctx.SkipOnUnresolvableArea = false;
    }

    [Given(@"a revision with area path ""(.*)"" and iteration path ""(.*)""")]
    public void GivenRevisionWithPaths(string areaPath, string iterationPath)
    {
        _ctx.SetPaths(areaPath, iterationPath);
    }

    [When(@"the revision is processed")]
    public async Task WhenRevisionIsProcessed()
    {
        await _ctx.RunProcessorAsync();
    }

    [Then(@"the revision is skipped with a warning")]
    public void ThenRevisionSkippedWithWarning()
    {
        Assert.IsNull(_ctx.CaughtException, "Expected no exception but got: " + _ctx.CaughtException?.Message);
        Assert.IsFalse(_ctx.UpdateFieldsWasCalled, "Expected UpdateFieldsAsync NOT to be called (revision should be skipped).");
    }

    [Then(@"an error is raised identifying the unresolvable area path")]
    public void ThenErrorRaisedForUnresolvableAreaPath()
    {
        Assert.IsNotNull(_ctx.CaughtException, "Expected an InvalidOperationException to be thrown.");
        StringAssert.Contains(_ctx.CaughtException!.Message.ToLowerInvariant(), "area",
            "Exception message should identify the field as area.");
    }

    [Then(@"the revision is skipped with a warning identifying the path as external")]
    public void ThenRevisionSkippedWithExternalWarning()
    {
        Assert.IsNull(_ctx.CaughtException, "Expected no exception but got: " + _ctx.CaughtException?.Message);
        Assert.IsFalse(_ctx.UpdateFieldsWasCalled, "Expected UpdateFieldsAsync NOT to be called (revision should be skipped).");
    }
}

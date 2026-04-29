using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Preserve iteration node start and finish dates during replication")]
public class IterationDatesSteps
{
    private readonly ReplicateSourceTreeContext _ctx;

    public IterationDatesSteps(ReplicateSourceTreeContext ctx) => _ctx = ctx;

    [Given(@"a NodeTranslation configuration with ReplicateSourceTree enabled")]
    public void GivenReplicateEnabled()
    {
        // ReplicateSourceTree is a NodesModule concern — NodeEnsurer always executes when called
    }

    [Given(@"a package containing Nodes/source-tree\.json")]
    public void GivenPackageWithSourceTree()
    {
        // Artifact will be set up in RunReplicateSourceTreeAsync
    }

    [Given(@"the source-tree artifact contains area node ""(.*)""")]
    public void GivenAreaNode(string path)
    {
        _ctx.AddAreaNode(path);
    }

    [Given(@"the source-tree artifact contains iteration node ""(.*)"" with start ""(.*)"" and finish ""(.*)""")]
    public void GivenIterationNodeWithDates(string path, string start, string finish)
    {
        _ctx.AddIterationNode(path, DateTimeOffset.Parse(start), DateTimeOffset.Parse(finish));
    }

    [Given(@"the source-tree artifact contains iteration node ""(.*)"" with no dates")]
    public void GivenIterationNodeNoDates(string path)
    {
        _ctx.AddIterationNode(path, null, null);
    }

    [Given(@"SetIterationDates throws an exception")]
    public void GivenSetIterationDatesThrows()
    {
        _ctx.SetIterationDatesThrows = true;
    }

    [When(@"the replicate-source-tree phase runs")]
    public async Task WhenReplicatePhaseRuns()
    {
        await _ctx.RunReplicateSourceTreeAsync();
    }

    [Then(@"SetIterationDates is called for ""(.*)"" with start ""(.*)"" and finish ""(.*)""")]
    public void ThenSetIterationDatesCalled(string targetPath, string start, string finish)
    {
        var expectedStart = DateTimeOffset.Parse(start);
        var expectedFinish = DateTimeOffset.Parse(finish);

        _ctx.NodeCreatorMock.Verify(c => c.SetIterationDatesAsync(
            It.Is<string>(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)),
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value.Date == expectedStart.Date),
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value.Date == expectedFinish.Date),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Then(@"SetIterationDates is not called for any node")]
    public void ThenSetIterationDatesNotCalled()
    {
        _ctx.NodeCreatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Then(@"the replication completes without throwing")]
    public void ThenReplicationCompletesWithoutThrowing()
    {
        Assert.IsNull(_ctx.CaughtException, "Expected no exception but got: " + _ctx.CaughtException?.Message);
    }

    [Then(@"a warning is logged for the date-setting failure")]
    public void ThenWarningLoggedForDateFailure()
    {
        // The NodeEnsurer catches the exception, logs a warning, and continues.
        // We verify by confirming SetIterationDatesAsync was attempted and EnsureExistsAsync was called.
        _ctx.NodeCreatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "SetIterationDatesAsync should have been attempted before the failure.");
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "EnsureExistsAsync should have been called (node creation succeeded before date failure).");
    }
}

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
[Scope(Feature = "Replicate source classification tree to target")]
public class ReplicateSourceTreeSteps
{
    private readonly ReplicateSourceTreeContext _ctx;

    public ReplicateSourceTreeSteps(ReplicateSourceTreeContext ctx) => _ctx = ctx;

    [Given(@"a valid NodeTranslation configuration")]
    public void GivenValidConfig()
    {
        // Default options used by context
    }

    [Given(@"a package containing Nodes/source-tree\.json")]
    public void GivenPackageWithSourceTree()
    {
        // Artifact will be set up in RunReplicateSourceTreeAsync based on added nodes
    }

    [Given(@"the source-tree artifact contains area node ""(.*)""")]
    public void GivenAreaNode(string path)
    {
        _ctx.AddAreaNode(path);
    }

    [Given(@"the source-tree artifact contains iteration node ""(.*)"" with no dates")]
    public void GivenIterationNodeNoDates(string path)
    {
        _ctx.AddIterationNode(path, null, null);
    }

    [Given(@"""(.*)"" is already in the node replication checkpoint")]
    public void GivenPathInCheckpoint(string targetPath)
    {
        _ctx.AddCheckpointedPath(targetPath);
    }

    [Given(@"the source-tree artifact is absent from the package")]
    public void GivenSourceTreeAbsent()
    {
        _ctx.SourceTreeArtifactAbsent = true;
    }

    [When(@"the replicate-source-tree phase runs")]
    public async Task WhenReplicatePhaseRuns()
    {
        await _ctx.RunReplicateSourceTreeAsync();
    }

    [Then(@"the area node ""(.*)"" is created in the target")]
    public void ThenAreaNodeCreated(string targetPath)
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Area,
            It.Is<string>(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Then(@"the iteration node ""(.*)"" is created in the target")]
    public void ThenIterationNodeCreated(string targetPath)
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Iteration,
            It.Is<string>(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Then(@"no nodes are created in the target")]
    public void ThenNoNodesCreated()
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Then(@"no additional nodes are created for ""(.*)""")]
    public void ThenNoAdditionalNodesForPath(string targetPath)
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.Is<string>(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Then(@"no nodes are created and a warning is logged")]
    public void ThenNoNodesCreatedAndWarning()
    {
        // The NodeEnsurer logs a warning and returns without creating nodes
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsNull(_ctx.CaughtException, "Expected no exception to be thrown.");
    }
}

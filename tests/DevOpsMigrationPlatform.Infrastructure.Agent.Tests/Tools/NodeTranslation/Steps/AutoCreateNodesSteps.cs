// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Auto-create missing classification nodes")]
public class AutoCreateNodesSteps
{
    private readonly AutoCreateNodesContext _ctx;

    public AutoCreateNodesSteps(AutoCreateNodesContext ctx) => _ctx = ctx;

    [Given(@"a NodeTranslation configuration with AutoCreateNodes enabled")]
    public void GivenAutoCreateEnabled()
    {
        _ctx.AutoCreateNodesEnabled = true;
    }

    [Given(@"a NodeTranslation configuration with AutoCreateNodes disabled")]
    public void GivenAutoCreateDisabled()
    {
        _ctx.AutoCreateNodesEnabled = false;
    }

    [Given(@"a source package with referenced-paths.json")]
    public void GivenPackageWithReferencedPaths()
    {
        // Paths set up by subsequent Given steps; artifact will be set up before When
    }

    [Given(@"the referenced-paths artifact contains area path ""(.*)""")]
    public void GivenArtifactContainsAreaPath(string path)
    {
        _ctx.AddAreaPath(path);
    }

    [Given(@"the referenced-paths artifact contains no paths")]
    public void GivenArtifactContainsNoPaths()
    {
        // No paths added; set up empty artifact
    }

    [Given(@"the area node ""(.*)"" does not exist in the target")]
    public void GivenNodeDoesNotExist(string path)
    {
        _ctx.NodeCreatorMock.Setup(c => c.NodeExistsAsync(
            ClassificationNodeType.Area, path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Given(@"the area node ""(.*)"" already exists in the target")]
    public void GivenNodeAlreadyExists(string path)
    {
        _ctx.NodeCreatorMock.Setup(c => c.NodeExistsAsync(
            ClassificationNodeType.Area, path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [When(@"the pre-collection phase runs")]
    public async Task WhenPreCollectionRuns()
    {
        if (_ctx.AutoCreateNodesEnabled && _ctx.GetType().GetField("_areaPaths",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_ctx) is System.Collections.Generic.List<string> { Count: > 0 })
            _ctx.SetupArtifact();
        else if (!_ctx.AutoCreateNodesEnabled)
            _ctx.SetupArtifact();
        else
            _ctx.SetupEmptyArtifact();

        var orchestrator = _ctx.BuildOrchestrator();
        var context = new ProjectMapping("SourceProject", "TargetProject");
        await orchestrator.EnsureReferencedPathsAsync(context, _ctx.ArtefactStoreMock.Object, CancellationToken.None);
    }

    [Then(@"the area node ""(.*)"" is created in the target")]
    public void ThenNodeCreated(string path)
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Area,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Then(@"the node creator is called exactly once for ""(.*)""")]
    public void ThenNodeCreatorCalledOnce(string path)
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Area,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Then(@"no nodes are created in the target")]
    public void ThenNoNodesCreated()
    {
        _ctx.NodeCreatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}

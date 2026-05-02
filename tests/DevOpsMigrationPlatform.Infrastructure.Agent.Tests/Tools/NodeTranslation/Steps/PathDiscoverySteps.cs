// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Export-time area and iteration path discovery")]
public class PathDiscoverySteps
{
    private readonly PathDiscoveryContext _ctx;

    public PathDiscoverySteps(PathDiscoveryContext ctx) => _ctx = ctx;

    [Given(@"an empty artefact store for path discovery")]
    public void GivenAnEmptyArtefactStore()
    {
        _ctx.SetupNoExistingArtifact();
        _ctx.CreateTracker();
    }

    [Given(@"no referenced-paths artifact exists in the package")]
    public void GivenNoArtifactExists()
    {
        _ctx.SetupNoExistingArtifact();
        _ctx.CreateTracker();
    }

    [Given(@"the referenced-paths artifact already contains area path ""(.*)""")]
    public async Task GivenArtifactContainsAreaPath(string path)
    {
        _ctx.SetupExistingArtifact(new List<string> { path });
        _ctx.CreateTracker();
        await _ctx.Tracker!.InitializeAsync(_ctx.ArtefactStoreMock.Object, CancellationToken.None);
    }

    [When(@"the path tracker discovers area path ""(.*)""")]
    public async Task WhenDiscoverAreaPath(string path)
    {
        await _ctx.Tracker!.RecordAreaPathAsync(path, _ctx.ArtefactStoreMock.Object, CancellationToken.None);
    }

    [When(@"the path tracker discovers iteration path ""(.*)""")]
    public async Task WhenDiscoverIterationPath(string path)
    {
        await _ctx.Tracker!.RecordIterationPathAsync(path, _ctx.ArtefactStoreMock.Object, CancellationToken.None);
    }

    [When(@"the path tracker is initialized from the existing artifact")]
    public async Task WhenTrackerInitialized()
    {
        await _ctx.Tracker!.InitializeAsync(_ctx.ArtefactStoreMock.Object, CancellationToken.None);
    }

    [Then(@"the referenced-paths artifact contains area path ""(.*)""")]
    public void ThenArtifactContainsAreaPath(string path)
    {
        Assert.IsTrue(_ctx.Tracker!.AreaPaths.Contains(path),
            $"Expected area path '{path}' in tracker");
    }

    [Then(@"the referenced-paths artifact contains exactly (\d+) area path")]
    public void ThenArtifactContainsExactlyNAreaPaths(int count)
    {
        Assert.AreEqual(count, _ctx.Tracker!.AreaPaths.Count,
            $"Expected exactly {count} area path(s)");
    }

    [Then(@"the referenced-paths artifact contains (\d+) area paths")]
    public void ThenArtifactContainsNAreaPaths(int count)
    {
        Assert.AreEqual(count, _ctx.Tracker!.AreaPaths.Count,
            $"Expected {count} area paths");
    }

    [Then(@"the referenced-paths artifact contains (\d+) iteration path")]
    public void ThenArtifactContainsNIterationPaths(int count)
    {
        Assert.AreEqual(count, _ctx.Tracker!.IterationPaths.Count,
            $"Expected {count} iteration path(s)");
    }
}

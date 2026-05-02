// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Export source classification tree capture")]
public class TreeCaptureSteps
{
    private readonly TreeCaptureContext _ctx;

    public TreeCaptureSteps(TreeCaptureContext ctx) => _ctx = ctx;

    [Given(@"a source project with classification nodes")]
    public void GivenASourceProjectWithClassificationNodes() { /* setup via other steps */ }

    [Given(@"the source project has area nodes ""(.*)"" and ""(.*)""")]
    public void GivenAreaNodes(string node1, string node2)
    {
        _ctx.AddAreaNode(node1);
        _ctx.AddAreaNode(node2);
    }

    [Given(@"the source project has iteration node ""(.*)"" with start ""(.*)"" and finish ""(.*)""")]
    public void GivenIterationNodeWithDates(string path, string start, string finish)
    {
        var startDate = DateTimeOffset.Parse(start);
        var finishDate = DateTimeOffset.Parse(finish);
        _ctx.AddIterationNode(new IterationNodeEntry(path, startDate, finishDate, false));
    }

    [Given(@"the source project has iteration node ""(.*)"" with no dates")]
    public void GivenIterationNodeNoDates(string path)
    {
        _ctx.AddIterationNode(new IterationNodeEntry(path, null, null, false));
    }

    [Given(@"the classification tree reader throws an exception")]
    public void GivenReaderThrows()
    {
        _ctx.SetReaderThrows();
    }

    [When(@"the classification tree is captured during export")]
    public async Task WhenCaptured()
    {
        await _ctx.RunCaptureAsync();
    }

    [When(@"the classification tree capture is attempted")]
    public async Task WhenCaptureAttempted()
    {
        await _ctx.RunCaptureAsync();
    }

    [Then(@"the source-tree artifact contains area node ""(.*)""")]
    public void ThenAreaNodePresent(string path)
    {
        Assert.IsNotNull(_ctx.CapturedSnapshot, "Snapshot should have been captured");
        Assert.IsTrue(_ctx.CapturedSnapshot!.AreaNodes.Contains(path, StringComparer.OrdinalIgnoreCase),
            $"Expected area node '{path}'");
    }

    [Then(@"the source-tree artifact contains iteration node ""(.*)"" with start ""(.*)"" and finish ""(.*)""")]
    public void ThenIterationNodeWithDates(string path, string start, string finish)
    {
        Assert.IsNotNull(_ctx.CapturedSnapshot);
        var node = _ctx.CapturedSnapshot!.IterationNodes.FirstOrDefaultByPath(path);
        Assert.IsNotNull(node, $"Expected iteration node '{path}'");
        Assert.IsNotNull(node.StartDate);
        Assert.IsNotNull(node.FinishDate);
        Assert.AreEqual(DateTimeOffset.Parse(start).Date, node.StartDate!.Value.Date);
        Assert.AreEqual(DateTimeOffset.Parse(finish).Date, node.FinishDate!.Value.Date);
    }

    [Then(@"the source-tree artifact contains iteration node ""(.*)"" with no dates")]
    public void ThenIterationNodeNoDates(string path)
    {
        Assert.IsNotNull(_ctx.CapturedSnapshot);
        var node = _ctx.CapturedSnapshot!.IterationNodes.FirstOrDefaultByPath(path);
        Assert.IsNotNull(node, $"Expected iteration node '{path}'");
        Assert.IsNull(node.StartDate);
        Assert.IsNull(node.FinishDate);
    }

    [Then(@"the capture fails with an error")]
    public void ThenCaptureFailed()
    {
        Assert.IsNotNull(_ctx.CaptureException, "Expected capture to fail with an exception");
    }
}

file static class IterationNodeExtensions
{
    public static IterationNodeEntry? FirstOrDefaultByPath(this IReadOnlyList<IterationNodeEntry> nodes, string path)
    {
        foreach (var n in nodes)
            if (string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }
}

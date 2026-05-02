// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Area and iteration path mapping")]
public class PathMappingSteps
{
    private readonly PathMappingContext _ctx;
    public PathMappingSteps(PathMappingContext ctx) => _ctx = ctx;

    [Given(@"a source project named ""(.*)"" and a target project named ""(.*)""")]
    public void GivenProjectNames(string source, string target)
    {
        _ctx.SourceProjectName = source;
        _ctx.TargetProjectName = target;
    }

    [Given(@"a NodeTranslation tool configured with area path mapping ""(.*)"" replaced by ""(.*)""")]
    public void GivenAreaPathMapping(string match, string replacement)
    {
        _ctx.AreaMappings.Add(new NodeMapping(match, replacement));
    }

    [Given(@"a NodeTranslation tool with no mapping rules configured")]
    public void GivenNoMappingRules() { /* default — empty lists */ }

    [Given(@"a NodeTranslation tool that is disabled")]
    public void GivenToolDisabled()
    {
        _ctx.Enabled = false;
    }

    [Given(@"a NodeTranslation tool configured with two area path mappings:")]
    public void GivenTwoAreaMappings(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            _ctx.AreaMappings.Add(new NodeMapping(row["Match"], row["Replacement"]));
        }
    }

    [When(@"the tool translates area path ""(.*)""")]
    public void WhenTranslatesAreaPath(string path)
    {
        _ctx.Result = _ctx.GetTool().TranslatePath("System.AreaPath", path, _ctx.GetMapping());
    }

    [When(@"the tool is checked for enabled state")]
    public void WhenCheckedForEnabledState() { /* just check IsEnabled */ }

    [Then(@"the translated area path is ""(.*)""")]
    public void ThenTranslatedAreaPath(string expected)
    {
        Assert.IsNotNull(_ctx.Result, "Translation result must not be null");
        Assert.AreEqual(expected, _ctx.Result.TargetPath);
    }

    [Then(@"the translation was matched by a mapping rule")]
    public void ThenMatchedByMap()
    {
        Assert.IsNotNull(_ctx.Result);
        Assert.IsTrue(_ctx.Result.MatchedByMap, "Expected MatchedByMap to be true");
    }

    [Then(@"the translation was matched by auto project-name swap")]
    public void ThenMatchedByAutoSwap()
    {
        Assert.IsNotNull(_ctx.Result);
        Assert.IsTrue(_ctx.Result.MatchedByProjectSwap, "Expected MatchedByProjectSwap to be true");
    }

    [Then(@"the translation is marked as an external path")]
    public void ThenIsExternalPath()
    {
        Assert.IsNotNull(_ctx.Result);
        Assert.IsTrue(_ctx.Result.IsExternalPath, "Expected IsExternalPath to be true");
    }

    [Then(@"the tool reports it is not enabled")]
    public void ThenToolIsNotEnabled()
    {
        Assert.IsFalse(_ctx.GetTool().IsEnabled, "Expected tool to be disabled");
    }
}

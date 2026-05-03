// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "NodeTranslation configuration and package validation")]
public class NodeTranslationValidationSteps
{
    private readonly NodeTranslationValidationContext _ctx;
    private string? _invalidRegexPattern;

    public NodeTranslationValidationSteps(NodeTranslationValidationContext ctx) => _ctx = ctx;

    [Given(@"a package with referenced-paths\.json and NodeTranslation configuration")]
    public void GivenPackageWithReferencedPaths()
    {
        // Setup done by subsequent Given steps
    }

    [Given(@"the referenced-paths artifact contains area path ""(.*)""")]
    public void GivenArtifactContainsAreaPath(string path)
    {
        _ctx.AddAreaPath(path);
    }

    [Given(@"the referenced-paths artifact contains no paths")]
    public void GivenArtifactContainsNoPaths()
    {
        // No paths added — empty artifact will be used
    }

    [Given(@"a NodeTranslation mapping rule maps ""(.*)"" to ""(.*)""")]
    public void GivenMappingRule(string matchPattern, string replacement)
    {
        // Escape the literal path so it becomes a valid regex pattern
        _ctx.AddAreaMapping(new NodeMapping(Regex.Escape(matchPattern), replacement));
    }

    [Given(@"no mapping rules are configured")]
    public void GivenNoMappingRules()
    {
        // No mappings added
    }

    [Given(@"a NodeTranslation mapping rule with an invalid regex pattern ""(.*)""")]
    public void GivenInvalidRegexPattern(string pattern)
    {
        _invalidRegexPattern = pattern;
    }

    [When(@"validation runs")]
    public async Task WhenValidationRuns()
    {
        if (_invalidRegexPattern != null)
            await _ctx.RunValidationWithInvalidRegexAsync(_invalidRegexPattern);
        else
            await _ctx.RunValidationAsync();
    }

    [Then(@"the validation report is valid")]
    public void ThenReportIsValid()
    {
        Assert.IsNotNull(_ctx.ValidationReport);
        Assert.IsTrue(_ctx.ValidationReport!.IsValid,
            $"Expected IsValid=true. UnmappedPaths={_ctx.ValidationReport.UnmappedPaths.Count}, " +
            $"UnanchoredPaths={_ctx.ValidationReport.UnanchoredPaths.Count}, " +
            $"MalformedTargetPaths={_ctx.ValidationReport.MalformedTargetPaths.Count}");
    }

    [Then(@"the validation report contains an unmapped path finding for ""(.*)""")]
    public void ThenReportContainsUnmappedFinding(string path)
    {
        Assert.IsNotNull(_ctx.ValidationReport);
        // External paths go to UnanchoredPaths; check either collection for coverage
        bool found = _ctx.ValidationReport!.UnmappedPaths.Any(f => f.Path == path)
                  || _ctx.ValidationReport.UnanchoredPaths.Any(f => f.Path == path);
        Assert.IsTrue(found, $"Expected a finding for path '{path}' in UnmappedPaths or UnanchoredPaths.");
        Assert.IsFalse(_ctx.ValidationReport.IsValid);
    }

    [Then(@"the validation report contains an external path finding for ""(.*)""")]
    public void ThenReportContainsExternalFinding(string path)
    {
        Assert.IsNotNull(_ctx.ValidationReport);
        bool found = _ctx.ValidationReport!.UnanchoredPaths.Any(f => f.Path == path);
        Assert.IsTrue(found, $"Expected an unanchored path finding for '{path}'.");
        Assert.IsFalse(_ctx.ValidationReport.IsValid);
    }

    [Then(@"the validation report contains an invalid regex finding")]
    public void ThenReportContainsInvalidRegexFinding()
    {
        Assert.IsNotNull(_ctx.ValidationReport);
        Assert.IsTrue(_ctx.ValidationReport!.MalformedTargetPaths.Count > 0,
            "Expected at least one malformed target path finding.");
        Assert.IsFalse(_ctx.ValidationReport.IsValid);
    }
}

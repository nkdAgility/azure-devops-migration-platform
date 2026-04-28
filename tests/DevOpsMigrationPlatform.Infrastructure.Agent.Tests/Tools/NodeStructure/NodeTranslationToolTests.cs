using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure;

[TestClass]
public class NodeTranslationToolTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");

    private static NodeStructureTool CreateTool(NodeStructureOptions options)
        => new NodeStructureTool(Options.Create(options));

    private static NodeStructureOptions DefaultOptions(
        IReadOnlyList<NodeMapping>? areaMappings = null,
        IReadOnlyList<NodeMapping>? iterationMappings = null,
        bool enabled = true,
        string? areaLanguageOverride = null,
        string? iterationLanguageOverride = null)
        => new NodeStructureOptions
        {
            Enabled = enabled,
            AreaPathMappings = areaMappings ?? [],
            IterationPathMappings = iterationMappings ?? [],
            AreaLanguageOverride = areaLanguageOverride,
            IterationLanguageOverride = iterationLanguageOverride
        };

    // --- Regex map hit ---

    [TestMethod]
    public void TranslatePath_WhenRuleMatches_ReturnsRemappedPath()
    {
        var sut = CreateTool(DefaultOptions(
            areaMappings: [new NodeMapping(@"^SourceProject\\(.*)", @"TargetProject\$1")]));

        var result = sut.TranslatePath("System.AreaPath", @"SourceProject\Team A", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Team A", result.TargetPath);
        Assert.IsTrue(result.MatchedByMap);
        Assert.IsFalse(result.MatchedByProjectSwap);
        Assert.IsFalse(result.IsExternalPath);
    }

    [TestMethod]
    public void TranslatePath_WhenRuleMatchesWithCaptureGroups_AppliesReplacement()
    {
        var sut = CreateTool(DefaultOptions(
            areaMappings: [new NodeMapping(@"^SourceProject\\Team A\\(.*)", @"TargetProject\Merged\$1")]));

        var result = sut.TranslatePath("System.AreaPath", @"SourceProject\Team A\Feature 2", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Merged\Feature 2", result.TargetPath);
        Assert.IsTrue(result.MatchedByMap);
    }

    [TestMethod]
    public void TranslatePath_FirstMatchingRuleWins()
    {
        var sut = CreateTool(DefaultOptions(
            areaMappings: [
                new NodeMapping(@"^SourceProject\\Team A\\(.*)", @"TargetProject\First\$1"),
                new NodeMapping(@"^SourceProject\\(.*)", @"TargetProject\Second\$1")
            ]));

        var result = sut.TranslatePath("System.AreaPath", @"SourceProject\Team A\Feature 3", DefaultMapping);

        Assert.AreEqual(@"TargetProject\First\Feature 3", result.TargetPath);
    }

    [TestMethod]
    public void TranslatePath_MatchingIsCaseInsensitive()
    {
        var sut = CreateTool(DefaultOptions(
            areaMappings: [new NodeMapping(@"^sourceproject\\(.*)", @"TargetProject\$1")]));

        var result = sut.TranslatePath("System.AreaPath", @"SOURCEPROJECT\Team D", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Team D", result.TargetPath);
        Assert.IsTrue(result.MatchedByMap);
    }

    // --- Auto-swap ---

    [TestMethod]
    public void TranslatePath_WhenNoRuleMatches_AutoSwapsProjectName()
    {
        var sut = CreateTool(DefaultOptions());

        var result = sut.TranslatePath("System.AreaPath", @"SourceProject\Team B", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Team B", result.TargetPath);
        Assert.IsFalse(result.MatchedByMap);
        Assert.IsTrue(result.MatchedByProjectSwap);
        Assert.IsFalse(result.IsExternalPath);
    }

    // --- External path pass-through ---

    [TestMethod]
    public void TranslatePath_WhenPathFromOtherProject_MarksAsExternal()
    {
        var sut = CreateTool(DefaultOptions());

        var result = sut.TranslatePath("System.AreaPath", @"OtherProject\Team C", DefaultMapping);

        Assert.AreEqual(@"OtherProject\Team C", result.TargetPath);
        Assert.IsFalse(result.MatchedByMap);
        Assert.IsFalse(result.MatchedByProjectSwap);
        Assert.IsTrue(result.IsExternalPath);
    }

    // --- Whitespace trimming ---

    [TestMethod]
    public void TranslatePath_WhitespaceTrimmedBeforeProcessing()
    {
        var sut = CreateTool(DefaultOptions());

        var result = sut.TranslatePath("System.AreaPath", @"  SourceProject\Team B  ", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Team B", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap);
    }

    // --- Language override ---

    [TestMethod]
    public void TranslatePath_AreaLanguageOverride_NormalisesRootSegment()
    {
        var sut = CreateTool(DefaultOptions(areaLanguageOverride: "Area"));

        // Path root segment "Área" (Spanish) is normalised to "Area"
        var result = sut.TranslatePath("System.AreaPath", @"Área\Team A", DefaultMapping);

        // After override, path becomes "Area\Team A" — then auto-swap won't match since root is now "Area"
        // so it will be external (not SourceProject)
        Assert.IsTrue(result.TargetPath!.StartsWith("Area\\"));
    }

    [TestMethod]
    public void TranslatePath_IterationLanguageOverride_NormalisesRootSegment()
    {
        var sut = CreateTool(DefaultOptions(iterationLanguageOverride: "Iteration"));

        var result = sut.TranslatePath("System.IterationPath", @"Iteración\Sprint 1", DefaultMapping);

        Assert.IsTrue(result.TargetPath!.StartsWith("Iteration\\"));
    }

    // --- Language override + auto-swap ---

    [TestMethod]
    public void TranslatePath_AreaLanguageOverride_NormalisesLocalisedRootThenAutoSwaps()
    {
        // After override "Área" → "SourceProject", auto-swap fires because root now matches source project
        var sut = CreateTool(DefaultOptions(areaLanguageOverride: "SourceProject"));
        var result = sut.TranslatePath("System.AreaPath", @"Área\Team A", DefaultMapping);
        Assert.AreEqual(@"TargetProject\Team A", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap, "Expected auto-swap after language override normalised root to source project name.");
    }

    [TestMethod]
    public void TranslatePath_IterationLanguageOverride_NormalisesLocalisedRootThenAutoSwaps()
    {
        // After override "Iteración" → "SourceProject", auto-swap fires
        var sut = CreateTool(DefaultOptions(iterationLanguageOverride: "SourceProject"));
        var result = sut.TranslatePath("System.IterationPath", @"Iteración\Sprint 1", DefaultMapping);
        Assert.AreEqual(@"TargetProject\Sprint 1", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap);
    }
}

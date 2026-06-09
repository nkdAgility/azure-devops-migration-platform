// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class NodeTranslationToolTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");

    private static NodeTranslationTool CreateTool(NodeTranslationOptions options)
        => new NodeTranslationTool(Options.Create(options));

    private static NodeTranslationOptions DefaultOptions(
        IReadOnlyList<NodeMapping>? areaMappings = null,
        IReadOnlyList<NodeMapping>? iterationMappings = null,
        bool enabled = true,
        string? areaLanguageOverride = null,
        string? iterationLanguageOverride = null)
        => new NodeTranslationOptions
        {
            Enabled = enabled,
            AreaPathMappings = areaMappings ?? [],
            IterationPathMappings = iterationMappings ?? [],
            AreaLanguageOverride = areaLanguageOverride,
            IterationLanguageOverride = iterationLanguageOverride
        };

    // --- Regex map hit ---

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_WhenRuleMatchesWithCaptureGroups_AppliesReplacement()
    {
        var sut = CreateTool(DefaultOptions(
            areaMappings: [new NodeMapping(@"^SourceProject\\Team A\\(.*)", @"TargetProject\Merged\$1")]));

        var result = sut.TranslatePath("System.AreaPath", @"SourceProject\Team A\Feature 2", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Merged\Feature 2", result.TargetPath);
        Assert.IsTrue(result.MatchedByMap);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_WhenCalledWithSameInput_ReturnsMemoizedResultInstance()
    {
        var sut = CreateTool(DefaultOptions());

        var first = sut.TranslatePath("System.AreaPath", @"SourceProject\Team B", DefaultMapping);
        var second = sut.TranslatePath("System.AreaPath", @"SourceProject\Team B", DefaultMapping);

        Assert.AreSame(first, second);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task TranslatePath_WhenCalledConcurrentlyWithSameInput_ReturnsSameMemoizedInstance()
    {
        var sut = CreateTool(DefaultOptions());
        const int callerCount = 64;
        using var startGate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, callerCount)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                return sut.TranslatePath("System.AreaPath", @"SourceProject\Team B", DefaultMapping);
            }))
            .ToArray();

        startGate.Set();
        var results = await Task.WhenAll(tasks);
        var distinctReferences = new HashSet<PathTranslation>(ReferenceEqualityComparer.Instance);
        foreach (var result in results)
        {
            distinctReferences.Add(result);
        }

        Assert.AreEqual(1, distinctReferences.Count, "Concurrent callers should observe a single memoized PathTranslation instance.");
    }

    // --- Whitespace trimming ---

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_WhitespaceTrimmedBeforeProcessing()
    {
        var sut = CreateTool(DefaultOptions());

        var result = sut.TranslatePath("System.AreaPath", @"  SourceProject\Team B  ", DefaultMapping);

        Assert.AreEqual(@"TargetProject\Team B", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap);
    }

    // --- Language override ---

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_IterationLanguageOverride_NormalisesRootSegment()
    {
        var sut = CreateTool(DefaultOptions(iterationLanguageOverride: "Iteration"));

        var result = sut.TranslatePath("System.IterationPath", @"Iteración\Sprint 1", DefaultMapping);

        Assert.IsTrue(result.TargetPath!.StartsWith("Iteration\\"));
    }

    // --- Language override + auto-swap ---

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_AreaLanguageOverride_NormalisesLocalisedRootThenAutoSwaps()
    {
        // After override "Área" → "SourceProject", auto-swap fires because root now matches source project
        var sut = CreateTool(DefaultOptions(areaLanguageOverride: "SourceProject"));
        var result = sut.TranslatePath("System.AreaPath", @"Área\Team A", DefaultMapping);
        Assert.AreEqual(@"TargetProject\Team A", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap, "Expected auto-swap after language override normalised root to source project name.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TranslatePath_IterationLanguageOverride_NormalisesLocalisedRootThenAutoSwaps()
    {
        // After override "Iteración" → "SourceProject", auto-swap fires
        var sut = CreateTool(DefaultOptions(iterationLanguageOverride: "SourceProject"));
        var result = sut.TranslatePath("System.IterationPath", @"Iteración\Sprint 1", DefaultMapping);
        Assert.AreEqual(@"TargetProject\Sprint 1", result.TargetPath);
        Assert.IsTrue(result.MatchedByProjectSwap);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabled_ReturnsFalse_WhenToolConfiguredAsDisabled()
    {
        var sut = CreateTool(DefaultOptions(enabled: false));
        Assert.IsFalse(sut.IsEnabled);
    }
}


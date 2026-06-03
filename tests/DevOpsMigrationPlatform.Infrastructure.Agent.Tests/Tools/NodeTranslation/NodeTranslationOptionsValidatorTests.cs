// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

/// <summary>
/// Unit tests for <see cref="NodeTranslationOptionsValidator"/>.
/// </summary>
[TestClass]
public class NodeTranslationOptionsValidatorTests
{
    private static NodeTranslationOptionsValidator Sut() => new();

    private static NodeTranslationOptions ValidOptions() => new()
    {
        AreaPathMappings = [],
        IterationPathMappings = []
    };

    // ── Passing cases ─────────────────────────────────────────────────────────

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_EmptyMappings_Succeeds()
    {
        var result = Sut().Validate(null, ValidOptions());
        Assert.IsTrue(result.Succeeded);
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_ValidAreaPathMapping_Succeeds()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [new NodeMapping(@"^OldProject\\", "NewProject\\")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded);
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_ValidIterationPathMapping_Succeeds()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [],
            IterationPathMappings = [new NodeMapping(@"Sprint (\d+)", "Iteration $1")]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded);
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_MultipleMappings_AllValid_Succeeds()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings =
            [
                new NodeMapping(@"^Source\\Team A", "Target\\Team A"),
                new NodeMapping(@"^Source\\Team B", "Target\\Team B")
            ],
            IterationPathMappings =
            [
                new NodeMapping(@"Sprint \d+", "Iteration $0")
            ]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded);
    }

    // ── Failing cases ─────────────────────────────────────────────────────────

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_AreaPathMapping_EmptyMatch_Fails()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [new NodeMapping("", "NewProject")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AreaPathMappings[0].Match is required");
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_AreaPathMapping_WhitespaceMatch_Fails()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [new NodeMapping("   ", "NewProject")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_IterationPathMapping_InvalidRegex_Fails()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [],
            IterationPathMappings = [new NodeMapping("[invalid(", "replacement")]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "IterationPathMappings[0].Match");
        StringAssert.Contains(string.Join("|", result.Failures!), "not a valid regular expression");
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_AreaPathMapping_InvalidRegex_Fails()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [new NodeMapping("(unclosed", "replacement")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AreaPathMappings[0].Match");
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_AreaPathMapping_LookbehindPatternRejectedByRuntimeOptions_Fails()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings = [new NodeMapping("(?<=SourceProject\\\\)Team A", "TargetTeam")],
            IterationPathMappings = []
        };

        var result = Sut().Validate(null, opts);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AreaPathMappings[0].Match");
    }

    [TestCategory("UnitTests")]

    [TestMethod]
    public void Validate_MultipleInvalidMappings_ReportsAllErrors()
    {
        var opts = new NodeTranslationOptions
        {
            AreaPathMappings =
            [
                new NodeMapping("", "a"),
                new NodeMapping("[bad", "b")
            ],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        var failures = string.Join("|", result.Failures!);
        StringAssert.Contains(failures, "AreaPathMappings[0]");
        StringAssert.Contains(failures, "AreaPathMappings[1]");
    }
}



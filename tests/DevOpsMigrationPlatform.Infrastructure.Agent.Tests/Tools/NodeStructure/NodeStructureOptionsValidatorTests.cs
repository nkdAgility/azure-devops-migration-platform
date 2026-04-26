using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure;

/// <summary>
/// Unit tests for <see cref="NodeStructureOptionsValidator"/>.
/// </summary>
[TestClass]
public class NodeStructureOptionsValidatorTests
{
    private static NodeStructureOptionsValidator Sut() => new();

    private static NodeStructureOptions ValidOptions() => new()
    {
        AreaPathMappings = [],
        IterationPathMappings = []
    };

    // ── Passing cases ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_EmptyMappings_Succeeds()
    {
        var result = Sut().Validate(null, ValidOptions());
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ValidAreaPathMapping_Succeeds()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [new NodeMapping(@"^OldProject\\", "NewProject\\")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ValidIterationPathMapping_Succeeds()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [],
            IterationPathMappings = [new NodeMapping(@"Sprint (\d+)", "Iteration $1")]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_MultipleMappings_AllValid_Succeeds()
    {
        var opts = new NodeStructureOptions
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

    [TestMethod]
    public void Validate_AreaPathMapping_EmptyMatch_Fails()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [new NodeMapping("", "NewProject")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AreaPathMappings[0].Match is required");
    }

    [TestMethod]
    public void Validate_AreaPathMapping_WhitespaceMatch_Fails()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [new NodeMapping("   ", "NewProject")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void Validate_IterationPathMapping_InvalidRegex_Fails()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [],
            IterationPathMappings = [new NodeMapping("[invalid(", "replacement")]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "IterationPathMappings[0].Match");
        StringAssert.Contains(string.Join("|", result.Failures!), "not a valid regular expression");
    }

    [TestMethod]
    public void Validate_AreaPathMapping_InvalidRegex_Fails()
    {
        var opts = new NodeStructureOptions
        {
            AreaPathMappings = [new NodeMapping("(unclosed", "replacement")],
            IterationPathMappings = []
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AreaPathMappings[0].Match");
    }

    [TestMethod]
    public void Validate_MultipleInvalidMappings_ReportsAllErrors()
    {
        var opts = new NodeStructureOptions
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

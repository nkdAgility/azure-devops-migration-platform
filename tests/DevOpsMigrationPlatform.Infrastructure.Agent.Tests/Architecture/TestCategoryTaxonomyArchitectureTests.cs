// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Architecture;

/// <summary>
/// Convention tests for the canonical test-category taxonomy
/// (.agents/20-guardrails/workflow/testing-rules.md, tests/AGENTS.md rule 1):
/// only the eight canonical [TestCategory] strings are valid, and every
/// [TestMethod] carries both a parent family tag (CodeTest | SystemTest) and a
/// specific category tag, at method or class level. The Touch = Tag rule was
/// previously prose-only and drifted; these tests make it machine-enforced
/// (ADR-0031 follow-on, Phase 2 of analysis/agentic-maturity-level4-plan.md).
/// </summary>
[TestClass]
public sealed class TestCategoryTaxonomyArchitectureTests
{
    private static readonly HashSet<string> ParentFamilies = new(StringComparer.Ordinal)
    {
        "CodeTest",
        "SystemTest",
    };

    private static readonly HashSet<string> SpecificCategories = new(StringComparer.Ordinal)
    {
        "UnitTests",
        "DomainTests",
        "IntegrationTests",
        "SystemTest_Smoke",
        "SystemTest_Simulated",
        "SystemTest_Live",
    };

    // Tolerates whitespace before '(' / around the literal, combined attributes
    // (two categories in one [...] list) and line breaks inside the attribute.
    private static readonly Regex CategoryRegex = new(
        "(?<![\\w.])TestCategory(?:Attribute)?\\s*\\(\\s*\"(?<name>[^\"]+)\"\\s*\\)", RegexOptions.Compiled);

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AllTestCategoryStrings_AreCanonical()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateTestSourceFiles())
        {
            // Whole-file scan so attributes split across lines are still checked.
            var text = File.ReadAllText(file);
            foreach (Match match in CategoryRegex.Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (!ParentFamilies.Contains(name) && !SpecificCategories.Contains(name))
                {
                    var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                    violations.Add($"{Relative(file)}({line}): \"{name}\"");
                }
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Non-canonical [TestCategory] strings found. Only CodeTest, SystemTest, "
            + "UnitTests, DomainTests, IntegrationTests, SystemTest_Smoke, "
            + "SystemTest_Simulated, SystemTest_Live are valid "
            + "(.agents/20-guardrails/workflow/testing-rules.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void EveryTestMethod_CarriesParentFamilyAndSpecificCategory()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateTestSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var classCategories = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < lines.Length; i++)
            {
                // Attribute lines only — doc comments or code may contain the
                // literal text "[TestMethod" without declaring the attribute.
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith('['))
                {
                    continue;
                }

                if (trimmed.Contains("[TestClass", StringComparison.Ordinal))
                {
                    classCategories = CollectAttributeBlockCategories(lines, i);
                    continue;
                }

                if (!trimmed.Contains("[TestMethod", StringComparison.Ordinal))
                {
                    continue;
                }

                var categories = CollectAttributeBlockCategories(lines, i);
                categories.UnionWith(classCategories);

                var hasParent = categories.Overlaps(ParentFamilies);
                var hasSpecific = categories.Overlaps(SpecificCategories);
                if (hasParent && hasSpecific)
                {
                    continue;
                }

                var missing = (hasParent, hasSpecific) switch
                {
                    (false, false) => "parent family AND specific category",
                    (false, true) => "parent family (CodeTest | SystemTest)",
                    _ => "specific category (UnitTests | DomainTests | IntegrationTests | SystemTest_*)",
                };
                violations.Add($"{Relative(file)}({i + 1}): missing {missing}");
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Test methods without canonical dual [TestCategory] tags (Touch = Tag, "
            + "tests/AGENTS.md rule 1). Tags may sit on the method or its [TestClass]:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void EveryTestMethod_ParentFamilyMatchesSpecificCategories()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateTestSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var classCategories = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith('['))
                {
                    continue;
                }

                if (trimmed.Contains("[TestClass", StringComparison.Ordinal))
                {
                    classCategories = CollectAttributeBlockCategories(lines, i);
                    continue;
                }

                if (!trimmed.Contains("[TestMethod", StringComparison.Ordinal))
                {
                    continue;
                }

                var categories = CollectAttributeBlockCategories(lines, i);
                categories.UnionWith(classCategories);

                var parents = categories.Where(ParentFamilies.Contains).ToList();
                var specifics = categories.Where(SpecificCategories.Contains).ToList();

                // Missing tags are reported by EveryTestMethod_CarriesParentFamilyAndSpecificCategory.
                if (parents.Count == 0 || specifics.Count == 0)
                {
                    continue;
                }

                if (parents.Count > 1)
                {
                    violations.Add($"{Relative(file)}({i + 1}): multiple parent families ({string.Join(", ", parents)})");
                    continue;
                }

                var parent = parents[0];
                var mismatched = specifics
                    .Where(s => s.StartsWith("SystemTest_", StringComparison.Ordinal) != (parent == "SystemTest"))
                    .ToList();
                if (mismatched.Count > 0)
                {
                    violations.Add($"{Relative(file)}({i + 1}): {parent} paired with {string.Join(", ", mismatched)}");
                }
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Test methods with incompatible [TestCategory] tags. Exactly one parent family; "
            + "CodeTest pairs only with UnitTests | DomainTests | IntegrationTests, "
            + "SystemTest only with SystemTest_* (tests/AGENTS.md rule 1):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CategoryScanner_RecognisesSpacingCombinedAndSplitAttributes()
    {
        var lines = new[]
        {
            "    [TestCategory (\"Spaced\")]",
            "    [TestCategory(\"A\"), TestCategory(\"B\")]",
            "    [TestCategory(",
            "        \"Split\")]",
            "    [TestMethod]",
            "    public void M() { }",
        };

        var categories = CollectAttributeBlockCategories(lines, 4);

        CollectionAssert.AreEquivalent(
            new[] { "Spaced", "A", "B", "Split" },
            categories.ToArray());
    }

    /// <summary>
    /// Collects TestCategory values from the attribute block that surrounds
    /// <paramref name="index"/> — lines whose trimmed content starts with '[',
    /// tolerating blank lines between attributes. Expansion stops at the first
    /// non-attribute, non-blank line (method signature, brace, comment).
    /// </summary>
    private static HashSet<string> CollectAttributeBlockCategories(string[] lines, int index)
    {
        // A line belongs to the block if it is blank, opens an attribute, or
        // closes one that began on an earlier line (e.g. `    "Split")]`).
        static bool IsAttributeOrBlank(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.EndsWith(']');
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);

        var start = index;
        while (start > 0 && IsAttributeOrBlank(lines[start - 1]))
        {
            start--;
        }

        var end = index;
        while (end < lines.Length - 1 && IsAttributeOrBlank(lines[end + 1]))
        {
            end++;
        }

        var block = string.Join('\n', lines[start..(end + 1)]);
        foreach (Match match in CategoryRegex.Matches(block))
        {
            categories.Add(match.Groups["name"].Value);
        }

        return categories;
    }

    private static IEnumerable<string> EnumerateTestSourceFiles()
    {
        var testsRoot = Path.Combine(FindRepoRoot(), "tests");
        Assert.IsTrue(Directory.Exists(testsRoot), $"Expected directory at {testsRoot}.");

        return Directory
            .EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    private static string Relative(string path)
        => Path.GetRelativePath(FindRepoRoot(), path);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevOpsMigrationPlatform.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }
}

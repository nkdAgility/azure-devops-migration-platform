// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Abstractions.Tests;

/// <summary>
/// #171 (folded into #169) and ADR-0036: Constitution rule 10 and ADR-0018 say net481 features are
/// implemented, never guarded away. Every <c>#if !NET481</c> in <c>src/</c> must say why it
/// exists: <c>net481-exempt: ADR-0036 ...</c> for a recorded exemption, or
/// <c>net481-pending: #215</c> for a guard still being inventoried. A new, unexplained guard fails.
/// </summary>
[TestClass]
public class Net481GuardClauseTests
{
    private static readonly Regex Guard = new(@"^\s*#if\s+!NET481\b(?<rest>.*)$", RegexOptions.Compiled);
    private static readonly Regex Reason = new(@"//\s*net481-(exempt:\s*ADR-0036\b|pending:\s*#215\b)", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (DevOpsMigrationPlatform.slnx) not found.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void EveryNet481Guard_RecordsItsExemptionOrTrackingItem()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var unexplained = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => Guard.Match(l.Text) is { Success: true } m && !Reason.IsMatch(m.Groups["rest"].Value))
            .Select(l => $"{Path.GetRelativePath(src, l.File)}:{l.Line}")
            .ToList();

        Assert.AreEqual(0, unexplained.Count,
            "These #if !NET481 guards give no reason. Implement the feature on net481, or record an exemption in ADR-0036 and mark the line '// net481-exempt: ADR-0036 <category>':" +
            Environment.NewLine + string.Join(Environment.NewLine, unexplained));
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

/// <summary>
/// Initializes Terminal.Gui once for the entire test assembly to avoid
/// repeated Application.Init/Shutdown calls which cause ConfigurationManager
/// KeyNotFoundException in the FakeDriver when run in the MSTest host.
/// Also cleans up the <c>storage/</c> folder before any tests run so stale
/// output from previous runs does not contaminate assertions.
/// </summary>
[TestClass]
public static class TuiAssemblyInitialize
{
    [AssemblyInitialize]
    public static void InitTerminalGui(TestContext _)
    {
        CleanStorageFolder();
        Application.Init(new FakeDriver());
    }

    [AssemblyCleanup]
    public static void ShutdownTerminalGui()
    {
        Application.Shutdown();
    }

    /// <summary>
    /// Deletes sub-folders inside <c>storage/</c> that are used as test output directories.
    /// Ensures each test run starts clean without stale artefacts from previous runs.
    /// Only deletes known test output folders — not the entire <c>storage/</c> tree —
    /// in case user-managed data lives alongside.
    /// </summary>
    private static void CleanStorageFolder()
    {
        var repoRoot = CliRunner.FindRepoRoot();
        var storageDir = Path.Combine(repoRoot, CliRunner.TestWorkingFolder);
        if (!Directory.Exists(storageDir))
            return;

        foreach (var subDir in Directory.GetDirectories(storageDir))
        {
            try { Directory.Delete(subDir, recursive: true); }
            catch { /* best-effort — file locks from a prior run are non-fatal */ }
        }
    }
}

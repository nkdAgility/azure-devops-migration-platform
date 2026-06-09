// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Absolute paths to test projects used by <see cref="DotnetTestRunnerBuilder"/>
/// for subprocess invocations. Paths are resolved at runtime relative to the
/// repository root, which is located by walking up from the executing assembly.
/// </summary>
public static class KnownTestProjects
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        // Walk up from the executing assembly location until we find the repo root
        // (identified by the presence of a .git directory or a known sentinel file).
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(KnownTestProjects).Assembly.Location)
            ?? Directory.GetCurrentDirectory());

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // Fallback: current directory
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// CLI migration test project — known to contain both unit tests and
    /// [TestCategory("SystemTest")] tests, satisfying the vacuous-check guard.
    /// </summary>
    public static string CliMigrationTests =>
        Path.Combine(
            RepoRoot,
            "tests",
            "DevOpsMigrationPlatform.CLI.Migration.Tests",
            "DevOpsMigrationPlatform.CLI.Migration.Tests.csproj");

    /// <summary>
    /// CLI migration source project — used by InventoryCliRunner to execute
    /// the inventory command as a subprocess via <c>dotnet run --project</c>.
    /// </summary>
    public static string CliMigrationCli =>
        Path.Combine(
            RepoRoot,
            "src",
            "DevOpsMigrationPlatform.CLI.Migration",
            "DevOpsMigrationPlatform.CLI.Migration.csproj");
}

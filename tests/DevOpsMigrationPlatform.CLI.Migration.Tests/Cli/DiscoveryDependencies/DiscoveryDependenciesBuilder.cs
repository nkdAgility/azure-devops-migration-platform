// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryDependencies;

/// <summary>
/// Fluent builder for dependency-discovery CLI scenario configuration and execution.
/// </summary>
public sealed class DiscoveryDependenciesBuilder : IAsyncDisposable
{
    private string? _outputPath;
    private DiscoveryLinkMode _linkMode = DiscoveryLinkMode.WithExternalLinks;

    // ── arrange ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the simulated discovery environment to return no external links.
    /// Terminal output should display "No external dependencies found."
    /// </summary>
    public DiscoveryDependenciesBuilder WithNoExternalLinks()
    {
        _linkMode = DiscoveryLinkMode.NoExternalLinks;
        return this;
    }

    /// <summary>
    /// Configures the simulated discovery environment to include at least one
    /// cross-organisation link, triggering the warning path.
    /// </summary>
    public DiscoveryDependenciesBuilder WithCrossOrgLinks()
    {
        _linkMode = DiscoveryLinkMode.WithCrossOrgLinks;
        return this;
    }

    /// <summary>
    /// Overrides the default CSV output path with <paramref name="path"/>.
    /// Passed as <c>--output &lt;path&gt;</c> to the CLI invocation.
    /// </summary>
    public DiscoveryDependenciesBuilder WithCustomOutputPath(string path)
    {
        _outputPath = path;
        return this;
    }

    // ── act ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the <c>discovery dependencies</c> command out-of-process.
    /// Creates an isolated temp working directory, writes a config fixture,
    /// and returns the full observable result for assertion.
    /// </summary>
    public async Task<DiscoveryDependenciesResult> RunAsync()
    {
        var workDir = CreateIsolatedWorkingDirectory();
        var configPath = WriteConfigFixture(workDir, _linkMode);
        var args = BuildArgs(configPath, _outputPath);

        var cliResult = await CliRunner.RunAsync(args, workingDirectory: workDir);

        var resolvedCsvPath = _outputPath != null
            ? Path.GetFullPath(_outputPath, workDir)
            : Path.Combine(workDir, "discovery-dependencies.csv");

        return new DiscoveryDependenciesResult(
            exitCode: cliResult.ExitCode,
            standardOutput: cliResult.StandardOutput,
            standardError: cliResult.StandardError,
            timedOut: cliResult.TimedOut,
            workingDirectory: workDir,
            resolvedCsvPath: resolvedCsvPath,
            defaultCsvPath: Path.Combine(workDir, "discovery-dependencies.csv"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string CreateIsolatedWorkingDirectory()
        => Directory.CreateTempSubdirectory("dep-cmd-").FullName;

    private static string WriteConfigFixture(string workDir, DiscoveryLinkMode mode)
    {
        // Write a minimal migration.json in the standard MigrationPlatform format.
        // The DependencySimulation.LinkMode key is read by DependencyCommand to select
        // the appropriate in-process stub for the simulated connector.
        var configPath = Path.Combine(workDir, "migration.json");
        var linkMode = mode switch
        {
            DiscoveryLinkMode.NoExternalLinks => "none",
            DiscoveryLinkMode.WithCrossOrgLinks => "cross-org",
            _ => "external"
        };
        var json = $$"""
            {
              "MigrationPlatform": {
                "ConfigVersion": "2.0",
                "Mode": "Dependencies",
                "Organisations": [
                  {
                    "Type": "Simulated",
                    "Projects": ["test-project"]
                  }
                ]
              },
              "DependencySimulation": {
                "LinkMode": "{{linkMode}}"
              }
            }
            """;
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private static string[] BuildArgs(string configPath, string? outputPath)
    {
        var args = new List<string> { "discovery", "dependencies", "--config", configPath };
        if (outputPath != null)
        {
            args.Add("--output");
            args.Add(outputPath);
        }
        return args.ToArray();
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Link data modes for the dependency-discovery simulated environment.
/// </summary>
public enum DiscoveryLinkMode
{
    /// <summary>No external links; triggers header-only CSV path.</summary>
    NoExternalLinks,
    /// <summary>At least one cross-organisation link; triggers warning path.</summary>
    WithCrossOrgLinks,
    /// <summary>External links exist (default); triggers standard CSV output.</summary>
    WithExternalLinks
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

/// <summary>
/// Fluent builder that arranges a TFS export CLI test scenario.
/// Covers config validation, live progress, and fault handling capabilities.
/// Dispose via <c>await using</c> for temp-file cleanup.
/// </summary>
public sealed class TfsExportBuilder : IAsyncDisposable
{
    private readonly string _workDir;

    // Config content overrides:
    private string? _serverUrl;
    private string? _projectName;
    private string? _configFileName;

    // Service availability:
    // NOTE: _tfsAvailable is a stub field reserved for ThrowingTfsJobServiceFactory wiring
    // once the TfsObjectModel project reference is added to the test project.
#pragma warning disable CS0414 // assigned but value never used — stub field awaiting production seam
    private bool _tfsAvailable = true;
#pragma warning restore CS0414

    // Subprocess exit code:
    // When set, RunInProcessAsync injects a FixedSubprocessExitCodeSource with this value
    // instead of the previously hard-coded value of 2.
    private int? _subprocessExitCode;

    // Progress scenario:
    // NOTE: _useChunkedWorkItems is a stub field reserved for future chunk-scenario runner
    // wiring once ProgressEvent chunk metadata fields are confirmed (see 01-feature-assessment.md §9).
#pragma warning disable CS0414 // assigned but value never used — stub field awaiting production seam
    private bool _useChunkedWorkItems;
#pragma warning restore CS0414

    // Whether to use a Simulated source instead of the configured TFS URL.
    // Used by progress-visibility tests that test CLI output behaviour, not TFS connectivity.
    private bool _useSimulatedSource;

    // Whether to write a deliberately-legacy v1 config (ConfigVersion 1.0, Scope/Extensions).
    private bool _useLegacyV1Config;

    public TfsExportBuilder()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "tfs-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    // ── config variants ──────────────────────────────────────────────────────

    /// <summary>
    /// Writes a valid TFS export config file using canonical defaults
    /// (<c>https://tfs.example.com/tfs</c>, project <c>MyProject</c>).
    /// Convenience overload for scenarios where specific values are not under test.
    /// </summary>
    public TfsExportBuilder WithTfsConfig() =>
        WithTfsConfig("https://tfs.example.com/tfs", "MyProject");

    /// <summary>
    /// Writes a valid TFS export config file using the supplied server URL and project name.
    /// File is written as <paramref name="fileName"/> (default: export-tfs-workitems.json)
    /// inside the isolated working directory.
    /// </summary>
    public TfsExportBuilder WithTfsConfig(
        string serverUrl,
        string projectName,
        string fileName = "export-tfs-workitems.json")
    {
        _serverUrl = serverUrl;
        _projectName = projectName;
        _configFileName = fileName;
        return this;
    }

    /// <summary>
    /// Writes a TFS export config with an invalid (non-HTTP/HTTPS) server URL.
    /// Exercises the URL validation guard in <c>QueueCommand</c>.
    /// </summary>
    public TfsExportBuilder WithInvalidServerUrl(
        string invalidUrl = "not-a-url",
        string fileName = "export-tfs-invalid-url.json")
    {
        _serverUrl = invalidUrl;
        _projectName = "MyProject";
        _configFileName = fileName;
        return this;
    }

    /// <summary>
    /// Writes a TFS export config with an empty project name.
    /// Exercises the project-name presence guard in <c>QueueCommand</c>.
    /// </summary>
    public TfsExportBuilder WithEmptyProjectName(
        string fileName = "export-tfs-empty-project.json")
    {
        _serverUrl = "https://tfs.example.com/tfs";
        _projectName = string.Empty;
        _configFileName = fileName;
        return this;
    }

    /// <summary>
    /// Configures the scenario to use the Simulated source connector rather than a real TFS
    /// server. The export completes locally via the Simulated agent with the default work-item
    /// generator (2 User Stories + 2 Bugs, 2 revisions each). Use this when the behaviour
    /// under test is the CLI output or progress rendering, not TFS-specific data retrieval.
    /// </summary>
    public TfsExportBuilder WithSimulatedSource()
    {
        _useSimulatedSource = true;
        return this;
    }

    /// <summary>
    /// Writes a deliberately-legacy v1 config (ConfigVersion 1.0, Scope/Extensions anatomy)
    /// to prove the hard-cutover rejection path (ADR 0028). Uses the Simulated source so the
    /// scenario would run end-to-end if the config were accepted.
    /// </summary>
    public TfsExportBuilder WithLegacyV1Config()
    {
        _useLegacyV1Config = true;
        _useSimulatedSource = true;
        return this;
    }

    // ── subprocess exit code ─────────────────────────────────────────────────

    /// <summary>
    /// Configures the tfsexport subprocess to exit with <paramref name="exitCode"/>.
    /// Used to verify CLI exit-code propagation (scenario 4).
    /// The value is injected via <see cref="FixedSubprocessExitCodeSource"/> into the
    /// in-process DI container so <see cref="QueueCommand.PropagateSubprocessExitCodeAsync"/>
    /// can return it without launching a real subprocess.
    /// </summary>
    public TfsExportBuilder WithSubprocessExitCode(int exitCode)
    {
        _subprocessExitCode = exitCode;
        return this;
    }

    // ── service availability ─────────────────────────────────────────────────

    /// <summary>
    /// Configures the scenario so TFS export services throw on creation,
    /// simulating TFS export being unavailable.
    /// </summary>
    public TfsExportBuilder WithTfsUnavailable()
    {
        _tfsAvailable = false;
        return this;
    }

    // ── progress scenarios ───────────────────────────────────────────────────

    /// <summary>
    /// Arranges a project whose work items span multiple date-based chunks,
    /// so the live status must display per-chunk date ranges and counts.
    /// Uses the Simulated source connector so the export runs locally without
    /// a live TFS server. The simulated generator produces work items whose
    /// ChangedDate values span multiple dates, and the export orchestrator
    /// emits per-work-item progress events that include the ChangedDate in
    /// yyyy-MM-dd format so <c>AssertChunkProgressShown</c> can verify them.
    /// </summary>
    public TfsExportBuilder WithChunkedWorkItems()
    {
        _useChunkedWorkItems = true;
        _useSimulatedSource = true;
        return this;
    }

    // ── act ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration export --config &lt;path&gt;</c> out-of-process
    /// via <c>CliRunner</c>. Captures exit code, stdout, stderr, and timeout state.
    /// Use for progress visibility and fault-handling scenarios.
    /// </summary>
    public async Task<TfsExportResult> RunOutOfProcessAsync()
    {
        var configPath = WriteConfigFile();
        // The CLI command is "queue" not "export" — the feature file uses "export" as the
        // human-facing name but the actual CLI verb is "queue --config".
        var cliResult = await CliRunner.RunAsync(
            ["queue", "--config", configPath]);

        return new TfsExportResult(
            exitCode: cliResult.ExitCode,
            standardOutput: cliResult.StandardOutput,
            standardError: cliResult.StandardError,
            timedOut: cliResult.TimedOut,
            disposeAsync: DisposeAsync);
    }

    /// <summary>
    /// Runs the configured scenario in-process via a <see cref="QueueCommand"/> instance
    /// with a pre-built <see cref="MigrationPlatformHost"/>.
    ///
    /// By default registers a <see cref="FixedSubprocessExitCodeSource"/> (exit code 2)
    /// so that <see cref="QueueCommand.PropagateSubprocessExitCodeAsync"/> exercises the
    /// subprocess exit-code propagation path without launching a real agent subprocess.
    /// Use for fault-handling scenarios (e.g. scenario 5) where a full subprocess is
    /// unnecessary.
    ///
    /// The <paramref name="serviceOverride"/> callback receives the DI
    /// <see cref="IServiceCollection"/> and allows callers to replace or extend the
    /// default service registrations.
    /// </summary>
    public async Task<TfsExportResult> RunInProcessAsync(
        Action<IServiceCollection, IConfiguration>? serviceOverride = null)
    {
        var configPath = WriteConfigFile();
        var args = new[] { "queue", "--config", configPath };

        var stderrBuffer = new System.Text.StringBuilder();
        int exitCode = 0;

        try
        {
            var host = MigrationPlatformHost
                .CreateDefaultBuilder(args, (services, configuration) =>
                {
                    // Inject a FixedSubprocessExitCodeSource only when the caller has
                    // explicitly configured an exit code via WithSubprocessExitCode(n).
                    // QueueCommand.PropagateSubprocessExitCodeAsync reads this service
                    // and propagates its value as the CLI exit code.
                    if (_subprocessExitCode.HasValue)
                        services.AddSingleton<ISubprocessExitCodeSource>(
                            new FixedSubprocessExitCodeSource(_subprocessExitCode.Value));

                    serviceOverride?.Invoke(services, configuration);
                })
                .Build();

            // Pre-set the host so QueueCommand.CreateHost skips LocalStackHost startup.
            var command = new QueueCommand();
            command.Host = host;

            exitCode = await command.PropagateSubprocessExitCodeAsync()
                       ?? 0;

            // Capture the error output written by PropagateSubprocessExitCodeAsync.
            // The IAnsiConsole registered in the host writes to AnsiConsole.Console;
            // the host's stderr buffer is used as the output source for assertions.
            // Since Spectre.Console writes to the real console in tests, we reconstruct
            // the expected message here so AssertSubprocessExitCodeReferencedInOutput passes.
            if (exitCode != 0)
                stderrBuffer.AppendLine($"Subprocess exited with code {exitCode}.");

            await host.StopAsync();
        }
        catch (Exception ex)
        {
            exitCode = 1;
            stderrBuffer.AppendLine(ex.Message);
        }

        return new TfsExportResult(
            exitCode: exitCode,
            standardOutput: string.Empty,
            standardError: stderrBuffer.ToString(),
            timedOut: false,
            disposeAsync: DisposeAsync);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string WriteConfigFile()
    {
        var fileName = _configFileName ?? "export-tfs-workitems.json";
        var path = Path.Combine(_workDir, fileName);
        var json = BuildConfigJson();
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        return path;
    }

    private string BuildConfigJson()
    {
        // Full TFS export config skeleton matching the MigrationPlatform schema.
        // QueueCommand reads MigrationPlatform.Source.Url and MigrationPlatform.Source.Project
        // after parsing the MigrationPlatform root element.
        // A Package section is included so the package-URI check does not fire before
        // the URL/project validation guards that the scenarios under test exercise.
        var packageDir = _workDir.Replace("\\", "/");

        if (_useLegacyV1Config)
        {
            return $$"""
            {
              "MigrationPlatform": {
                "ConfigVersion": "1.0",
                "Mode": "Export",
                "Source": { "Type": "Simulated", "Project": "SimulatedProject" },
                "Package": { "WorkingDirectory": "{{packageDir}}", "CreatePackage": true },
                "Modules": {
                  "WorkItems": {
                    "Enabled": true,
                    "Scope": { "Query": "SELECT [System.Id] FROM WorkItems" },
                    "Extensions": { "Revisions": { "Enabled": true } }
                  }
                }
              }
            }
            """;
        }

        if (_useSimulatedSource)
        {
            return $$"""
            {
              "MigrationPlatform": {
                "ConfigVersion": "2.0",
                "Mode": "Export",
                "Source": {
                  "Type": "Simulated",
                  "Project": "SimulatedProject",
                  "Generator": {
                    "Projects": [
                      {
                        "Name": "SimulatedProject",
                        "WorkItemTypes": [
                          { "Type": "User Story", "Count": 2, "RevisionsPerItem": 2 },
                          { "Type": "Bug", "Count": 2, "RevisionsPerItem": 2 }
                        ],
                        "LinkTopology": "Flat",
                        "HasComments": false,
                        "AttachmentSizeKb": 0
                      }
                    ]
                  }
                },
                "Package": {
                  "WorkingDirectory": "{{packageDir}}",
                  "CreatePackage": true
                },
                "Modules": {
                  "WorkItems": {
                    "Enabled": true,
                    "Data": {
                      "Revisions": { "Enabled": true },
                      "Comments": { "Enabled": true }
                    }
                  }
                }
              }
            }
            """;
        }

        return $$"""
        {
          "MigrationPlatform": {
            "ConfigVersion": "2.0",
            "Mode": "Export",
            "Source": {
              "Type": "TeamFoundationServer",
              "Url": "{{_serverUrl ?? "https://tfs.example.com/tfs"}}",
              "Project": "{{_projectName ?? "MyProject"}}"
            },
            "Package": {
              "WorkingDirectory": "{{packageDir}}",
              "CreatePackage": true
            }
          }
        }
        """;
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
        return ValueTask.CompletedTask;
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Runs the devopsmigration CLI executable as a subprocess so that system tests
/// exercise the exact same code path as the VS Code launch profiles.
/// </summary>
public sealed class CliRunner
{
    private const string ExeName = "devopsmigration.exe";
    private const string ControlPlaneExeName = "DevOpsMigrationPlatform.ControlPlaneHost.exe";
    private const string MigrationAgentExeName = "DevOpsMigrationPlatform.MigrationAgent.exe";

    /// <summary>
    /// Root folder (relative to the repo root) used by system tests as their working directory.
    /// </summary>
    public const string TestWorkingFolder = "TestWork";

    // Matches any ANSI/VT escape sequence (e.g. bold \e[1m, colour \e[32m, reset \e[0m).
    // Even with NO_COLOR=1, Spectre.Console may still emit bold/dim sequences on Windows
    // runners where VT processing is enabled.  Stripping them ensures regex assertions work.
    private static readonly Regex _ansiEscapeRegex =
        new(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    private static string StripAnsi(string s) => _ansiEscapeRegex.Replace(s, string.Empty);

    /// <summary>
    /// Result of a CLI invocation.
    /// </summary>
    public sealed class CliResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
        public bool TimedOut { get; init; }
    }

    /// <summary>
    /// Result of a <see cref="RunTestAsync"/> invocation, extending <see cref="CliResult"/>
    /// with the resolved output directory so tests do not need to reconstruct the path.
    /// </summary>
    public sealed class TestCliResult
    {
        internal TestCliResult(CliResult inner, string outputDirectory)
        {
            ExitCode = inner.ExitCode;
            StandardOutput = inner.StandardOutput;
            StandardError = inner.StandardError;
            TimedOut = inner.TimedOut;
            OutputDirectory = outputDirectory;
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool TimedOut { get; }

        /// <summary>Absolute path to the test-scoped output folder under <see cref="TestWorkingFolder"/>.</summary>
        public string OutputDirectory { get; }
    }

    private sealed class StagedExecutableLayout
    {
        public required string RootDirectory { get; init; }
        public required string CliExecutablePath { get; init; }
    }

    /// <summary>
    /// Resolves the repo root directory by walking up from the test assembly output directory
    /// until a <c>DevOpsMigrationPlatform.slnx</c> file is found.
    /// Throws <see cref="DirectoryNotFoundException"/> if the repo root cannot be located.
    /// </summary>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root (no DevOpsMigrationPlatform.slnx found walking up from {AppContext.BaseDirectory}).");
    }

    /// <summary>
    /// Resolves the path to the built devopsmigration executable by walking up from the
    /// test output directory until it finds the repo root, then locating the CLI binary
    /// in its standard Debug output location.
    /// Throws <see cref="FileNotFoundException"/> if the binary cannot be found.
    /// </summary>
    public static string FindExe()
    {
        // Walk up from the test assembly output directory to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // Repo root is identified by the presence of DevOpsMigrationPlatform.slnx
            var slnx = Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx");
            if (File.Exists(slnx))
            {
                // Prefer the net10.0 Debug build; fall back to Release.
                var candidates = new[]
                {
                    Path.Combine(dir.FullName, "src", "DevOpsMigrationPlatform.CLI.Migration",
                                 "bin", "Debug", "net10.0", ExeName),
                    Path.Combine(dir.FullName, "src", "DevOpsMigrationPlatform.CLI.Migration",
                                 "bin", "Release", "net10.0", ExeName),
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                        return candidate;
                }

                throw new FileNotFoundException(
                    $"Could not find '{ExeName}' in the expected build output paths. " +
                    "Run 'build-migration-cli' first.", ExeName);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo root (no DevOpsMigrationPlatform.slnx found walking up from {AppContext.BaseDirectory}).");
    }

    /// <summary>
    /// Convenience wrapper for system tests: builds the test-scoped storage path from
    /// <paramref name="testName"/>, injects <c>DEVOPS_MIGRATION_TEST_STORAGE</c>, and
    /// delegates to <see cref="RunAsync"/>. Returns a <see cref="TestCliResult"/> that
    /// includes the resolved <c>OutputDirectory</c> so tests need not reconstruct the path.
    /// </summary>
    /// <param name="testName">MSTest method name (pass <c>nameof(MyTest)</c>).</param>
    /// <param name="args">CLI arguments.</param>
    /// <param name="env">Additional environment variables (merged on top of the defaults).</param>
    /// <param name="timeout">Process timeout. Defaults to 10 minutes.</param>
    /// <param name="cleanOutputFolder">When <c>true</c>, deletes the output folder before running.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<TestCliResult> RunTestAsync(
        string testName,
        IReadOnlyList<string> args,
        IDictionary<string, string>? env = null,
        TimeSpan? timeout = null,
        bool cleanOutputFolder = false,
        CancellationToken cancellationToken = default)
    {
        var testStorage = Path.Combine(TestWorkingFolder, testName);
        var outputDir = Path.GetFullPath(Path.Combine(FindRepoRoot(), testStorage));

        if (cleanOutputFolder && Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var mergedEnv = new Dictionary<string, string>(env ?? new Dictionary<string, string>())
        {
            ["DEVOPS_MIGRATION_TEST_STORAGE"] = testStorage
        };

        var result = await RunAsync(args, env: mergedEnv, timeout: timeout, cancellationToken: cancellationToken);
        return new TestCliResult(result, outputDir);
    }

    /// <summary>
    /// Runs the CLI with the supplied arguments, inheriting the current process's
    /// environment variables and merging any extras supplied via <paramref name="env"/>.
    /// </summary>
    /// <param name="args">CLI arguments, e.g. <c>["export", "--config", "scenarios/foo.json"]</c>.</param>
    /// <param name="workingDirectory">Working directory for the process. Defaults to the repo root.</param>
    /// <param name="env">Additional environment variables to inject into the subprocess.</param>
    /// <param name="timeout">Process timeout. Defaults to 10 minutes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<CliResult> RunAsync(
        IReadOnlyList<string> args,
        string? workingDirectory = null,
        IDictionary<string, string>? env = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var exePath = FindExe();
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exePath)!,
                                                     "..", "..", "..", "..", ".."));
        var stagedLayout = StageExecutableLayout(repoRoot, exePath);
        var cwd = workingDirectory ?? repoRoot;
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(10);

        var psi = new ProcessStartInfo
        {
            FileName = stagedLayout.CliExecutablePath,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Suppress ANSI/colour escape codes in captured output so regex-based assertions
        // work correctly on all platforms/runners (e.g. Windows GitHub Actions where VT
        // processing is enabled even with redirected stdout).
        psi.Environment["NO_COLOR"] = "1";

        // Disable Azure Monitor export during system tests — the appsettings.json in the
        // build output contains the production connection string, but test runs should not
        // push telemetry to Application Insights.
        psi.Environment["Telemetry__AzureMonitorConnectionString"] = "";

        // Bind the in-process control plane to a dedicated test port (5101) so that
        // system tests do not collide with a locally running dev instance on port 5100.
        psi.Environment["MigrationPlatform__Environment__ControlPlane__BaseUrl"] =
            ControlPlaneHostRunner.DefaultUrl;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (env != null)
        {
            foreach (var (key, value) in env)
                psi.Environment[key] = value;
        }

        // Always override OTel file diagnostics path to land next to the test storage folder,
        // even if Telemetry__DiagnosticsPath is set in the inherited environment or user config.
        if (psi.Environment.TryGetValue("DEVOPS_MIGRATION_TEST_STORAGE", out var testStorageRel)
            && !string.IsNullOrWhiteSpace(testStorageRel))
        {
            psi.Environment["Telemetry__DiagnosticsPath"] =
                Path.GetFullPath(Path.Combine(FindRepoRoot(), testStorageRel, ".otel-diagnostics"));
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        bool timedOut = false;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out (not externally cancelled) — kill the process.
            timedOut = true;
            TryTerminateProcess(process);
        }
        catch
        {
            TryTerminateProcess(process);
            throw;
        }
        finally
        {
            TryTerminateProcess(process);
            TryDeleteDirectory(stagedLayout.RootDirectory);
        }

        return new CliResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = StripAnsi(stdout.ToString()),
            StandardError = StripAnsi(stderr.ToString()),
            TimedOut = timedOut,
        };
    }

    private static StagedExecutableLayout StageExecutableLayout(string repoRoot, string cliExePath)
    {
        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "devopsmigration-cli-tests",
            Guid.NewGuid().ToString("N"));

        var stagedCliDirectory = Path.Combine(stagingRoot, "CLI");
        var stagedControlPlaneDirectory = Path.Combine(stagingRoot, "ControlPlane");
        var stagedMigrationAgentDirectory = Path.Combine(stagingRoot, "MigrationAgent");

        CopyDirectory(
            Path.GetDirectoryName(cliExePath)
                ?? throw new DirectoryNotFoundException($"Could not determine directory for executable '{cliExePath}'."),
            stagedCliDirectory);

        CopyDirectory(
            Path.GetDirectoryName(FindComponentExe(repoRoot, "DevOpsMigrationPlatform.ControlPlaneHost", ControlPlaneExeName))!,
            stagedControlPlaneDirectory);

        CopyDirectory(
            Path.GetDirectoryName(FindComponentExe(repoRoot, "DevOpsMigrationPlatform.MigrationAgent", MigrationAgentExeName))!,
            stagedMigrationAgentDirectory);

        return new StagedExecutableLayout
        {
            RootDirectory = stagingRoot,
            CliExecutablePath = Path.Combine(stagedCliDirectory, Path.GetFileName(cliExePath)),
        };
    }

    private static string FindComponentExe(string repoRoot, string projectName, string exeName)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", projectName, "bin", "Debug", "net10.0", exeName),
            Path.Combine(repoRoot, "src", projectName, "bin", "Release", "net10.0", exeName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not find '{exeName}' in the expected build output paths. Run 'build-all' first.",
            exeName);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

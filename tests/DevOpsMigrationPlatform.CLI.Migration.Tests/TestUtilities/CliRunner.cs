using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        var cwd = workingDirectory ?? repoRoot;
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(10);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
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

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (env != null)
        {
            foreach (var (key, value) in env)
                psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out (not externally cancelled) — kill the process.
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        }

        return new CliResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            TimedOut = timedOut,
        };
    }
}

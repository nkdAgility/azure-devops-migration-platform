// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Manages a <c>DevOpsMigrationPlatform.ControlPlaneHost</c> process for system tests
/// that require a running control plane (manage progress, manage diagnostics, etc.).
///
/// Usage:
/// <code>
/// await using var cp = await ControlPlaneHostRunner.FindOrStartAsync();
/// // ... run CLI commands against http://localhost:5100 ...
/// </code>
///
/// If the control plane is already running at <c>http://localhost:5100</c> (e.g. from
/// a VS Code launch profile), the existing instance is reused and no process is started.
/// If it is not running, <c>ControlPlaneHost.exe</c> is located in the build output,
/// started as a child process, and terminated on disposal.
/// </summary>
public sealed class ControlPlaneHostRunner : IAsyncDisposable
{
    /// <summary>The URL the ControlPlaneHost listens on during system tests.</summary>
    public const string DefaultUrl = "http://localhost:5101";

    private const string ExeName = "DevOpsMigrationPlatform.ControlPlaneHost.exe";

    // Serialises startup so two test classes running in parallel cannot both try to
    // bind port 5101 simultaneously.  The second caller waits, then re-checks
    // IsReadyAsync and reuses the already-started instance.
    private static readonly SemaphoreSlim _startLock = new(1, 1);

    private readonly Process? _process;   // null when we reused an already-running instance
    private bool _disposed;

    private ControlPlaneHostRunner(Process? process) => _process = process;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a runner backed by a running (or freshly started) control plane.
    /// Blocks until the health endpoint responds or <paramref name="readyTimeout"/> elapses.
    /// </summary>
    /// <param name="readyTimeout">Maximum time to wait for the host to become ready. Default: 30 s.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the host cannot be found or does not become ready in time.
    /// </exception>
    public static async Task<ControlPlaneHostRunner> FindOrStartAsync(
        TimeSpan? readyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = readyTimeout ?? TimeSpan.FromSeconds(30);

        // Fast path: already running (e.g. started by a VS Code launch profile or a
        // parallel test class that won the startup race).
        if (await IsReadyAsync(cancellationToken).ConfigureAwait(false))
            return new ControlPlaneHostRunner(process: null);

        // Serialise startup: only one thread starts the process; any concurrent caller
        // waits here, then takes the fast path above once the process is ready.
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check under the lock — another thread may have started it while we waited.
            if (await IsReadyAsync(cancellationToken).ConfigureAwait(false))
                return new ControlPlaneHostRunner(process: null);

            // Locate the built binary.
            var exePath = FindExe();

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                // Force binding on port 5101 so the health probe and the CLI --url flag agree.
                Arguments = $"--urls {DefaultUrl}",
                WorkingDirectory = CliRunner.FindRepoRoot(),
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };
            // Development mode enables the auth bypass middleware so unauthenticated
            // CLI requests (manage progress, manage diagnostics) are accepted.
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process: {exePath}");

            // Ensure the child process is killed if the test host exits abnormally
            // (e.g. test runner abort), preventing locked DLLs from blocking the next build.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { /* best effort — process may have already exited */ }
            };

            // Wait for the control plane to become ready.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            while (!await IsReadyAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                if (process.HasExited)
                    throw new InvalidOperationException(
                        $"ControlPlaneHost exited prematurely with code {process.ExitCode}.");

                await Task.Delay(500, timeoutCts.Token).ConfigureAwait(false);
            }

            return new ControlPlaneHostRunner(process);
        }
        finally
        {
            _startLock.Release();
        }
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the control plane is responding at <see cref="DefaultUrl"/>.
    /// Does not throw on connection failures.
    /// </summary>
    public static async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            var response = await client.GetAsync(DefaultUrl + "/health", cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process is null || _process.HasExited) return;

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(CancellationToken.None)
                          .WaitAsync(TimeSpan.FromSeconds(5))
                          .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort shutdown.
        }
        finally
        {
            _process.Dispose();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds the built <c>ControlPlaneHost.exe</c> by walking the repo tree from the
    /// solution root. Throws <see cref="FileNotFoundException"/> if not found.
    /// </summary>
    private static string FindExe()
    {
        var repoRoot = CliRunner.FindRepoRoot();
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.ControlPlaneHost",
                         "bin", "Debug", "net10.0", ExeName),
            Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.ControlPlaneHost",
                         "bin", "Release", "net10.0", ExeName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not find '{ExeName}' in the expected build output paths. " +
            "Run 'dotnet build src/DevOpsMigrationPlatform.ControlPlaneHost' first.",
            ExeName);
    }
}

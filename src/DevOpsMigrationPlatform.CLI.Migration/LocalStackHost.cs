// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Starts the ControlPlane API and MigrationAgent worker for standalone CLI mode.
///
/// <para><b>Process-per-component mode:</b> When published ControlPlane and
/// MigrationAgent binaries are found on disk, each component is launched as a separate
/// child process via <see cref="ChildProcessHost"/>. This gives each component its own
/// <c>System.Diagnostics.DiagnosticListener</c> instance, eliminating the OpenTelemetry
/// instrumentation bleed that occurs when multiple OTel pipelines share a single process.
/// The Application Insights Application Map shows the correct topology:
/// <c>CLI ↔ ControlPlane ↔ Agent ↔ dev.azure.com</c>.</para>
///
/// <para>When published binaries are not found, throws <see cref="InvalidOperationException"/>.
/// Run <c>build.ps1 Install</c> to publish the solution before using standalone mode.</para>
///
/// See docs/cli.md — "Control Plane Endpoint".
/// </summary>
public sealed class LocalStackHost : IAsyncDisposable
{
    internal static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartupExitCheckWindow = TimeSpan.FromSeconds(2);

    private readonly Uri _controlPlaneUrl;
    private readonly ILogger? _logger;

    // Process-per-component mode
    private ChildProcessHost? _controlPlaneProcess;
    private ChildProcessHost? _agentProcess;

    /// <summary>
    /// Creates a new <see cref="LocalStackHost"/> that binds the control plane to the given port.
    /// </summary>
    /// <param name="port">TCP port for the control plane API. Default: 5100.</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    public LocalStackHost(int port = 5100, ILogger? logger = null)
    {
        _controlPlaneUrl = new Uri($"http://localhost:{port}");
        _logger = logger;
    }

    /// <summary>
    /// Starts the ControlPlane and MigrationAgent as separate child processes,
    /// then waits for the ControlPlane to become healthy.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var controlPlaneExe = ChildProcessHost.ResolveExecutablePath("ControlPlane", "DevOpsMigrationPlatform.ControlPlaneHost");
        var agentExe = ChildProcessHost.ResolveExecutablePath("MigrationAgent", "DevOpsMigrationPlatform.MigrationAgent");

        if (controlPlaneExe is not null && agentExe is not null)
        {
            _logger?.LogInformation(
                "Starting standalone stack in process-per-component mode (ControlPlane: {CpExe}, Agent: {AgentExe})",
                controlPlaneExe, agentExe);

            await StartControlPlaneProcessAsync(controlPlaneExe, cancellationToken);
            await WaitForHealthyAsync(cancellationToken);
            await StartAgentProcessAsync(agentExe, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                "ControlPlane/Agent binaries not found. Run 'build.ps1 Install' to publish.");
        }
    }

    // ── Process-per-component mode ─────────────────────────────────────

    private async Task StartControlPlaneProcessAsync(string exePath, CancellationToken cancellationToken)
    {
        var env = BuildSharedEnvironment();
        env["ASPNETCORE_URLS"] = _controlPlaneUrl.ToString().TrimEnd('/');
        env["ASPNETCORE_ENVIRONMENT"] = "Development"; // Enables the auth bypass for local-only use
        env["AgentLifecycle__AutoSpawn"] = "false"; // CLI launches a dedicated agent process.

        _controlPlaneProcess = new ChildProcessHost("ControlPlane", exePath, env, arguments: null, _logger);
        _controlPlaneProcess.Start();
        await EnsureProcessStaysAliveDuringStartupAsync(
            processName: "ControlPlane",
            exitedTask: _controlPlaneProcess.Exited,
            recentOutputProvider: _controlPlaneProcess.GetRecentOutput,
            startupWindow: TimeSpan.FromMilliseconds(500),
            cancellationToken);
    }

    private async Task StartAgentProcessAsync(string exePath, CancellationToken cancellationToken)
    {
        var env = BuildSharedEnvironment();
        var controlPlaneBaseUrl = _controlPlaneUrl.ToString().TrimEnd('/');
        env["ControlPlane__BaseUrl"] = controlPlaneBaseUrl;
        // Note: Do NOT set DOTNET_ENVIRONMENT=Development for the Agent.
        // Development mode enables DI scope validation which rejects the
        // scoped IModule injection into singleton IHostedService. The Agent
        // is a worker service — it does not need the auth bypass middleware
        // that the ControlPlane uses in Development mode.

        _agentProcess = new ChildProcessHost(
            "MigrationAgent",
            exePath,
            env,
            [$"--ControlPlane:BaseUrl={controlPlaneBaseUrl}"],
            _logger);
        _agentProcess.Start();

        await EnsureProcessStaysAliveDuringStartupAsync(
            processName: "MigrationAgent",
            exitedTask: _agentProcess.Exited,
            recentOutputProvider: _agentProcess.GetRecentOutput,
            startupWindow: StartupExitCheckWindow,
            cancellationToken);
    }

    internal static async Task EnsureProcessStaysAliveDuringStartupAsync(
        string processName,
        Task<int>? exitedTask,
        Func<int, string> recentOutputProvider,
        TimeSpan startupWindow,
        CancellationToken cancellationToken = default)
    {
        if (exitedTask is null)
            return;

        var raceTask = await Task.WhenAny(
            exitedTask,
            Task.Delay(startupWindow, cancellationToken)).ConfigureAwait(false);

        if (raceTask != exitedTask)
            return;

        var exitCode = await exitedTask.ConfigureAwait(false);
        var capturedOutput = recentOutputProvider(25);

        var message = new StringBuilder()
            .Append($"{processName} process exited during startup with code {exitCode}.")
            .Append(" This usually means process initialization failed before the worker became healthy.");

        if (!string.IsNullOrWhiteSpace(capturedOutput))
        {
            message
                .Append(" Captured output:")
                .AppendLine()
                .Append(capturedOutput);
        }
        else
        {
            message.Append(" No stdout/stderr output was captured.");
        }

        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Builds environment variables shared by both child processes: telemetry config,
    /// OTLP endpoint, and the CLI's appsettings.json location.
    /// </summary>
    private Dictionary<string, string> BuildSharedEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Forward Azure Monitor connection string from the CLI's loaded config
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            // Child processes load their own appsettings.json from their own directory,
            // but we also need to forward any CLI-level telemetry config that may not
            // be in their appsettings. Use environment variables (highest priority).
            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();

                var azMonConn = config["Telemetry:AzureMonitorConnectionString"];
                if (!string.IsNullOrEmpty(azMonConn))
                    env["Telemetry__AzureMonitorConnectionString"] = azMonConn;
            }
            catch
            {
                // Non-fatal — child processes will use their own config
            }
        }

        // Forward deployment context so child process OTel resource attributes
        // include the correct mode and control plane URL on Application Insights.
        env["MigrationPlatform__Environment__Type"] = "Standalone";
        env["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = _controlPlaneUrl.ToString().TrimEnd('/');

        // Forward OTLP endpoint if set in the CLI's environment
        var otlp = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrEmpty(otlp))
            env["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlp;

        return env;
    }

    // ── Shared health check ────────────────────────────────────────────

    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient
        {
            BaseAddress = _controlPlaneUrl,
            Timeout = TimeSpan.FromSeconds(2)
        };

        await WaitForHealthyAsync(
            http,
            _controlPlaneUrl,
            () => _controlPlaneProcess?.HasExited == true,
            DefaultReadyTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task WaitForHealthyAsync(
        HttpClient http,
        Uri controlPlaneUrl,
        Func<bool> controlPlaneHasExited,
        TimeSpan readyTimeout,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < readyTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If the control plane process exited before becoming healthy, the port was
            // likely already in use (e.g. a ControlPlaneHostRunner from a previous test
            // run is still alive). Fail fast rather than accidentally connecting to the
            // stale process and hanging indefinitely.
            if (controlPlaneHasExited())
            {
                throw new InvalidOperationException(
                    $"ControlPlane process exited prematurely. " +
                    $"Port {controlPlaneUrl.Port} may already be in use by another process.");
            }

            if (await IsHealthyAsync(http, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(ReadinessPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"ControlPlane API at {controlPlaneUrl} did not become ready within {readyTimeout.TotalSeconds:0} seconds.");
    }

    internal static async Task<bool> IsHealthyAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync("/health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // ── Disposal ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync() => await DisposeProcessModeAsync();

    private async ValueTask DisposeProcessModeAsync()
    {
        // Stop agent first (it polls the control plane), then control plane.
        if (_agentProcess is not null)
        {
            await _agentProcess.DisposeAsync();
            _agentProcess = null;
        }

        if (_controlPlaneProcess is not null)
        {
            await _controlPlaneProcess.DisposeAsync();
            _controlPlaneProcess = null;
        }
    }
}

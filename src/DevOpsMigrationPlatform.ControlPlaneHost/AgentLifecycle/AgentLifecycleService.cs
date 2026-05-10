// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlaneHost.AgentLifecycle;

/// <summary>
/// Manages the lifecycle of the sibling Migration Agent process.
///
/// <para><b>Standalone mode</b> (packaged zip install, no Aspire):
/// When <c>DOTNET_RUNNING_UNDER_DOTNET_ASPIRE</c> is absent, this service resolves
/// <c>../MigrationAgent/DevOpsMigrationPlatform.MigrationAgent[.exe]</c> relative to
/// <c>AppContext.BaseDirectory</c> and spawns it as a child process. If the process
/// exits unexpectedly it is restarted with exponential back-off (1 s → 2 s → 4 s → … cap 60 s).
/// The back-off resets if the agent ran for more than 10 seconds (healthy start).
/// The control-plane URL is passed to the agent via <c>ControlPlane__BaseUrl</c>.</para>
///
/// <para><b>Aspire-managed mode</b> (local dev, <c>build.ps1 -Mode Start</c>):
/// When <c>DOTNET_RUNNING_UNDER_DOTNET_ASPIRE=true</c>, Aspire already manages the
/// agent process. This service logs a note and idles — no spawn logic runs.</para>
///
/// <para><b>Cloud mode</b> (Azure Container Apps):
/// ACA/KEDA manages agent container lifecycle. The agent binary is never co-located
/// with the control plane container, so the sibling path will not exist and the
/// service gracefully idles after logging a warning.</para>
///
/// <para>Auto-spawn can be disabled by setting <c>AgentLifecycle:AutoSpawn=false</c>
/// in configuration (e.g. <c>appsettings.json</c> or environment variable).</para>
///
/// See docs/control-plane.md — "Agent Lifecycle Management".
/// </summary>
internal sealed class AgentLifecycleService : BackgroundService
{
    private const int HealthyRunThresholdSeconds = 10;
    private const int MaxBackOffSeconds = 60;

    private readonly ILogger<AgentLifecycleService> _logger;
    private readonly IConfiguration _configuration;

    public AgentLifecycleService(ILogger<AgentLifecycleService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ── Aspire-managed: hands-off ─────────────────────────────────────────
        // Read via IConfiguration — ASP.NET Core surfaces environment variables through it,
        // keeping module code free of direct Environment calls (Hexagonal rule 6).
        if (!string.IsNullOrEmpty(_configuration["DOTNET_RUNNING_UNDER_DOTNET_ASPIRE"]))
        {
            _logger.LogInformation(
                "AgentLifecycleService: running under Aspire — agent lifecycle is managed externally. Idling.");
            return;
        }

        // ── Opt-out via config ────────────────────────────────────────────────
        if (!(_configuration.GetValue<bool?>("AgentLifecycle:AutoSpawn") ?? true))
        {
            _logger.LogInformation(
                "AgentLifecycleService: AutoSpawn disabled via configuration. Idling.");
            return;
        }

        // ── Resolve agent binary ──────────────────────────────────────────────
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "DevOpsMigrationPlatform.MigrationAgent.exe"
            : "DevOpsMigrationPlatform.MigrationAgent";

        var agentPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "MigrationAgent", exeName));

        if (!File.Exists(agentPath))
        {
            _logger.LogWarning(
                "AgentLifecycleService: agent binary not found at {AgentPath}. " +
                "Running from a source build or the packaged agent is not present. " +
                "Start the Migration Agent manually: " +
                "dotnet run --project src/DevOpsMigrationPlatform.MigrationAgent",
                agentPath);
            return;
        }

        // ── Resolve control plane URL to pass to the agent ────────────────────
        // ASPNETCORE_URLS may contain semicolon-separated values; use the first.
        // Read via IConfiguration rather than Environment.GetEnvironmentVariable (Hexagonal rule 6).
        var aspNetCoreUrls = _configuration["ASPNETCORE_URLS"] ?? string.Empty;
        var controlPlaneUrl = aspNetCoreUrls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0]
            .TrimEnd('/');
        if (string.IsNullOrEmpty(controlPlaneUrl))
            controlPlaneUrl = "http://localhost:5100";

        _logger.LogInformation(
            "AgentLifecycleService: standalone mode — spawning agent from {AgentPath}, " +
            "control plane URL: {ControlPlaneUrl}",
            agentPath, controlPlaneUrl);

        // ── Spawn + restart loop ──────────────────────────────────────────────
        var backOffSeconds = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            Process? process = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = agentPath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                psi.ArgumentList.Add($"--ControlPlane:BaseUrl={controlPlaneUrl}");
                psi.Environment["ControlPlane__BaseUrl"] = controlPlaneUrl;
                psi.Environment["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = controlPlaneUrl;

                process = Process.Start(psi);
                if (process is null)
                {
                    _logger.LogError("AgentLifecycleService: failed to start agent process.");
                }
                else
                {
                    _logger.LogInformation(
                        "AgentLifecycleService: agent started (PID {Pid}).", process.Id);

                    await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);

                    _logger.LogWarning(
                        "AgentLifecycleService: agent exited with code {ExitCode} after {Elapsed:F1}s.",
                        process.ExitCode, started.Elapsed.TotalSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested — kill agent if still running
                KillAgent(process);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentLifecycleService: unexpected error managing agent process.");
            }
            finally
            {
                process?.Dispose();
            }

            if (stoppingToken.IsCancellationRequested)
                return;

            // Reset back-off if the agent ran long enough to be considered healthy
            if (started.Elapsed.TotalSeconds >= HealthyRunThresholdSeconds)
                backOffSeconds = 1;

            _logger.LogInformation(
                "AgentLifecycleService: restarting agent in {BackOff}s.", backOffSeconds);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(backOffSeconds), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Exponential back-off capped at MaxBackOffSeconds
            backOffSeconds = Math.Min(backOffSeconds * 2, MaxBackOffSeconds);
        }
    }

    private void KillAgent(Process? process)
    {
        if (process is null || process.HasExited)
            return;

        try
        {
            _logger.LogInformation(
                "AgentLifecycleService: stopping agent (PID {Pid}).", process.Id);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AgentLifecycleService: error killing agent process.");
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlaneHost.Services;

/// <summary>
/// Monitors the lifecycle of Migration Agent processes.
///
/// In local and self-hosted topologies, Aspire spawns and restarts MigrationAgent
/// processes. This service tracks their liveness and will grow to handle crash
/// detection and policy-driven restarts in future phases.
///
/// In cloud deployments (Azure Container Apps), container orchestration manages
/// agent instances via KEDA scaling rules; this service defers to that mechanism.
///
/// See docs/control-plane.md — "Agent Lifecycle Management".
/// </summary>
internal sealed class AgentLifecycleService : BackgroundService
{
    private readonly ILogger<AgentLifecycleService> _logger;

    public AgentLifecycleService(ILogger<AgentLifecycleService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AgentLifecycleService started. Agent process lifecycle is currently managed by Aspire " +
            "(local/self-host) or Azure Container Apps (cloud). " +
            "Crash detection and policy-driven restarts will be added in a later phase.");

        // TODO: Phase 2 — poll for registered agents via the lease table;
        //       detect missed heartbeats; trigger respawn when running under
        //       self-hosted mode (i.e. when ASPIRE_MANAGED_AGENTS env var is set).
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

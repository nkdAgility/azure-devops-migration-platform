using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Background worker that polls the control plane for available MigrationJobs,
/// acquires a lease, executes the Job Engine, and reports progress.
/// See docs/migration-agent.md for the full lease protocol.
/// </summary>
public sealed class MigrationAgentWorker : BackgroundService
{
    private readonly ILogger<MigrationAgentWorker> _logger;

    public MigrationAgentWorker(ILogger<MigrationAgentWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration Agent started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: Implement lease polling loop (docs/migration-agent.md):
            //   1. GET /agents/lease — receive leased job
            //   2. Resolve Key Vault secrets
            //   3. Connect to artefact store via packageUri
            //   4. Load cursor → resume position
            //   5. Start heartbeat loop
            //   6. Run Job Engine (ExportAsync / ImportAsync / Both)
            //   7. POST /agents/lease/{id}/complete or /fail

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Migration Agent stopping.");
    }
}

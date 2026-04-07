using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Background worker that polls the control plane for available <see cref="MigrationJob"/>s,
/// acquires a lease, executes the Job Engine, and reports progress.
/// See docs/migration-agent.md for the full lease protocol.
/// </summary>
public sealed class MigrationAgentWorker : BackgroundService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceProvider _services;
    private readonly IProgressSink _progressSink;
    private readonly HttpClient _controlPlane;
    private readonly ILogger<MigrationAgentWorker> _logger;

    public MigrationAgentWorker(
        IServiceProvider services,
        IProgressSink progressSink,
        IHttpClientFactory httpClientFactory,
        ILogger<MigrationAgentWorker> logger)
    {
        _services = services;
        _progressSink = progressSink;
        _controlPlane = httpClientFactory.CreateClient("ControlPlane");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration Agent started — polling for jobs.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndExecuteAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in agent poll loop; retrying in 10 s.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Migration Agent stopping.");
    }

    private async Task PollAndExecuteAsync(CancellationToken ct)
    {
        // 1. Poll for a leased job (long-poll, 30 s server-side timeout).
        using var leaseResponse = await _controlPlane
            .GetAsync("/agents/lease", ct)
            .ConfigureAwait(false);

        // 204 = no pending job; back off briefly and try again.
        if (leaseResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            return;
        }

        leaseResponse.EnsureSuccessStatusCode();

        var lease = await leaseResponse.Content
            .ReadFromJsonAsync<AgentLeaseResponse>(_jsonOptions, ct)
            .ConfigureAwait(false);

        if (lease is null) return;

        _logger.LogInformation(
            "Acquired lease {LeaseId} for job {JobId} (mode={Mode})",
            lease.LeaseId, lease.Job.JobId, lease.Job.Mode);

        // 2. Build stores from job artefacts URI.
        var packagePath = ResolvePackagePath(lease.Job);
        var artefactStore = new FileSystemArtefactStore(packagePath);
        var stateStore = new FileSystemStateStore(packagePath);

        // 3. Build export context.
        var context = new ExportContext
        {
            Job = lease.Job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = _progressSink
        };

        // 4. Resolve IDataTypeModule registrations and run the export modules.
        bool failed = false;
        try
        {
            var modules = _services.GetServices<IDataTypeModule>();
            foreach (var module in modules)
            {
                _logger.LogInformation("Running module {Module}.ExportAsync", module.Name);
                await module.ExportAsync(context, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed during module execution.", lease.Job.JobId);
            failed = true;
        }

        // 5. Signal terminal state to control plane.
        var terminal = failed ? "fail" : "complete";
        await _controlPlane
            .PostAsync($"/agents/lease/{lease.LeaseId}/{terminal}", content: null, ct)
            .ConfigureAwait(false);
    }

    private static string ResolvePackagePath(MigrationJob job)
    {
        var uri = job.Artefacts.PackageUri;
        if (string.IsNullOrWhiteSpace(uri)) return ".";
        return uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            ? uri["file:///".Length..].Replace('/', System.IO.Path.DirectorySeparatorChar)
            : uri;
    }

    private sealed record AgentLeaseResponse(string LeaseId, MigrationJob Job);
}


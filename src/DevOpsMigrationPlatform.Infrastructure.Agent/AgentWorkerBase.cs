using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Serialization;
#endif

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Shared base class for all migration agents (MigrationAgent, TfsMigrationAgent).
/// Implements the polling loop, lease acquisition, and terminal signalling.
/// Subclasses implement <see cref="OnJobAsync"/> to dispatch on <see cref="Job.Kind"/>.
/// </summary>
public abstract class AgentWorkerBase : BackgroundService
{
    private readonly ActiveLeaseState _leaseState;
    private readonly ActivePackageState _packageState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// JSON options used for lease deserialization. Includes the polymorphic endpoint
    /// converter when registered. Exposed for subclasses that need to deserialize
    /// migration-config.json using the same converter (e.g. to handle init-only properties).
    /// </summary>
    protected JsonSerializerOptions AgentJsonOptions => _jsonOptions;

    protected AgentWorkerBase(
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory,
        ILogger logger
#if !NET481
        , PolymorphicEndpointOptionsConverter? endpointConverter = null
#endif
        )
    {
        _leaseState = leaseState ?? throw new ArgumentNullException(nameof(leaseState));
        _packageState = packageState ?? throw new ArgumentNullException(nameof(packageState));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
#if !NET481
        if (endpointConverter is not null)
            _jsonOptions.Converters.Add(endpointConverter);
#endif
    }

    /// <summary>
    /// Connector types this agent advertises to the control plane for job routing.
    /// Examples: <c>[ConnectorType.AzureDevOps, ConnectorType.Simulated]</c> or <c>[ConnectorType.TeamFoundationServer]</c>.
    /// </summary>
    protected abstract ConnectorType[] Capabilities { get; }

    /// <summary>
    /// Called when a <see cref="Job"/> is acquired from the control plane.
    /// Subclasses dispatch on <see cref="Job.Kind"/>.
    /// </summary>
    protected abstract Task OnJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct);

    /// <summary>
    /// Called after a job completes (success or failure) and before <see cref="ActivePackageState.Clear"/>.
    /// Subclasses should flush any buffered sinks here.
    /// </summary>
    protected virtual Task OnPostJobFlushAsync() => Task.CompletedTask;

    protected ActiveLeaseState LeaseState => _leaseState;
    protected ActivePackageState PackageState => _packageState;
    protected IHttpClientFactory HttpClientFactory => _httpClientFactory;
    protected JsonSerializerOptions JsonOptions => _jsonOptions;

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent worker started — polling for jobs.");

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

        _logger.LogInformation("Agent worker stopping.");
    }

    private async Task PollAndExecuteAsync(CancellationToken ct)
    {
        using var controlPlane = _httpClientFactory.CreateClient("ControlPlane");

        var capabilitiesParam = string.Join(",", Capabilities.Select(c => c.ToString()));
        var leaseUrl = $"/agents/lease?capabilities={Uri.EscapeDataString(capabilitiesParam)}";

        using var leaseResponse = await controlPlane
            .GetAsync(leaseUrl, ct)
            .ConfigureAwait(false);

        if (leaseResponse.StatusCode == HttpStatusCode.NoContent)
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
            "Acquired lease {LeaseId} for job {JobId} ({JobKind})",
            lease.LeaseId, lease.Job.JobId, lease.Job.Kind);

        _leaseState.CurrentLeaseId = lease.LeaseId;
        _packageState.CurrentJobId = lease.Job.JobId;

        await OnJobAsync(lease.Job, controlPlane, lease.LeaseId, ct).ConfigureAwait(false);

        _leaseState.CurrentLeaseId = null;

        await OnPostJobFlushAsync().ConfigureAwait(false);

        _packageState.Clear();
    }

    /// <summary>
    /// Signals the control plane that a job has reached a terminal state (complete or fail).
    /// Retries with exponential backoff up to 5 attempts.
    /// </summary>
    protected async Task SignalTerminalAsync(
        HttpClient controlPlane, string leaseId, string terminal, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await controlPlane
                    .PostAsync($"/agents/lease/{leaseId}/{terminal}", content: null, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Terminal signal attempt {Attempt}/{Max} failed for lease {LeaseId}; retrying in {Delay} s.",
                    attempt, maxAttempts, leaseId, delay.TotalSeconds);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
        }

        _logger.LogError(
            "Failed to signal terminal state for lease {LeaseId} after {Max} attempts.",
            leaseId, maxAttempts);
    }

    private sealed record AgentLeaseResponse(string LeaseId, Job Job);
}

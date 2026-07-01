// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
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
    private const int IdleLeaseLogEvery = 12; // 12 * 5s = ~60s
    private readonly ActiveLeaseState _leaseState;
    private readonly ActivePackageState _packageState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly UnifiedWorkerEventWriter _eventWriter;
    private int _consecutiveNoLeaseResponses;

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
        ILogger logger,
        UnifiedWorkerEventWriter eventWriter
#if !NET481
        , PolymorphicEndpointOptionsConverter? endpointConverter = null
        , PolymorphicOrganisationEntryConverter? organisationConverter = null
#endif
        )
    {
        _leaseState = leaseState ?? throw new ArgumentNullException(nameof(leaseState));
        _packageState = packageState ?? throw new ArgumentNullException(nameof(packageState));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
#if !NET481
        if (endpointConverter is not null)
            _jsonOptions.Converters.Add(endpointConverter);
        if (organisationConverter is not null)
            _jsonOptions.Converters.Add(organisationConverter);
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
        _logger.LogWarning(
            "Agent worker ExecuteAsync entered — polling for jobs against {BaseUrl}.",
            _httpClientFactory.CreateClient("ControlPlane").BaseAddress);

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

        _logger.LogWarning("Agent worker stopping.");
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
            _consecutiveNoLeaseResponses++;
            if (_consecutiveNoLeaseResponses == 1 || _consecutiveNoLeaseResponses % IdleLeaseLogEvery == 0)
            {
                _logger.LogInformation(
                    "No lease available yet (attempt {Attempt}) for capabilities [{Capabilities}] against {BaseAddress}.",
                    _consecutiveNoLeaseResponses,
                    capabilitiesParam,
                    controlPlane.BaseAddress);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            return;
        }

        leaseResponse.EnsureSuccessStatusCode();

        var lease = await leaseResponse.Content
            .ReadFromJsonAsync<AgentLeaseResponse>(_jsonOptions, ct)
            .ConfigureAwait(false);

        if (lease is null) return;
        _consecutiveNoLeaseResponses = 0;

        _logger.LogInformation(
            "Acquired lease {LeaseId} for job {JobId} ({JobKind})",
            lease.LeaseId, lease.Job.JobId, lease.Job.Kind);

        _leaseState.CurrentLeaseId = lease.LeaseId;
        _packageState.CurrentJob = lease.Job;

        try
        {
            if (!JobPackageUriResolver.TryResolveFromConfigPayload(lease.Job.ConfigPayload, out var packageUri))
            {
                _logger.LogError(
                    "Job {JobId} is missing MigrationPlatform.Package location in ConfigPayload.",
                    lease.Job.JobId);
                await SignalTerminalAsync("fail", ct).ConfigureAwait(false);
                return;
            }

            _packageState.CurrentPackageUri = packageUri;
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = SendHeartbeatsAsync(controlPlane, lease.LeaseId, heartbeatCts.Token);
            try
            {
                await OnJobAsync(lease.Job, controlPlane, lease.LeaseId, ct).ConfigureAwait(false);
            }
            finally
            {
                heartbeatCts.Cancel();
                await heartbeatTask.ConfigureAwait(false);
            }
        }
        finally
        {
            _leaseState.CurrentLeaseId = null;

            try
            {
                await OnPostJobFlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _packageState.Clear();
            }
        }
    }

    private async Task SendHeartbeatsAsync(HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        try
        {
#if NET481
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
#else
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
#endif
                try
                {
                    var response = await controlPlane
                        .PostAsync($"/agents/lease/{Uri.EscapeDataString(leaseId)}/heartbeat", content: null, ct)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "Heartbeat for lease {LeaseId} returned {StatusCode} — lease may be unknown or expired.",
                            leaseId, (int)response.StatusCode);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Heartbeat POST failed for lease {LeaseId}.", leaseId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Job finished — normal exit.
        }
    }

    /// <summary>
    /// Signals the control plane that a job has reached a terminal state (complete or fail),
    /// routed through <see cref="UnifiedWorkerEventWriter"/> — the unified agent-to-control-plane
    /// flush path — rather than a dedicated HTTP call. Retry/backoff for terminal signalling
    /// lives in <see cref="UnifiedWorkerEventWriter.FlushWithRetryAsync"/> (5-attempt exponential
    /// backoff, same shape as before).
    /// </summary>
    protected async Task SignalTerminalAsync(string terminal, CancellationToken ct)
    {
        _eventWriter.EnqueueTerminal(failed: terminal == "fail");
        await _eventWriter.FlushAsync().ConfigureAwait(false);
    }

    private sealed record AgentLeaseResponse(string LeaseId, Job Job);
}

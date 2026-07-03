// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Minimal <see cref="IControlPlaneAgentClient"/> adapter for the Migration Agent.
/// Uses the "ControlPlane" named <see cref="HttpClient"/> to call the agent-status endpoint.
/// Implements <see cref="IControlPlaneAgentClient.IsAgentActiveAsync"/> for stale-lock detection.
/// </summary>
public sealed class AgentControlPlaneClientAdapter : IControlPlaneAgentClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentControlPlaneClientAdapter> _logger;

    public AgentControlPlaneClientAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<AgentControlPlaneClientAdapter> logger)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAgentActiveAsync(string agentInstanceId, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ControlPlane");
            using var response = await client
                .GetAsync($"/agents/{agentInstanceId}/status", ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return false;

#if NET481
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var status = doc.TryGetProperty("status", out var s) ? s.GetString() : null;
            return string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-requested cancellation must never be reported as "agent stale" —
            // that would allow a lock steal during shutdown. Propagate it.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Transient HTTP failures have already been retried by the Polly resilience
            // pipeline attached to the named "ControlPlane" HttpClient at registration.
            // Reaching here means the pipeline was exhausted (or the payload was malformed);
            // treat the agent as stale, but leave a structured trace of why.
            _logger.LogWarning(
                ex,
                "Agent-status probe for {AgentInstanceId} failed after resilience pipeline; treating agent as stale.",
                agentInstanceId);
            return false;
        }
    }
}

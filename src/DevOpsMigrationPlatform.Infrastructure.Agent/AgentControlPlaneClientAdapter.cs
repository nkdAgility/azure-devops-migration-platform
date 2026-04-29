using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Minimal <see cref="IControlPlaneAgentClient"/> adapter for the Migration Agent.
/// Uses the "ControlPlane" named <see cref="HttpClient"/> to call the agent-status endpoint.
/// Implements <see cref="IControlPlaneAgentClient.IsAgentActiveAsync"/> for stale-lock detection
/// by <c>PackageLockFileService</c>.
/// </summary>
public sealed class AgentControlPlaneClientAdapter : IControlPlaneAgentClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AgentControlPlaneClientAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
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
        catch
        {
            return false; // treat network errors as stale
        }
    }
}


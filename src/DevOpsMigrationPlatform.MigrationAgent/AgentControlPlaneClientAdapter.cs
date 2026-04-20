using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Minimal <see cref="IControlPlaneClient"/> adapter for the Migration Agent.
/// Uses the "ControlPlane" named <see cref="HttpClient"/> to call control-plane endpoints.
/// Implements <see cref="IsAgentActiveAsync"/> for stale-lock detection by
/// <c>PackageLockFileService</c>. Other methods are not needed by the agent and
/// throw <see cref="NotSupportedException"/>.
/// </summary>
internal sealed class AgentControlPlaneClientAdapter : IControlPlaneClient
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

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

    // The following methods are implemented by CLI's ControlPlaneClient.
    // The agent uses HttpClient directly for job polling — these are not needed here.

    Task<IReadOnlyList<JobSummary>> IControlPlaneClient.GetAllJobsAsync(CancellationToken ct)
        => throw new NotSupportedException("GetAllJobsAsync is not supported in the agent adapter.");

    Task<MetricSnapshot?> IControlPlaneClient.GetTelemetryAsync(Guid jobId, CancellationToken ct)
        => throw new NotSupportedException("GetTelemetryAsync is not supported in the agent adapter.");

    IAsyncEnumerable<ProgressEvent> IControlPlaneClient.FollowLogsAsync(
        Guid jobId,
        CancellationToken ct)
        => throw new NotSupportedException("FollowLogsAsync is not supported in the agent adapter.");

    IAsyncEnumerable<DiagnosticLogRecord> IControlPlaneClient.StreamDiagnosticsAsync(
        Guid jobId,
        string? level,
        CancellationToken ct)
        => throw new NotSupportedException("StreamDiagnosticsAsync is not supported in the agent adapter.");
}

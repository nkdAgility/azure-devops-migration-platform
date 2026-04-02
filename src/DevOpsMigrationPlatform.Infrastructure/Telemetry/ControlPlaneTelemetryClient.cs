#if !NETFRAMEWORK
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Posts <see cref="MetricSnapshot"/> payloads to
/// <c>POST /agents/lease/{leaseId}/telemetry</c> on the Control Plane.
/// Best-effort: failures are logged as warnings and never re-thrown.
/// </summary>
internal sealed class ControlPlaneTelemetryClient : IControlPlaneTelemetryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ControlPlaneTelemetryClient> _logger;

    public ControlPlaneTelemetryClient(
        HttpClient http,
        ILogger<ControlPlaneTelemetryClient> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task PushSnapshotAsync(string leaseId, MetricSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            var response = await _http
                .PostAsJsonAsync($"/agents/lease/{Uri.EscapeDataString(leaseId)}/telemetry", snapshot, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Telemetry push for lease {LeaseId} returned {StatusCode}. Snapshot discarded.",
                    leaseId,
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — no action required.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push telemetry snapshot for lease {LeaseId}. Snapshot discarded.",
                leaseId);
        }
    }
}
#endif

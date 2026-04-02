using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Polls <c>GET /jobs/{jobId}/telemetry</c> on the Control Plane and pushes the
/// result to a <see cref="TelemetryPanel"/> for rendering.
/// Stops polling when <paramref name="stoppingToken"/> is cancelled.
/// </summary>
public sealed class TelemetryPoller
{
    private readonly HttpClient _http;
    private readonly TelemetryPanel _panel;
    private readonly ILogger<TelemetryPoller> _logger;

    public TelemetryPoller(
        HttpClient http,
        TelemetryPanel panel,
        ILogger<TelemetryPoller> logger)
    {
        _http   = http   ?? throw new ArgumentNullException(nameof(http));
        _panel  = panel  ?? throw new ArgumentNullException(nameof(panel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Polls on <paramref name="intervalSeconds"/> cadence until the token is cancelled.
    /// </summary>
    public async Task RunAsync(
        Guid jobId,
        int intervalSeconds,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken)
                      .ConfigureAwait(false);

            await PollOnceAsync(jobId, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PollOnceAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            var response = await _http
                .GetAsync($"/jobs/{jobId}/telemetry", ct)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var snapshot = await response.Content
                    .ReadFromJsonAsync<MetricSnapshot>(ct)
                    .ConfigureAwait(false);
                _panel.Update(snapshot);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                // No snapshot yet — leave current state; waiting message will show.
            }
            else
            {
                _logger.LogDebug(
                    "Telemetry poll for job {JobId} returned {StatusCode}.",
                    jobId,
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry poll for job {JobId} failed.", jobId);
        }
    }
}

#if !NETFRAMEWORK
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Implements <see cref="IProgressSink"/> and <see cref="BackgroundService"/>.
/// Buffers incoming <see cref="ProgressEvent"/> records in a bounded channel and
/// drains them by POSTing each event to the Control Plane progress endpoint.
/// Transient HTTP failures are logged at debug level and never propagated.
/// </summary>
public sealed class ControlPlaneProgressSink : BackgroundService, IProgressSink
{
    private const int ChannelCapacity = 100;

    private readonly Channel<ProgressEvent> _channel = Channel.CreateBounded<ProgressEvent>(
        new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

    internal const string HttpClientName = nameof(ControlPlaneProgressSink);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ActiveLeaseState _leaseState;
    private readonly ILogger<ControlPlaneProgressSink> _logger;

    public ControlPlaneProgressSink(
        IHttpClientFactory httpFactory,
        ActiveLeaseState leaseState,
        ILogger<ControlPlaneProgressSink> logger)
    {
        _httpFactory = httpFactory;
        _leaseState = leaseState;
        _logger = logger;
    }

    public void Emit(ProgressEvent evt)
    {
        _channel.Writer.TryWrite(evt);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var http = _httpFactory.CreateClient(HttpClientName);
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                var leaseId = _leaseState.CurrentLeaseId;
                if (string.IsNullOrEmpty(leaseId))
                {
                    _logger.LogWarning(
                        "Progress event for stage {Stage} dropped — no active lease. " +
                        "The agent may not have acquired a lease yet.",
                        evt.Stage);
                    continue;
                }

                try
                {
                    var response = await http
                        .PostAsJsonAsync($"/agents/lease/{Uri.EscapeDataString(leaseId)}/progress", evt, stoppingToken)
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        _logger.LogWarning(
                            "Progress POST for lease {LeaseId} returned {StatusCode}. Event dropped.",
                            leaseId, (int)response.StatusCode);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex,
                        "Progress POST for lease {LeaseId} failed with HTTP error. Event dropped.",
                        leaseId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — channel cancelled while waiting for next item.
        }
    }
}
#endif

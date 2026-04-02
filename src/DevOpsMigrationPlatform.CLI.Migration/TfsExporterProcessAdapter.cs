using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Parses NDJSON <see cref="ProgressEvent"/> lines from the TFS export subprocess stdout
/// and forwards them to an <see cref="IProgressSink"/>.
/// When a <see cref="ProgressEvent.Metrics"/> payload is present, it is pushed to the
/// Control Plane via <see cref="IControlPlaneTelemetryClient"/> (fire-and-forget).
/// </summary>
public sealed class TfsExporterProcessAdapter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IProgressSink _progressSink;
    private readonly IControlPlaneTelemetryClient? _telemetryClient;
    private readonly ILogger<TfsExporterProcessAdapter> _logger;
    private string? _currentLeaseId;

    public TfsExporterProcessAdapter(
        IProgressSink progressSink,
        ILogger<TfsExporterProcessAdapter> logger,
        IControlPlaneTelemetryClient? telemetryClient = null)
    {
        _progressSink   = progressSink   ?? throw new ArgumentNullException(nameof(progressSink));
        _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient;
    }

    /// <summary>Sets the lease id used when pushing snapshots to the Control Plane.</summary>
    public void SetLeaseId(string? leaseId) => _currentLeaseId = leaseId;

    /// <summary>
    /// Called for each raw stdout line from the subprocess.
    /// Lines that cannot be parsed as JSON are logged and skipped.
    /// </summary>
    public void OnStdoutLine(string line, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        ProgressEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<ProgressEvent>(line, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Subprocess stdout line is not a ProgressEvent: {Line}", line);
            return;
        }

        if (evt is null) return;

        // Forward to progress sink.
        _progressSink.Emit(evt);

        // Push snapshot to Control Plane when available — fire-and-forget.
        if (evt.Metrics is not null && _telemetryClient is not null && _currentLeaseId is not null)
        {
            _ = _telemetryClient.PushSnapshotAsync(_currentLeaseId, evt.Metrics, ct);
        }
    }
}

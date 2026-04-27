using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// Extension methods for adding file-based diagnostic exporters to the OTel pipeline.
/// Gated by <c>Telemetry:DiagnosticsPath</c> configuration. When set, each signal type
/// (traces, metrics, logs) is written to a separate file in the configured folder.
/// </summary>
public static class FileDiagnosticsExtensions
{
    /// <summary>
    /// Adds a file-based trace exporter that writes spans to
    /// <c>{diagnosticsPath}/{serviceName}-traces.log</c>.
    /// </summary>
    public static TracerProviderBuilder AddFileExporter(
        this TracerProviderBuilder builder, string diagnosticsPath, string serviceName)
    {
        var filePath = Path.Combine(diagnosticsPath, $"{serviceName}-traces.log");
        return builder.AddProcessor(
            new OpenTelemetry.SimpleActivityExportProcessor(new FileTraceExporter(filePath)));
    }

    /// <summary>
    /// Adds a file-based metric exporter that writes metric snapshots to
    /// <c>{diagnosticsPath}/{serviceName}-metrics.log</c>.
    /// </summary>
    public static MeterProviderBuilder AddFileExporter(
        this MeterProviderBuilder builder, string diagnosticsPath, string serviceName)
    {
        var filePath = Path.Combine(diagnosticsPath, $"{serviceName}-metrics.log");
        return builder.AddReader(
            new OpenTelemetry.Metrics.PeriodicExportingMetricReader(
                new FileMetricExporter(filePath),
                exportIntervalMilliseconds: 10_000));
    }

    /// <summary>
    /// Adds a file-based log provider that writes structured log entries to
    /// <c>{diagnosticsPath}/{serviceName}-logs.log</c>.
    /// </summary>
    public static ILoggingBuilder AddFileDiagnostics(
        this ILoggingBuilder builder, string diagnosticsPath, string serviceName)
    {
        var filePath = Path.Combine(diagnosticsPath, $"{serviceName}-logs.log");
        builder.AddProvider(new FileLoggerProvider(filePath));
        return builder;
    }

    /// <summary>
    /// Resolves <c>Telemetry:DiagnosticsPath</c> from configuration.
    /// Returns null if not configured.
    /// </summary>
    public static string? GetDiagnosticsPath(IConfiguration configuration)
    {
        var path = configuration["Telemetry:DiagnosticsPath"];
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Resolve relative paths against current directory
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }
}

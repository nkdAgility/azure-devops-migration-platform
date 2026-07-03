// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    /// Uses the factory overload of <c>AddReader</c> so that the reader is created
    /// during <c>MeterProvider</c> build — avoiding orphaned readers when
    /// <c>ConfigureOpenTelemetryMeterProvider</c> callbacks run later.
    /// </summary>
    public static MeterProviderBuilder AddFileExporter(
        this MeterProviderBuilder builder, string diagnosticsPath, string serviceName)
    {
        var filePath = Path.Combine(diagnosticsPath, $"{serviceName}-metrics.log");
        return builder.AddReader(sp =>
            new OpenTelemetry.Metrics.PeriodicExportingMetricReader(
                new FileMetricExporter(filePath),
                exportIntervalMilliseconds: 2_000));
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
    /// Adds a file-based log provider that writes only <c>DevOpsMigrationPlatform.*</c>
    /// log entries to <c>{diagnosticsPath}/{serviceName}-cli.log</c>.
    /// Infrastructure noise from <c>System.Net.Http</c>, <c>Microsoft.Hosting</c>, etc.
    /// is suppressed at the provider level.
    /// </summary>
    public static ILoggingBuilder AddCliFileDiagnostics(
        this ILoggingBuilder builder, string diagnosticsPath, string serviceName)
    {
        var filePath = Path.Combine(diagnosticsPath, $"{serviceName}-cli.log");
        builder.AddProvider(new CliFileLoggerProvider(filePath));
        return builder;
    }

    private const string SessionIdEnvVar = "Telemetry__DiagnosticsSessionId";

    /// <summary>
    /// Resolves <c>Telemetry:DiagnosticsPath</c> from configuration.
    /// Returns null if not configured. When configured, creates a session
    /// subfolder so that all processes in the same run share one folder.
    /// <para>
    /// Session identity resolution order:
    /// <list type="number">
    /// <item><c>Telemetry:DiagnosticsSessionId</c> from configuration</item>
    /// <item><c>Telemetry__DiagnosticsSessionId</c> environment variable (inherited from parent)</item>
    /// <item>A new <c>yyyyMMdd-HHmmss</c> timestamp, which is then published to the
    ///       environment variable so child processes inherit it.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static string? GetDiagnosticsPath(IConfiguration configuration)
    {
        var path = configuration["Telemetry:DiagnosticsPath"];
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = Environment.ExpandEnvironmentVariables(path);
        var basePath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);

        // 1. Explicit config value
        var sessionId = configuration["Telemetry:DiagnosticsSessionId"];

        // 2. Inherited from parent process via env var
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = Environment.GetEnvironmentVariable(SessionIdEnvVar);

        // 3. Generate and publish for child processes
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            Environment.SetEnvironmentVariable(SessionIdEnvVar, sessionId);
        }

        return Path.Combine(basePath, sessionId);
    }
}

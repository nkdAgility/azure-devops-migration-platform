// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Configuration for the telemetry pipeline.
/// Bind to the "Telemetry" section in appsettings.json via
/// <c>services.AddOptions&lt;TelemetryOptions&gt;().BindConfiguration(TelemetryOptions.SectionName)</c>.
/// </summary>
public sealed class TelemetryOptions
{
    public static string SectionName => "Telemetry";

    /// <summary>
    /// Azure Monitor connection string (Application Insights instrumentation key URL).
    /// Null or empty = Azure Monitor exporter not registered.
    /// OTLP export is configured via the standard OTEL_EXPORTER_OTLP_ENDPOINT environment
    /// variable, handled by ServiceDefaults — do not duplicate that configuration here.
    /// </summary>
    public string? AzureMonitorConnectionString { get; init; }

    /// <summary>
    /// Absolute or relative path to a folder where diagnostic telemetry files are written.
    /// When set, each host writes <c>{serviceName}-traces.log</c>, <c>{serviceName}-metrics.log</c>,
    /// and <c>{serviceName}-logs.log</c> to this folder. Null or empty = file diagnostics disabled.
    /// </summary>
    public string? DiagnosticsPath { get; init; }

    /// <summary>
    /// How often (seconds) the Migration Agent pushes a <see cref="JobMetrics"/> to the Control Plane.
    /// Default: 5 seconds.
    /// </summary>
    public int SnapshotIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// How often (revisions processed) the TFS subprocess embeds a <see cref="JobMetrics"/> in a
    /// ProgressEvent. Default: every 100 revisions.
    /// </summary>
    public int SubprocessSnapshotRevisionInterval { get; init; } = 100;
}

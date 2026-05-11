// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Configuration for agent-side diagnostic log sinks (package and control plane).
/// Bound from the <c>Diagnostics</c> configuration section.
/// </summary>
public sealed class DiagnosticLogOptions
{
    /// <summary>Configuration section name for <see cref="DiagnosticLogOptions"/>.</summary>
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// Minimum log level for diagnostic sinks. Records below this level are discarded.
    /// Default: <c>"Information"</c>. Set per-job via <c>export --level</c>.
    /// Valid values: Trace, Debug, Information, Warning, Error, Critical.
    /// </summary>
    public string MinimumLevel { get; init; } = "Information";

    /// <summary>Bounded channel capacity for the diagnostic log buffer.</summary>
    public int ChannelCapacity { get; init; } = 1024;

    /// <summary>Maximum flush interval in milliseconds.</summary>
    public int FlushIntervalMs { get; init; } = 500;

    /// <summary>Maximum number of records per flush batch.</summary>
    public int FlushBatchSize { get; init; } = 50;

    /// <summary>
    /// Maximum size threshold in megabytes used by the diagnostics drain loop.
    /// Diagnostic records are persisted to
    /// <c>.migration/runs/&lt;runId&gt;/logs/diagnostics.ndjson</c>.
    /// Default: 50 MB. Set to 0 to disable rotation.
    /// </summary>
    public int MaxLogFileSizeMB { get; init; } = 50;
}

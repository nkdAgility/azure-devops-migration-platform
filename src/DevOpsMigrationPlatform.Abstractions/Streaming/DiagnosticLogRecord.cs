using System;

namespace DevOpsMigrationPlatform.Abstractions.Streaming;

/// <summary>
/// Structured diagnostic log record derived from <c>ILogger</c> output.
/// Each record is serialised as one NDJSON line in <c>Logs/agent.jsonl</c>.
/// </summary>
public record DiagnosticLogRecord
{
    /// <summary>UTC timestamp when the log was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Log level name: Trace, Debug, Information, Warning, Error, Critical.</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>Logger category (fully qualified type name of the emitting class).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Formatted log message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Full exception <c>ToString()</c> when an exception is associated with the log entry.</summary>
    public string? Exception { get; init; }

    /// <summary>W3C trace ID from <c>Activity.Current</c> when present.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C span ID from <c>Activity.Current</c> when present.</summary>
    public string? SpanId { get; init; }

    /// <summary>
    /// Data classification of the log entry. Null or "System" for operational logs,
    /// "Customer" for customer-identifiable data, "Derived" for aggregates.
    /// </summary>
    public string? DataClassification { get; init; }
}

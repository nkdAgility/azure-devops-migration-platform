# Data Model: Three-Channel Observability

## Entities

### DiagnosticLogRecord (new)

Defined in `DevOpsMigrationPlatform.Abstractions`.

| Field | Type | Required | Description |
|---|---|---|---|
| `Timestamp` | `DateTimeOffset` | Yes | UTC timestamp when the log was emitted |
| `Level` | `string` | Yes | Log level name: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` |
| `Category` | `string` | Yes | Logger category (fully qualified type name of the emitting class) |
| `Message` | `string` | Yes | Formatted log message |
| `Exception` | `string?` | No | Full exception `ToString()` when an exception is associated with the log entry |
| `TraceId` | `string?` | No | W3C trace ID from `Activity.Current` when present |
| `SpanId` | `string?` | No | W3C span ID from `Activity.Current` when present |

```csharp
public record DiagnosticLogRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Level { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
}
```

### ProgressEvent (existing, unchanged)

Already defined in `DevOpsMigrationPlatform.Abstractions`. No schema changes.

| Field | Type | Description |
|---|---|---|
| `Module` | `string` | Module name (e.g., "WorkItems") |
| `Stage` | `string` | Stage label (e.g., "AppliedFields") |
| `LastProcessed` | `string?` | Relative path of last processed item |
| `TotalWorkItems` | `int` | Total items seen |
| `WorkItemsProcessed` | `int` | Items fully processed |
| `RevisionsProcessed` | `int` | Revisions written |
| `WorkItemId` | `int` | Current work item ID |
| `Message` | `string?` | Human-readable status |
| `Timestamp` | `DateTimeOffset` | UTC timestamp |
| `Metrics` | `MetricSnapshot?` | Optional metric snapshot |

### MetricSnapshot (existing, unchanged)

Already defined in `DevOpsMigrationPlatform.Abstractions`. No changes.

---

## Relationships

```
MigrationJob (1) ──→ (0..*) ProgressEvent       persisted to Logs/progress.jsonl
MigrationJob (1) ──→ (0..*) DiagnosticLogRecord  persisted to Logs/agent.jsonl
MigrationJob (1) ──→ (0..1) MetricSnapshot       polled, not persisted to package
```

All three channels are independently produced by the Migration Agent during job execution. None depends on the others.

---

## Package Layout (Logs/ folder)

```
PackageRoot/
  Logs/
    progress.jsonl    ← NDJSON of ProgressEvent records
    agent.jsonl       ← NDJSON of DiagnosticLogRecord records
```

Both files are append-only during a job. Both are NDJSON format (one JSON object per line, newline-delimited).

---

## Options Classes

### DiagnosticLogOptions (new)

```csharp
public sealed class DiagnosticLogOptions
{
    public const string SectionName = "Diagnostics";
    public string MinimumLevel { get; init; } = "Warning";
    public int ChannelCapacity { get; init; } = 1024;
    public int FlushIntervalMs { get; init; } = 500;
    public int FlushBatchSize { get; init; } = 50;
}
```

Note: `MinimumLevel` default changed from `"Information"` to `"Warning"` to match the `export --level` default. This is the agent-side level set per job.

### DiagnosticLogStoreOptions (new)

```csharp
public sealed class DiagnosticLogStoreOptions
{
    public const string SectionName = "DiagnosticLogStore";
    public int Capacity { get; init; } = 1000;
    public string MinimumLevel { get; init; } = "Warning";
}
```

Note: `MinimumLevel` is the control plane's deployment-level floor. Independent of the agent's per-job level. In standalone mode, the CLI sets this to match the operator's `--level`.

---

## State Transitions

No state transitions apply — `DiagnosticLogRecord` and `ProgressEvent` are immutable append-only records. They do not have lifecycle states.

---

## Validation Rules

- `DiagnosticLogRecord.Level` MUST be one of: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`.
- `DiagnosticLogRecord.Timestamp` MUST be UTC.
- `DiagnosticLogRecord.Category` MUST NOT be empty.
- `DiagnosticLogRecord.Message` MUST NOT be empty.

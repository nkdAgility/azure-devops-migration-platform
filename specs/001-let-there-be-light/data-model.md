# Data Model: Telemetry Pipeline — Cloud Export + TUI Live Feed

---

## 1. `MetricSnapshot` (new — `DevOpsMigrationPlatform.Abstractions`)

A point-in-time snapshot of all metric counter values captured from the running export.
Used as the payload for both the subprocess relay (via `ProgressEvent.Metrics`) and the
Migration Agent → Control Plane push (via `POST /agents/lease/{leaseId}/telemetry`).

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Point-in-time metric aggregates for a running export job.
/// Serialised as part of ProgressEvent when emitted from the TFS subprocess.
/// Also posted directly from the Migration Agent to the Control Plane.
/// </summary>
public record MetricSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // --- Work item counts ---
    public long WorkItemsExported     { get; init; }
    public long RevisionsExported     { get; init; }
    public long RevisionErrors        { get; init; }
    public long LinksExported         { get; init; }
    public long LinkErrors            { get; init; }

    // --- Attachment counts ---
    public long AttachmentsAttempted  { get; init; }
    public long AttachmentsSucceeded  { get; init; }
    public long AttachmentsFailed     { get; init; }

    // --- Duration aggregates (milliseconds) ---
    public double? WorkItemDurationMeanMs   { get; init; }
    public double? RevisionDurationMeanMs   { get; init; }
    public double? TotalExportDurationMs    { get; init; }
}
```

**Constraints**:
- All count fields are non-negative, monotonically increasing.
- Duration fields are nullable — they are `null` until at least one measurement is recorded.
- `Timestamp` is always UTC.

**Validation**: None required — snapshot values are best-effort; missing or zero values are normal at the start of a job.

---

## 2. `ProgressEvent` (extended — `DevOpsMigrationPlatform.Abstractions`)

The existing `ProgressEvent` record gains one optional property:

```csharp
/// <summary>
/// Optional metric snapshot emitted alongside this progress event.
/// Populated by the TFS subprocess every N revisions (default: 100).
/// Null when emitted by the .NET 10 Migration Agent directly.
/// </summary>
public MetricSnapshot? Metrics { get; init; }
```

This is purely additive — existing consumers that do not read `Metrics` continue to work unchanged.

**Also add a `WellKnownMeterNames` static class to `DevOpsMigrationPlatform.Abstractions`** — resolves the cross-project meter name accessibility issue (Principle VI prevents `net10.0` code referencing .NET 4.8 assemblies):

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Meter name constants shared across the solution.
/// Defined here so .NET 10 hosts can register meters without referencing .NET 4.8 assemblies.
/// </summary>
public static class WellKnownMeterNames
{
    public const string WorkItemExport      = "DevOpsMigrationPlatform.WorkItemExport";
    public const string AttachmentDownload  = "DevOpsMigrationPlatform.AttachmentDownload";
}
```

`WorkItemExportMetrics.MeterName` and `AttachmentDownloadMetrics.MeterName` in `.Infrastructure.TfsObjectModel` should reference these constants rather than re-declaring the strings.

---

Configuration options for all telemetry routing, bound via `IOptions<TelemetryOptions>`.
Section name: `"Telemetry"`.

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Configuration for the telemetry pipeline.
/// Bind to the "Telemetry" section in appsettings.json.
/// </summary>
public sealed class TelemetryOptions
{
    public static string SectionName => "Telemetry";

    /// <summary>
    /// Azure Monitor connection string (Application Insights instrumentation key URL).
    /// Null or empty = Azure Monitor exporter not registered.
    /// OTLP export is configured via the standard OTEL_EXPORTER_OTLP_ENDPOINT environment
    /// variable, handled by ServiceDefaults — do not duplicate it here.
    /// </summary>
    public string? AzureMonitorConnectionString { get; init; }

    /// <summary>
    /// How often (seconds) the Migration Agent pushes a MetricSnapshot to the Control Plane.
    /// Default: 5 seconds.
    /// </summary>
    public int SnapshotIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// How often (revisions) the TFS subprocess embeds a MetricSnapshot in a ProgressEvent.
    /// Default: every 100 revisions.
    /// </summary>
    public int SubprocessSnapshotRevisionInterval { get; init; } = 100;
}
```

---

## 4. `IMetricSnapshotStore` (new — `DevOpsMigrationPlatform.Abstractions`)

A thread-safe store that holds the latest `MetricSnapshot` produced by the OTel pipeline.
Written by `SnapshotMetricExporter` (registered in the `MeterProvider`) and read by
`ControlPlaneTelemetryTimer` for pushing to the Control Plane.

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Thread-safe store for the latest metric snapshot produced by the OTel export pipeline.
/// Written by SnapshotMetricExporter (BaseExporter&lt;Metric&gt;) and read by the telemetry push timer.
/// </summary>
public interface IMetricSnapshotStore
{
    /// <summary>Updates the stored snapshot. Called by SnapshotMetricExporter on each export cycle.</summary>
    void Update(MetricSnapshot snapshot);

    /// <summary>Returns the latest snapshot, or null if no export cycle has completed yet.</summary>
    MetricSnapshot? Latest { get; }
}
```

**Implementation** (`InMemoryMetricSnapshotStore`) uses a single `volatile` field — lock-free for single-writer (OTel export thread), single-reader (push timer) usage.

> **Why not `IMetricCollector`?** The previous design called `Collect()` from a custom `MeterListener`, bypassing the OTel SDK's aggregation engine. `IMetricSnapshotStore` is passive — the OTel `PeriodicExportingMetricReader` drives the `SnapshotMetricExporter` which calls `Update()`. No parallel metric-collection infrastructure exists.

---

## 5. `IControlPlaneTelemetryClient` (new — `DevOpsMigrationPlatform.Abstractions`)

Interface for the control plane HTTP telemetry push. Implemented in
`DevOpsMigrationPlatform.Infrastructure` using `HttpClient`.

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Pushes MetricSnapshot payloads to the Control Plane telemetry endpoint.
/// Called by the Migration Agent on a background timer.
/// </summary>
public interface IControlPlaneTelemetryClient
{
    Task PushSnapshotAsync(string leaseId, MetricSnapshot snapshot, CancellationToken ct);
}
```

---

## 6. Control Plane Telemetry Store (in-memory, per job)

The Control Plane stores the latest `MetricSnapshot` per `jobId` in memory.
No database persistence is required for Phase 1 — the TUI always gets the latest snapshot,
not history.

```text
In-memory dictionary: Dictionary<Guid, MetricSnapshot>
Key:   jobId
Value: latest MetricSnapshot received from the Migration Agent
```

**Eviction**: When a job reaches `Completed` or `Failed` state, the snapshot is retained
until the TUI requests it and then cleared on next GC cycle (TTL 1 hour or explicit eviction).

**Thread safety**: `ConcurrentDictionary<Guid, MetricSnapshot>`.

---

## 7. State Transitions Relevant to Telemetry

```
Job: Queued → Leased → Running ──── [AgentJobService pushes snapshots every 5s]
                                ↓
                           Completed / Failed  ←── Final snapshot pushed before terminal state
```

Telemetry push happens only while `Running`. The final `MetricSnapshot` is pushed as part of
the `POST /agents/lease/{leaseId}/complete` or `/fail` body to capture end-of-run totals.

---

## 8. Subprocess Relay Flow

```
WorkItemExportService (.NET 4.8 subprocess)
  │  Every 100 revisions (configurable):
  │  ProgressEvent { ..., Metrics: MetricSnapshot { WorkItemsExported: 450, ... } }
  │  serialised as NDJSON line on stdout
  ▼
TfsExporterProcessAdapter (.NET 10, Migration Agent)
  │  Reads NDJSON line → deserialises ProgressEvent
  │  Extracts Metrics → stores as latestSnapshot
  │  Forwards ProgressEvent (with Metrics stripped) to IProgressSink
  │
  ├──► IProgressSink (cursor/stage progress → control plane progress endpoint)
  └──► IControlPlaneTelemetryClient (snapshot → /agents/lease/{id}/telemetry)
```

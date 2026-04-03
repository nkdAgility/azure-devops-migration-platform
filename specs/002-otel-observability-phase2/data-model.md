# Data Model: OpenTelemetry Observability — CLI DI and Phase 2 Live Progress Streaming

**Feature**: `002-otel-observability-phase2`  
**Phase**: 1 — Design & Contracts

---

## Existing Entities (unchanged)

### `ProgressEvent` — `DevOpsMigrationPlatform.Abstractions/Models/ProgressEvent.cs`

A structured progress event emitted by the Job Engine or a module. Already defined; **no changes required** by this feature.

| Field | Type | Description |
|---|---|---|
| `Module` | `string` | Module that emitted the event, e.g. `"WorkItems"` |
| `Stage` | `string` | Current stage label, e.g. `"AppliedFields"` |
| `LastProcessed` | `string?` | Relative path of the last processed revision folder |
| `TotalWorkItems` | `int` | Total work items seen so far |
| `WorkItemsProcessed` | `int` | Work items fully processed |
| `RevisionsProcessed` | `int` | Revisions written to the package |
| `WorkItemId` | `int` | Work item ID currently being processed |
| `Message` | `string?` | Human-readable status message |
| `Timestamp` | `DateTimeOffset` | UTC timestamp when emitted (default: `UtcNow`) |
| `Metrics` | `MetricSnapshot?` | Optional metric snapshot (from TFS subprocess every N revisions; null from .NET 10 Agent) |

> **Note**: `ProgressEvent` does **not** include `jobId`. The Control Plane resolves the job ID from the lease ID at receipt (`POST /agents/lease/{leaseId}/progress`).

### `MetricSnapshot` — `DevOpsMigrationPlatform.Abstractions/Models/MetricSnapshot.cs`

Point-in-time metric aggregates for a running export job. Already defined; **no changes required**.

---

## New Entities

### `JobProgressOptions` — `DevOpsMigrationPlatform.ControlPlane/Services/JobProgressOptions.cs`

Sealed options class, bound under `"JobProgress"` in `appsettings.json`.

| Property | Type | Default | Validation |
|---|---|---|---|
| `Capacity` | `int` | `1000` | `[Range(1, 100_000)]` |

```csharp
public sealed class JobProgressOptions
{
    public const string SectionName = "JobProgress";

    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1000;
}
```

### `JobProgressEntry` — internal to `JobProgressStore`

Not a public type; internal implementation detail of `JobProgressStore`.

| Field | Type | Description |
|---|---|---|
| `Queue` | `ConcurrentQueue<ProgressEvent>` | Snapshot ring buffer; evict oldest on overflow |
| `Subscribers` | `List<ChannelWriter<ProgressEvent>>` | Active SSE subscriber writers; guarded by a lock on mutation |

### `JobProgressStore` — `DevOpsMigrationPlatform.ControlPlane/Services/JobProgressStore.cs`

In-memory store keyed by job ID. Analogous to `JobTelemetryStore` for `MetricSnapshot`.

**Public API**:

| Method | Signature | Description |
|---|---|---|
| `Append` | `void Append(Guid jobId, ProgressEvent evt)` | Adds event to snapshot queue (evict oldest if full); fans out to all active subscriber channels |
| `GetSnapshot` | `IReadOnlyList<ProgressEvent> GetSnapshot(Guid jobId)` | Returns a copy of the current snapshot queue contents for `jobId`; empty list if job unknown |
| `Subscribe` | `ChannelReader<ProgressEvent> Subscribe(Guid jobId)` | Creates a new bounded subscriber channel, registers its writer, returns the reader |
| `Unsubscribe` | `void Unsubscribe(Guid jobId, ChannelWriter<ProgressEvent> writer)` | Removes and completes the writer; called on SSE disconnect |
| `CompleteJob` | `void CompleteJob(Guid jobId)` | Completes all subscriber channels for `jobId`; signals stream end to SSE consumers |
| `Remove` | `void Remove(Guid jobId)` | Evicts the snapshot buffer and subscriber list for `jobId`; called after job reaches terminal state and all SSE streams are closed |

**State machine note**: `CompleteJob` signals the SSE `job-ended` event by completing each subscriber `ChannelWriter`. The `ProgressController` SSE loop detects channel completion and sends the `event: job-ended\ndata: {}\n\n` message before closing the response.

### `ControlPlaneProgressSink` — `DevOpsMigrationPlatform.Infrastructure/Telemetry/ControlPlaneProgressSink.cs`

Implements `IProgressSink` + `BackgroundService`. Registered once as a singleton in `MigrationAgent` as part of a `CompositeProgressSink`.

| Field | Type | Description |
|---|---|---|
| `_channel` | `Channel<ProgressEvent>` | Bounded, `DropOldest`; capacity fixed at `private const int ChannelCapacity = 100` — internal fire-and-forget buffer, not user-configurable |
| `_http` | `HttpClient` | Named client targeting the Control Plane base URL |
| `_leaseState` | `ActiveLeaseState` | Provides `LeaseId` for the POST URL |
| `_logger` | `ILogger<ControlPlaneProgressSink>` | Debug-level logging for dropped/failed events |

**`Emit(ProgressEvent evt)`**: Calls `_channel.Writer.TryWrite(evt)`. `DropOldest` ensures this never blocks or throws. If the channel is full the oldest buffered event is evicted to make room.

**Drain loop (`ExecuteAsync`)**: `await foreach (var evt in _channel.Reader.ReadAllAsync(ct))` → `await PostEventAsync(evt, ct)`. On `HttpRequestException`, log at debug and continue — never propagates to caller.

### `CompositeProgressSink` — `DevOpsMigrationPlatform.Infrastructure/Telemetry/CompositeProgressSink.cs`

Broadcasts a single `ProgressEvent` to a list of inner `IProgressSink` instances. Registered in `MigrationAgent` so all three sinks — `AnsiProgressSink`, `PackageProgressSink`, and `ControlPlaneProgressSink` — receive every event without coupling `MigrationAgentWorker` to the multi-sink concern.

| Field | Type | Description |
|---|---|---|
| `_sinks` | `IReadOnlyList<IProgressSink>` | Ordered list of inner sinks; stored from `params IProgressSink[] sinks` constructor argument |
| `_logger` | `ILogger<CompositeProgressSink>` | Used to log debug-level exceptions from individual sinks |

**Constructor**: `(ILogger<CompositeProgressSink> logger, params IProgressSink[] sinks)` — stores `_logger` and `_sinks = sinks.ToList()`.

**`Emit(ProgressEvent evt)`**: Iterates `_sinks` and calls `sink.Emit(evt)` on each. Any exception is caught, logged via `_logger.LogDebug(ex, "Sink {Sink} threw during Emit", sink.GetType().Name)`, and execution continues — one failing sink must not suppress the others.

**Registration** (in `MigrationAgent/Program.cs`):
```csharp
builder.Services.AddSingleton<ControlPlaneProgressSink>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneProgressSink>());
builder.Services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
    sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
    new AnsiProgressSink(),
    new PackageProgressSink(...),
    sp.GetRequiredService<ControlPlaneProgressSink>()
));
```

### `LogsCommand` — `DevOpsMigrationPlatform.CLI.Migration/Commands/LogsCommand.cs`

Spectre.Console command: `migrate logs`.

| Setting Flag | Type | Required | Description |
|---|---|---|---|
| `JobId` | `Guid` | Yes | `--job <guid>` |
| `Follow` | `bool` | No | `--follow` — enable SSE tail mode |

**Behaviour**:
- Without `--follow`: call `ControlPlaneClient.GetLogsAsync(jobId, ct)`, print each event as compact JSON to stdout, exit 0.
- With `--follow`: call `ControlPlaneClient.FollowLogsAsync(jobId, ct)`, print each arriving event as compact JSON, exit 0 on stream-end / non-zero on unrecoverable error.

---

## State Transitions

```
Agent running
  │
  ├─ Emit(ProgressEvent)
  │     → ControlPlaneProgressSink.Emit (TryWrite to channel)
  │     → background drain → POST /agents/lease/{leaseId}/progress
  │     → ProgressController.PostProgress
  │     → JobProgressStore.Append
  │           → snapshot queue (evict oldest if full)
  │           → fan-out to subscriber channels
  │
  ├─ GET /jobs/{jobId}/logs
  │     → ProgressController.GetLogs
  │     → JobProgressStore.GetSnapshot → JSON array response
  │
  └─ GET /jobs/{jobId}/logs?follow=true
        → ProgressController.FollowLogs
        → JobProgressStore.Subscribe → ChannelReader
        → SSE loop (data: {json}\n\n per event, heartbeat every 15s)
        → on channel complete → event: job-ended → close

Job terminal state (Completed | Failed | Cancelled)
  → Control Plane state machine → JobProgressStore.CompleteJob
  → all subscriber channels completed → SSE streams close
```

---

## Validation Rules

| Entity | Rule |
|---|---|
| `JobProgressOptions.Capacity` | `[Range(1, 100_000)]`; validated at startup via `ValidateOnStart()` |
| `POST /agents/lease/{leaseId}/progress` body | Must be a valid `ProgressEvent` JSON object; 400 on parse failure |
| `GET /jobs/{jobId}/logs` | `jobId` must be a valid `Guid`; 404 if unknown; 403 if caller lacks visibility |
| `ControlPlaneProgressSink` channel write | `TryWrite` — never blocks; `DropOldest` evicts oldest buffered event on overflow |
| `ControlPlaneProgressSink` HTTP POST | Best-effort; failure logged at debug, never thrown to caller |
| `CompositeProgressSink.Emit` | Any individual sink exception is swallowed (logged at debug) so sibling sinks always run |

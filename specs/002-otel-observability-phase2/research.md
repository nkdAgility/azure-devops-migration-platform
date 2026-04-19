# Research: OpenTelemetry Observability — CLI DI and Phase 2 Live Progress Streaming

**Feature**: `002-otel-observability-phase2`  
**Phase**: 0 — Research & Decision Log  
**Status**: Complete — all unknowns resolved

---

## Decision 1 — CLI OTel DI Pattern (non-hosted application)

**Question**: How do we wire OpenTelemetry into `CLI.Migration/Program.cs`, which uses a bare `ServiceCollection` (not `IHostApplicationBuilder`)?

**Decision**: Register OTel using `services.AddOpenTelemetry().WithTracing(...).WithMetrics(...)` directly on `IServiceCollection` — the same extension entry point used by hosted applications. After building the `ServiceProvider`, hold a reference to `TracerProvider` and `MeterProvider` (retrieved from the container) and call `ForceFlush()` + `Dispose()` at the end of `Main` before the process exits.

**Rationale**: `OpenTelemetry.Extensions.Hosting` exposes `AddOpenTelemetry()` on `IServiceCollection`, so the CLI can use it without a full `IHost`. The TracerProvider and MeterProvider are registered as singletons and must be explicitly flushed because no `IHostApplicationLifetime` runs the shutdown sequence.

**Alternatives considered**:
- Build the `IHost` just for OTel → rejected: adds startup overhead (~50–100 ms cold start) and couples the thin CLI to hosted lifetime semantics.
- Build `TracerProvider` outside DI via `Sdk.CreateTracerProvider()` → rejected: means OTel configuration is split across two separate service graphs, making `IOptions<TelemetryOptions>` binding awkward.

---

## Decision 2 — Azure Monitor Exporter Package for CLI

**Question**: Which Azure Monitor exporter package is correct for a non-ASP.NET Core application?

**Decision**: Use `Azure.Monitor.OpenTelemetry.Exporter` (the standalone package). Call `.AddAzureMonitorExporter()` on both `TracerProviderBuilder` and `MeterProviderBuilder`. Register only when `TelemetryOptions.AzureMonitorConnectionString` is non-null/non-empty.

**Rationale**: `Azure.Monitor.OpenTelemetry.AspNetCore` adds ASP.NET Core middleware and automatic request telemetry. It is not appropriate for a CLI process. The standalone exporter package provides the same backend export without ASP.NET Core dependencies.

**Alternatives considered**:
- `Azure.Monitor.OpenTelemetry.AspNetCore` → rejected: wrong package for CLI; brings in unnecessary ASP.NET Core references.
- Application Insights classic SDK → rejected: deprecated and not OTel-native.

---

## Decision 3 — Ring Buffer Design in `JobProgressStore`

**Question**: The spec references a "per-job `Channel<ProgressEvent>` using `BoundedChannelFullMode.DropOldest`". Does a single `Channel<ProgressEvent>` satisfy both the snapshot (`GET /logs`) and live SSE (`GET /logs?follow=true`) use cases?

**Decision**: No. A `Channel<ProgressEvent>` is consumed destructively — reading an item removes it from the channel, so it cannot serve both a point-in-time snapshot and multiple concurrent SSE subscribers. The correct implementation is:

- **Snapshot buffer**: `ConcurrentQueue<ProgressEvent>` capped at `JobProgressOptions.Capacity` (default 1000). On write, if count ≥ capacity, dequeue the oldest item before enqueuing the new one. This gives O(1) `TryDequeue`/`Enqueue` and thread-safe enumeration via `ToArray()` for the snapshot endpoint.
- **SSE fan-out**: Each SSE subscriber gets its own `Channel<ProgressEvent>` (bounded, `DropOldest`, same capacity). When a new event arrives, it is written to the snapshot buffer AND to each active subscriber channel via `TryWrite`. Subscribers that can't keep up lose old events (DropOldest per subscriber).
- `JobProgressStore` maintains a `ConcurrentDictionary<Guid, JobProgressEntry>` where `JobProgressEntry` holds the queue and a `List<ChannelWriter<ProgressEvent>>` (protected by a lightweight lock for mutations; reads are snapshot-based).

**Rationale**: The spec's use of "Channel with DropOldest" describes the bounded-eviction semantics, not the literal data structure. `ConcurrentQueue` + per-subscriber channels is the standard ASP.NET Core SSE fan-out pattern.

**Alternatives considered**:
- `System.Collections.Generic.Queue<T>` with a `ReaderWriterLockSlim` → rejected: more locking complexity than `ConcurrentQueue`.
- `BroadcastBlock<T>` (TPL Dataflow) → rejected: Dataflow adds a NuGet dependency and is heavier than necessary for this use case.
- Single `Channel<ProgressEvent>` → rejected: destructive read makes snapshot impossible without consuming events.

---

## Decision 4 — SSE Endpoint Implementation

**Question**: How is the `GET /jobs/{jobId}/logs?follow=true` SSE endpoint implemented in ASP.NET Core MVC?

**Decision**: Implement `ProgressController` as a standard MVC controller (consistent with `TelemetryController`). The SSE action method:
1. Sets `Response.ContentType = "text/event-stream"` and `Response.Headers.CacheControl = "no-cache"`.
2. Disables response buffering (`Response.Body.Flush()` or `IHttpResponseBodyFeature.DisableBuffering()`).
3. Registers a subscriber channel with `JobProgressStore`.
4. Loops over `ChannelReader.ReadAllAsync(ct)`, writing each event as `data: {json}\n\n`.
5. Sends a heartbeat comment `:\n\n` every 15 s using a `PeriodicTimer` racing against the channel read.
6. On `job-ended` (job terminal state), sends `event: job-ended\ndata: {}\n\n` and closes.
7. On cancellation (client disconnect), removes the subscriber channel from `JobProgressStore`.

**Rationale**: Direct `Response` writing is the idiomatic minimal approach, avoids SignalR/WebSocket complexity, and is easy to unit-test by providing a mock `Response.Body`. `PeriodicTimer` (available in .NET 6+) is the correct heartbeat mechanism — no `Task.Delay` loops.

**Alternatives considered**:
- `IAsyncEnumerable<ServerSentEvent>` with a custom result type → rejected: no built-in ASP.NET Core support; requires a custom action result.
- SignalR → rejected: out of scope per spec assumptions; SSE is sufficient for unidirectional flow.
- Minimal API endpoint → rejected: inconsistent with the existing MVC controller pattern in `ControlPlane`.

---

## Decision 5 — `ControlPlaneProgressSink` Background Worker Ownership

**Question**: Should `ControlPlaneProgressSink` own the background Channel and drain task, or should a separate `BackgroundService` be responsible?

**Decision**: `ControlPlaneProgressSink` implements both `IProgressSink` and `IHostedService` (inheriting `BackgroundService`). It owns the bounded `Channel<ProgressEvent>` and the drain loop. The Migration Agent registers it **once** as a singleton and adds it to the DI hosted services. DI resolves the same instance for both `IProgressSink` injection points and the hosted service runner.

Registration pattern:
```csharp
builder.Services.AddSingleton<ControlPlaneProgressSink>();
builder.Services.AddSingleton<IProgressSink>(sp => sp.GetRequiredService<ControlPlaneProgressSink>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneProgressSink>());
```

**Rationale**: Single class keeps the bounded channel, drain loop, and HTTP dispatch colocated. Avoids a shared-channel coordination contract between two separate classes. Pattern is identical to `ControlPlaneTelemetryTimer : BackgroundService` already in `MigrationAgent`.

**Alternatives considered**:
- Separate `ControlPlaneProgressWorker : BackgroundService` sharing a channel via DI → rejected: forces a cross-class contract for the `ChannelWriter`, adding indirection with no benefit.
- Fire-and-forget `Task.Run` inside `Emit` → rejected: uncontrolled thread usage; no backpressure; violates Principle IX (no ambient statics).

---

## Decision 6 — `ControlPlaneProgressSink` Registration Condition

**Question**: When should `ControlPlaneProgressSink` be registered?

**Decision**: Register `ControlPlaneProgressSink` only when `ControlPlane:BaseUrl` is configured (the same condition that guards `AddControlPlaneTelemetryClient` in `MigrationAgent/Program.cs`). In test harnesses or standalone runs without a Control Plane URL, omit the registration — the composite `IProgressSink` list will contain only `ConsoleProgressSink` and `PackageProgressSink`.

**Rationale**: Prevents startup failures in environments where the Control Plane is not reachable. Matches the existing guard pattern.

---

## Decision 7 — `ControlPlaneClient` Extension for Logs Endpoints

**Question**: How does `LogsCommand` call the new logs endpoints?

**Decision**: Extend the existing `ControlPlaneClient` in `CLI.Migration/JobRunners/` with two new methods:
- `Task<IReadOnlyList<ProgressEvent>> GetLogsAsync(Guid jobId, CancellationToken ct)` — calls `GET /jobs/{jobId}/logs`, deserialises the JSON array.
- `IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct)` — opens `GET /jobs/{jobId}/logs?follow=true` with `HttpCompletionOption.ResponseHeadersRead`, reads the `text/event-stream` line-by-line, parses the `data:` prefix, deserialises each event, and `yield returns` them.

`ControlPlaneClient` already holds an `HttpClient` and `JsonSerializerOptions`. The new methods follow the same pattern as the existing `RunAsync` method.

**Rationale**: Reusing `ControlPlaneClient` avoids duplicating HttpClient configuration. The async enumerable pattern for SSE is already used in `RunAsync` (which polls progress).

---

## Decision 8 — Feature File Location

**Question**: Where do the Gherkin feature files for this feature live?

**Decision**: `features/platform/telemetry/` — observability is a cross-cutting platform concern that spans Infrastructure, ControlPlane, MigrationAgent, and CLI. It does not belong under `export/`, `import/`, or `services/`. The `platform/` category (alongside `platform/checkpointing/`, `platform/validation/`) is the correct home per the `agents.md` features layout.

Files:
- `features/platform/telemetry/cli-otel.feature` — US-1 CLI OTel DI scenarios
- `features/platform/telemetry/progress-sink.feature` — US-2 ControlPlaneProgressSink scenarios
- `features/platform/telemetry/job-progress-store.feature` — US-2 ring buffer unit scenarios
- `features/platform/telemetry/progress-controller.feature` — US-2 endpoint scenarios
- `features/platform/telemetry/migrate-logs.feature` — US-3 migrate logs command scenarios

---

## Resolved Clarifications (from speckit.clarify session)

All five ambiguities from the spec clarification session are resolved and captured here for reference:

| # | Question | Resolution |
|---|----------|------------|
| 1 | Auth on logs endpoints | Same auth as `GET /jobs/{jobId}` — caller must have job visibility (owner or admin). 403 on denial. |
| 2 | `migrate logs` output format | NDJSON — one compact JSON `ProgressEvent` per line. No headers, no schema envelope. |
| 3 | Ring buffer full — drop new or evict oldest? | Evict oldest (`DropOldest` semantics) so the live stream always reflects recent activity. |
| 4 | POST batching in `ControlPlaneProgressSink` | No batching in v1. Each event is sent individually via the background `Channel`. Natural flow control through the bounded channel capacity. |
| 5 | Max concurrent SSE subscribers per job | No hard limit in v1. High subscriber count is a documented operational constraint. No enforcement in code. |

# Observability Requirements

These requirements are mandatory for all module and connector implementations.

## O-1 — Activity Spans

Every module operation must create an `ActivitySource.StartActivity` span.

Requirements:
- Span name follows `{operation}.{subject}` convention, e.g. `export.workitems`, `import.teams`.
- Span must have at minimum a `module` tag.
- High-cardinality tags (e.g. org URL, project name) are permitted in traces but must carry `DataClassification.Customer` and must not be forwarded to Application Insights.

## O-2 — Business Metrics

Every module operation must call `IMigrationMetrics` for:

- `RecordAttempt` — before the operation begins.
- `RecordCompletion` — when the operation completes successfully, including count.
- `RecordError` — when the operation fails.
- `RecordDuration` — elapsed time for the operation.
- In-flight counter — increment on start, decrement on completion or error.

A counter added to `MigrationCounters` DTO must have a corresponding rendered row in `QueueCommand.BuildProgressRenderable`.

## O-3 — Structured Logging

Every module operation must log:

- `Information` — on start and successful completion (message and count).
- `Warning` — on skipped items, partial failures, or zero-count completions when the module is enabled.
- `Debug` — per-item detail (optional, behind a Debug log level check).
- `Error` — on failures with the exception.

A zero-count completion when the module is enabled must emit a `Warning` log. Silent zero-count completions are indistinguishable from fake implementations.

## O-4 — Progress Events

Operations that produce items must emit `ProgressEvent` via `IProgressSink`:

- At the start of the operation.
- Per item, or per batch of ≤50 items.
- At completion with final counts.

`IProgressSink` must be injected as optional (`IProgressSink?`). If null, progress emission is silently skipped.

`ProgressEvent.Metrics` is only populated by the TFS subprocess (net481). .NET 10 agents must always set it to null.

## O-5 — Per-Batch Progress Callbacks on Fetch/Discovery Infrastructure

Every caller of `IWorkItemFetchService.FetchAsync` must set `WorkItemFetchScope.Progress` to a non-null `IProgress<int>` wired to `IProgressSink.Emit`.

Every caller of `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` or `CountWorkItemsAsync` must pass a non-null `progress` argument wired to `IProgressSink.Emit`.

Passing `null` is only permitted for: `InventoryService` (already emits per-window events), `CatalogService` (no sink injected), `TfsJobAgentWorker` (counts come from the subprocess stream), and unit/integration tests. All other callers must not pass `null` without a documented rationale in an inline comment.

Do not inject `IProgressSink` into infrastructure classes. Infrastructure reports a bare `int` count via `IProgress<T>`; the calling module wraps it in a `ProgressEvent`.

## Separation of Channels

The CLI progress display and TUI Metrics panel must read from the Control Plane API, not from an in-process progress sink. See `control-plane-rules.md`.

## Related

- [.agents/context/telemetry-model.md](../context/telemetry-model.md) — telemetry model overview
- [docs/telemetry-development-guide.md](../docs/telemetry-development-guide.md) — implementation guide
- [docs/observability.md](../docs/observability.md) — operator observability guide
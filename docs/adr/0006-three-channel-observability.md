# ADR 0006 — Three-Channel Observability

## Status

Accepted — amended by Phase A-E iron-comms implementation (2026-06-30). The wire-transport decision is promoted to [ADR-0020](0020-unified-worker-event-channel.md); the Amendment section below is retained as history.

## Context

The platform emits different kinds of signals for different audiences:

- **Operators** need real-time progress while a job runs and a post-mortem record if it fails.
- **Platform engineers** need distributed traces and aggregated metrics for dashboards and alerts.
- **Support** needs structured diagnostic logs with full exception detail.

Previous implementations conflated these into a single channel (e.g., progress events called "logs", the `/logs` endpoint used for both), making each audience's needs harder to satisfy without compromising the others.

## Decision

All observability output is routed through exactly three channels that must not be conflated:

| Channel | Mechanism | Audience | Storage |
|---|---|---|---|
| **O-1: OTel Signals** | `ActivitySource` (traces), `IMeter` (metrics), `ILogger` OTel exporter | Dashboards, alerting, APM | OTel collector / OTLP endpoint |
| **O-2: Progress Events** | `IProgressSink.Emit(ProgressEvent)` | TUI live view, CLI `--follow`, Control Plane API | `.migration/runs/<runId>/logs/progress.ndjson` in package; `GET /jobs/{id}/progress?follow=true` (SSE) |
| **O-3: Diagnostic Logs** | `ILogger` → `PackageLoggerProvider` | Operators troubleshooting failures | `.migration/runs/<runId>/logs/diagnostics.ndjson` in package; `GET /jobs/{id}/diagnostics?follow=true` (SSE) |

Each channel has a dedicated API endpoint on the Control Plane. Progress and diagnostics are also persisted to the package as NDJSON files so they travel with the migration artefacts.

## Alternatives Considered

**Single event stream for all signals**: Simpler routing but forces every consumer to filter noise it did not ask for. Dashboard queries become expensive; CLI output becomes verbose.

**Use only OTel (no IProgressSink)**: OTel traces and metrics cover O-1 well, but the data model is not suited to ordered narrative progress events (start/item/complete per module). OTel spans don't carry human-readable operator descriptions in a queryable way.

**Write diagnostics only to the OTel log exporter**: Loses the package-portable record. Operators who receive a package from another environment get no diagnostic history.

## Consequences

- Every module must inject `IProgressSink` (optional) and emit `ProgressEvent` at start, per item (or per ≤50 batch), and completion.
- Every module must instrument with `ActivitySource.StartActivity` spans and `IMigrationMetrics` calls.
- `ILogger` writes are captured by `PackageLoggerProvider` and flushed to `.migration/runs/<runId>/logs/diagnostics.ndjson` before the job signals terminal state.
- The Control Plane exposes three distinct endpoints: `/progress?follow=true` (SSE), `/diagnostics?follow=true` (SSE), and `/telemetry` (polling).
- The CLI progress display reads metrics from `GET /jobs/{id}/telemetry` (Channel 1 polling) and stage updates from `GET /jobs/{id}/progress?follow=true` (Channel 2 SSE). It must not read from an in-process `IProgressSink`.
- The TUI reads the same three endpoints. It must not wire directly to any in-process sink.

## Amendment — Iron-Comms Implementation (Phases A-E, 2026-06-30)

The three-channel model remains the correct conceptual boundary. The transport layer has been hardened:

**Agent → ControlPlane transport (Phase C):**
The former 5 separate HTTP paths (`ControlPlaneProgressSink`, `ControlPlaneTelemetryClient`, `ControlPlaneLoggerProvider`, plus `/complete` and `/fail`) have been replaced by a single `UnifiedWorkerEventWriter` (`BackgroundService`). All telemetry — O-2 progress events, O-3 diagnostic records, task lists, metrics snapshots, and terminal signals — is batched into `WorkerEventBatch` (≤50 events or 500 ms) and POSTed to `POST /workers/{workerId}/events`. The CP dispatches each `WorkerEventKind` to the appropriate store. Retries on 429 (2 s) and other failures (exponential backoff, up to 5 attempts). No silent data loss.

**ControlPlane storage (Phase D):**
O-2 and O-3 stores (`JobProgressStore`, `DiagnosticLogStore`) replaced their ring buffers (DropOldest, cap 1000) with append-only logs (`List<T>` + `ReaderWriterLockSlim`). `GetSnapshot(jobId, fromSeq)` enables full history replay for reconnecting clients. Cap is 50,000 events with a warning, not silent eviction.

**CLI → ControlPlane stream (Phase E):**
The CLI's former 4-task fan-out (2 SSE connections + 2 polling loops) has been replaced by a single `GET /jobs/{jobId}/stream` SSE connection (`JobStreamController`). The stream multiplexes O-2 progress and O-3 diagnostics — subscribing before replaying history to avoid races, then driving live subscriber channels with a `Task.WhenAny` loop and a 15-second heartbeat. Closes with `event: job-ended` or `event: job-failed`.

**Legacy endpoint removal (2026-07-01):**
The seven per-lease telemetry endpoints (`POST /agents/lease/{leaseId}/progress`, `/complete`, `/fail`, `/metrics`, `/snapshot`, `/tasks`, `/diagnostics`) and the agent-side classes named above (`ControlPlaneProgressSink`, `ControlPlaneTelemetryClient`/`IControlPlaneTelemetryClient`, and the `ControlPlaneLoggerProvider` HTTP fallback) have been deleted outright — there are no compatibility shims. `POST /workers/{workerId}/events` (`WorkerEventsController`) is the sole agent-to-CP telemetry ingestion point, and `GET /jobs/{jobId}/stream?from={seq}` is the unified CP-to-CLI stream.

The logical three channels (O-1 OTel, O-2 Progress, O-3 Diagnostics) are unchanged. Only the wire transport from agent to CP and from CP to CLI has changed.

## Related

- [docs/architecture.md](../architecture.md) — component overview
- [.agents/30-context/domains/telemetry-model.md](../../.agents/30-context/domains/telemetry-model.md) — detailed telemetry model
- [.agents/20-guardrails/domains/observability-requirements.md](../../.agents/20-guardrails/domains/observability-requirements.md) — O-1 through O-4 requirements
- Driving spec: `specs/007-observability-logging/spec.md`


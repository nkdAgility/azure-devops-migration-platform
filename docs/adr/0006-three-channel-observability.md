# ADR 0006 — Three-Channel Observability

## Status

Accepted

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

## Related

- [docs/architecture.md](../architecture.md) — component overview
- [.agents/30-context/domains/telemetry-model.md](../../.agents/30-context/domains/telemetry-model.md) — detailed telemetry model
- [.agents/20-guardrails/domains/observability-requirements.md](../../.agents/20-guardrails/domains/observability-requirements.md) — O-1 through O-4 requirements
- Driving spec: `specs/007-observability-logging/spec.md`


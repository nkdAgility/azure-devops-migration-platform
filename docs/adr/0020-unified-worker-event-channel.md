# ADR 0020 — Unified Worker-Event Channel

## Status

Accepted (2026-07-01)

Promotes the iron-comms amendments recorded in [ADR-0006](0006-three-channel-observability.md) and [ADR-0010](0010-plan-driven-dag-execution.md) to a first-class decision. The logical three-channel observability model of ADR-0006 is unchanged; this ADR owns the wire transport.

## Context

Agent-to-Control-Plane telemetry originally used seven per-lease HTTP endpoints (`POST /agents/lease/{leaseId}/progress`, `/complete`, `/fail`, `/metrics`, `/snapshot`, `/tasks`, `/diagnostics`), each with its own client class (`ControlPlaneProgressSink`, `ControlPlaneTelemetryClient`, `ControlPlaneLoggerProvider` HTTP fallback). The CLI consumed job telemetry through a four-task fan-out: two SSE connections plus two polling loops.

This shape had structural problems:

1. **No shared delivery guarantees.** Each path implemented (or omitted) its own retry, ordering, and failure handling. Signals could be silently lost or arrive out of order relative to each other.
2. **Ring-buffer storage lost history.** Control Plane progress and diagnostic stores evicted oldest events (DropOldest, cap 1000), so a client connecting late — or reconnecting after a network blip — could never recover the full record.
3. **Termination races.** Terminal signals (`/complete`, `/fail`) travelled on a different path than the telemetry they concluded, producing stream-abort races at job termination.
4. **Per-signal endpoint sprawl.** Adding a telemetry kind meant a new endpoint, a new client class, and new CLI consumption logic.

## Decision

All agent-originated telemetry flows through **one batched, acknowledged, sequence-numbered channel**, and all client-consumed job events flow through **one replayable SSE stream**.

**Agent → Control Plane:**

1. `UnifiedWorkerEventWriter` (a `BackgroundService`) is the only agent-to-CP telemetry transport, for both the net10 `JobAgentWorker` and the net481 `TfsJobAgentWorker`.
2. Every signal — progress (O-2), diagnostics (O-3), metrics snapshots, task lists, heartbeat payloads, and terminal signals — is a `WorkerEvent` with a monotonic per-worker `seq` and a `kind` (`heartbeat | progress | diagnostic | metrics | snapshot | tasks | terminal`).
3. Events are batched (≤50 events or 500 ms, whichever first) into a `WorkerEventBatch` and POSTed to `POST /workers/{workerId}/events` (`WorkerEventsController`), the sole ingestion endpoint. Terminal events bypass the batch timer and flush immediately.
4. Delivery is acknowledged (`WorkerEventAck { lastAcceptedSeq }`), never fire-and-forget. Failed batches retry with exponential backoff up to 5 attempts; HTTP 429 is honoured with a 2-second back-off. No silent loss.
5. The only other agent-originated HTTP calls are lease acquisition (`GET /agents/lease`) and the 15-second heartbeat (`POST /agents/lease/{leaseId}/heartbeat`).

**Control Plane storage:**

6. `JobProgressStore` and `DiagnosticLogStore` are append-only per job. Events are never evicted; a safety cap (default 50,000 per job) discards further events with a warning rather than silently dropping history. `GetSnapshot(jobId, fromSeq)` supports full-history replay.

**Control Plane → CLI/TUI:**

7. `GET /jobs/{jobId}/stream?from={seq}` (`JobStreamController`) is the unified SSE stream. It multiplexes progress and diagnostics, subscribes before replaying history (no gap races), replays the append-only log from `seq`, emits a heartbeat comment every 15 seconds, and closes with `event: job-ended` or `event: job-failed`.
8. Clients auto-reconnect with replay from the last observed sequence, so a dropped connection loses nothing.
9. Task lists and metrics remain polled via `GET /jobs/{id}/bootstrap` and `GET /jobs/{id}/telemetry`.

**Removal:**

10. The seven per-lease endpoints and their client classes are deleted outright — no compatibility shims. They must not reappear.

## Alternatives Considered

**Keep per-signal endpoints and harden each individually**: Rejected. Multiplies retry/ordering/ack logic across seven paths and leaves cross-signal ordering unsolved (a terminal signal could still overtake the progress events it concludes).

**Message broker (queue) between agent and Control Plane**: Provides delivery guarantees but adds an infrastructure dependency the platform deliberately avoids; the package-plus-HTTP model keeps single-binary deployments viable.

**Keep ring buffers with larger caps**: Rejected. Any eviction breaks replay-on-reconnect; append-only with an explicit warned cap makes the failure mode visible instead of silent.

## Consequences

- Adding a new telemetry kind is additive: a new `WorkerEventKind` member and a CP dispatch case — no new endpoint, client class, or CLI connection.
- Cross-signal ordering is guaranteed by the per-worker sequence; termination races at job end are eliminated because terminal events travel in-band.
- Reconnecting clients (CLI, TUI, or any API consumer) recover full history via `?from={seq}` replay.
- Agent code must never bypass `UnifiedWorkerEventWriter` for telemetry; CLI/TUI code must never consume per-signal endpoints (they no longer exist).
- Older CLI builds are served by documented legacy read shims where noted in [docs/observability.md](../observability.md); agents have no shims.

## Enforced By

- `.agents/10-contracts/specs/observability-transport-contract.md` — canonical wire schema and semantics
- `.agents/20-guardrails/domains/observability-requirements.md`
- `.agents/20-guardrails/domains/control-plane-rules.md`

## Related

- [ADR-0006](0006-three-channel-observability.md) — the logical three-channel model this transport carries (amended by this ADR)
- [ADR-0010](0010-plan-driven-dag-execution.md) — task-list push now flows through this channel (amended by this ADR)
- [docs/observability.md](../observability.md), [docs/control-plane.md](../control-plane.md), [docs/client-integration-guide.md](../client-integration-guide.md)

# Control Plane Concept

The Control Plane is a coordination service. It manages jobs but never executes migration logic.

## Responsibilities

- **Job admission** — validates the job config and creates the job record.
- **Leasing** — assigns jobs to available agents; maintains exclusive lease per job.
- **Heartbeat management** — agents send periodic heartbeats to renew their lease.
- **Telemetry** — stores and exposes aggregate metric counters from agents.
- **Progress streams** — proxies SSE progress events from agents to clients.
- **Diagnostics** — stores and exposes structured log events from agents.
- **Entitlement enforcement** — checks usage rights at admission and lease renewal.

## What the Control Plane Does NOT Do

- Does not execute migration phases (Inventory, Export, Prepare, Import, Validate).
- Does not write package artefacts.
- Does not hold source or target credentials directly (they travel in the encrypted config payload).
- Does not maintain authoritative progress state — the package checkpoint is authoritative.

## Agent Lifecycle

1. Agent polls `GET /jobs/available` to find a queued job.
2. Agent calls `POST /jobs/{id}/lease` to acquire a lease.
3. Agent sends heartbeats via `PUT /jobs/{id}/heartbeat` every 30 seconds.
4. Agent reports progress via `POST /jobs/{id}/progress`.
5. Agent reports telemetry via `POST /jobs/{id}/telemetry`.
6. Agent completes with `POST /jobs/{id}/complete` or `POST /jobs/{id}/fail`.

## API Channels

| Endpoint | Purpose |
|---|---|
| `GET /jobs/{id}/progress?follow=true` | SSE stream of `ProgressEvent` objects |
| `GET /jobs/{id}/telemetry` | Current aggregate `JobMetrics` snapshot |
| `GET /jobs/{id}/diagnostics?follow=true` | SSE stream of structured log events |

## Related

- [.agents/guardrails/control-plane-rules.md](../guardrails/control-plane-rules.md) — enforced constraints
- [docs/control-plane.md](../docs/control-plane.md) — full explanation for operators/contributors
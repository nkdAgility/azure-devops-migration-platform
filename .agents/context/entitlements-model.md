# Entitlements Model

Compressed entitlements model for agents. Billing and licence detail is encapsulated in the Control Plane — modules must never check entitlements directly.

## Control Plane Admission Authority

The Control Plane checks entitlements at:
1. **Job admission** (`POST /jobs`) — verifies the org is licensed and within usage limits.
2. **Lease renewal** (`PUT /jobs/{id}/heartbeat`) — re-checks active licence during long-running jobs.
3. **Unit-of-work boundaries** — high-throughput operations may check per-batch.

## Entitlement Snapshot

When a job is admitted, the Control Plane captures an entitlement snapshot. This snapshot is used throughout the job's lifecycle to avoid repeated external calls.

## Module Ignorance

Modules must not:
- Call billing APIs.
- Check licence state.
- Gate execution on entitlement flags.

Entitlement enforcement is the exclusive responsibility of the Control Plane. A module that has been admitted to run will run.

## Usage Deltas and Idempotency

Usage reporting is idempotent — resubmitting the same job ID does not increment usage twice. Usage deltas are calculated by the Control Plane based on artefact counts from module telemetry.

## Lease and Heartbeat Model

- Each job has exactly one active agent lease at a time.
- Leases expire if heartbeats are not received within the timeout window.
- An expired lease allows another agent to claim the job.
- The agent must stop processing if its heartbeat returns `LeaseExpired`.

## Related

- [control-plane-concept.md](./control-plane-concept.md) — Control Plane overview
- [job-lifecycle.md](./job-lifecycle.md) — full job lifecycle
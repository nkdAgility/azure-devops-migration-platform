# ADR 0004 — Control Plane Does Not Execute Migrations

## Status

Accepted

## Context

The Control Plane is a coordination service that must remain available and stateless for job management. Coupling migration execution to the Control Plane would make it a stateful, heavyweight process that cannot scale independently.

## Decision

The Control Plane coordinates jobs but never executes migration logic. It:

- Receives job submissions
- Assigns jobs to available agents via lease
- Tracks job state (Queued, Running, Completed, Failed)
- Stores and exposes telemetry, progress, and diagnostics
- Manages agent leases and heartbeats

Migration execution is performed exclusively by the Migration Agent and TFS Migration Agent.

## Alternatives Considered

**Execute migrations in the Control Plane**: Simpler deployment, but the Control Plane becomes a monolith, cannot scale execution independently, and becomes harder to operate.

## Consequences

- The Control Plane may be deployed as a lightweight service.
- Agents are stateless workers that can be scaled independently.
- The Control Plane must not write package artefacts.
- The CLI and TUI communicate with the Control Plane only — they have no direct connection to agents.

## Related

- [docs/control-plane.md](../control-plane.md) — Control Plane responsibilities
- [.agents/20-guardrails/domains/control-plane-rules.md](../../.agents/20-guardrails/domains/control-plane-rules.md) — enforced constraints

# ADR 0005 — Agent-Only Package Write Access

## Status

Accepted

**Amendment (feature 025.1-fold-to-job):** Configuration now travels as `Job.ConfigPayload`, and the agent writes `migration-config.json` after lease acquisition. The earlier CLI pre-submission write design was superseded.

## Context

The migration package is the source of truth. Allowing multiple components to write to it risks corruption, non-determinism, and data residency violations.

## Decision

Only the Migration Agent and the TFS Migration Agent may write package artefacts. All other components — CLI, TUI, Control Plane, ControlPlaneHost — are read-only with respect to the package.

## Alternatives Considered

**Allow CLI to write directly**: Simpler for some use cases, but mixes operator-side and agent-side writes, makes data residency guarantees impossible, and creates race conditions.

**Allow Control Plane to cache artefacts**: Creates a copy of customer data in service-controlled storage without explicit configuration.

## Consequences

- Data residency is enforceable: the package stays where the operator placed it unless explicitly moved.
- CLI/TUI code must never reference `IArtefactStore` for writes.
- The Control Plane must not cache or write artefact data.
- The data residency boundary is the package working directory.
- The CLI serialises configuration into `Job.ConfigPayload` but does not write `migration-config.json` itself.
- The agent materialises `Job.ConfigPayload` to `migration-config.json` at the package root before any module executes.

## Related

- [.agents/20-guardrails/core/architecture-boundaries.md](../../.agents/20-guardrails/core/architecture-boundaries.md) — enforced constraints (Rule 23)
- [docs/architecture.md](../architecture.md#data-residency--agent-only-write-access) — data residency section

# Orchestrator Model

Compressed context for orchestrators as first-class runtime architecture.

## Canonical Runtime Chain

`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

## Purpose

Orchestrators define the authoritative runtime workflow for a concern: sequencing, stage boundaries, phase gates, and resume boundaries.

## Canonical Contract

- `.agents/10-contracts/specs/orchestrator-contract.md`
- `.agents/10-contracts/surface-catalog.yaml` (`orchestration`)
- `.agents/10-contracts/seam-catalog.yaml` (`orchestration`)

## Boundary Rules

- Orchestrators are composition and sequencing only.
- Concern behavior must be invoked through canonical abstractions/seams.
- Adapter-specific external mechanics remain in adapter implementations.
- Package/state writes go through package boundary and persistence abstractions.
- Module orchestrator abstractions use one symmetric phase shape (`ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`).
- Adapter/runtime differences do not change orchestrator abstraction method shape.

## Runtime Shape

1. `JobAgentWorker` dispatches to an orchestrator contract.
2. Orchestrator applies workflow order and phase/stage policy.
3. Orchestrator invokes concern abstractions/seams.
4. Adapter implementations execute external ADO/TFS/Simulated mechanics behind abstractions.
5. Orchestrator persists cursor/state/progress through package abstractions.
6. Orchestrator emits stage outcomes and telemetry.

## Invariants

- Preserve Source -> Files -> Target.
- Preserve streaming and lexicographic traversal semantics where required.
- Preserve cursor-based deterministic resume.
- Do not introduce parallel orchestration runtime entrypoints for the same concern.
- Keep module wrappers thin: orchestration sequencing must not drift back into module wrappers.
- Keep abstraction shape stable: phase methods remain symmetric across module orchestrators unless contract change governance is satisfied.

## Related

- `.agents/20-guardrails/core/architecture-boundaries.md`
- `.agents/20-guardrails/core/surface-usage.md`
- `.agents/20-guardrails/domains/migration-rules.md`
- `.agents/20-guardrails/domains/connector-rules.md`
- `.agents/10-contracts/specs/package-boundary-contract.md`
- `.agents/10-contracts/specs/package-persistence-contract.md`

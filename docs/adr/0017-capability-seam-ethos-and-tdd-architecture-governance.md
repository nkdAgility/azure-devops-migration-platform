# ADR 0017 — Capability Seam Ethos and TDD Architecture Governance

## Status

Accepted

## Context

Architecture drift was repeatedly appearing as post-implementation cleanup work: duplicated concern engines in multiple layers, alternative runtime entry points beside existing seams, and inconsistent policy placement between modules, orchestrators, and extensions.

The platform needs architecture-review tenets to shape design at creation time, not only as retrospective checks.

## Decision

The repository adopts a seam-first governance model across all concerns:

1. one canonical business seam per concern
2. one public reusable runtime surface per seam
3. thin adapters/extensions that own phase/slice policy only
4. centralized concern logic behind the seam, with no parallel runtime entry points

Test-first workflow is the primary control mechanism for this decision:

- design artifacts must declare a **Capability Seam Decision** before code
- RED/GREEN/REFACTOR work must preserve seam ownership and avoid policy/engine duplication
- completion evidence must include architecture perspective checks and seam integrity checks

## Consequences

- Seam ownership is now an explicit design input, not an implicit refactoring outcome.
- Modules, orchestrators, analysers, and extensions must consume canonical seam contracts rather than creating alternative engines.
- Guardrails and TDD workflow now reject drift patterns earlier in delivery.
- New architecture-review work focuses on deepening seams and naming clarity rather than recovering from structural drift.

## Enforced By

- `.agents/guardrails/capability-ethos-rules.md`
- `.agents/context/capability-seam-contract.md`
- `.agents/guardrails/test-first-workflow.md`
- `.agents/guardrails/definition-of-done.md`

## Related

- [ADR-0012](0012-imodule-five-phase-contract.md)
- [ADR-0016](0016-unified-package-access.md)
- [Architecture overview](../architecture.md)

# Capability Seam Contract

Compressed implementation-facing contract for preventing architectural drift across concerns.

## Purpose

This file explains how to apply the capability ethos in day-to-day design and implementation without hard-coding every future architecture decision.

Normative rules are in:

- `../../20-guardrails/core/architecture-perspectives-ethos.md`
- `../../20-guardrails/core/capability-ethos-rules.md`
- `../../../docs/adr/0017-capability-seam-ethos-and-tdd-architecture-governance.md`

## Canonical Shape

For each concern, use this shape:

1. **Canonical seam owner** — one owner for the concern's core behavior.
2. **Public reusable contract** — one stable surface consumed by runtime callers outside the concern.
3. **Thin adapter facades** — slice/phase policy wrappers around the seam.
4. **Centralized core engine** — reusable concern logic behind the seam, not copied into adapters.

## Responsibility Split

| Layer | Owns | Must not own |
| --- | --- | --- |
| Canonical seam (`XxxTool`, or equivalent) | concern logic and stable contract | phase-specific orchestration policy |
| Adapter / extension policy (`Xxx...Policy`) | when to invoke seam, skip/fail behavior, checkpoint interactions | alternate concern engine implementation |
| Module / orchestrator wrapper | composition, wiring, sequencing | duplicate transform/lookup engine logic |

## Design Checklist (before code)

For every new concern or major extension, declare:

- concern boundary
- canonical seam owner
- canonical public surface
- adapter responsibilities
- prohibited parallel entry points

If any of these are undefined, design is incomplete.

## Implementation Checklist (during code)

- Do all consumers call the canonical public surface?
- Is core concern logic implemented once?
- Are adapters thin policy facades only?
- Is naming explicit enough to reveal seam ownership?
- Has any alternate runtime entry point been introduced?

Any "no" indicates boundary drift.

## Review Checklist (before completion)

- Modular Monolith: no cross-module duplicate concern engines
- Clean/Hexagonal: dependency direction preserves seam abstraction
- Vertical Slice: slice policy in adapters, core logic in seam
- Screaming Architecture: names reveal seam and policy boundaries
- Architecture Deepening: touched scope records explicit deepening assessment

Record evidence in review/DoD outputs; do not defer.

## Example Interpretation

For concerns like node translation, identity lookup, or field transform:

- the canonical concern logic is owned by the seam surface
- modules and extensions consume that seam
- extension behavior customizes policy and orchestration, not translation engine internals

If deeper internal services are added, they remain behind the seam boundary.





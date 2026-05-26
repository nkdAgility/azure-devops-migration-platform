# Contract — Module Orchestrator Shape

## Purpose

Define the canonical abstraction shape for module orchestrators and prevent shape drift.

## Canonical Runtime Chain

`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

## Required Shape

Each module orchestrator abstraction exposes:

- `ExportAsync(...)`
- `PrepareAsync(...)`
- `ImportAsync(...)`
- `ValidateAsync(...)`

## Required Semantics

1. Shape is symmetric across module orchestrators.
2. Compile-time guards must not remove phase methods from abstraction contracts.
3. Runtime/adapter differences are implemented in behavior, not abstraction shape.
4. Module wrappers delegate sequencing to orchestrators and do not own orchestration loops.

## Compliance Evidence

- Abstraction signatures in `Abstractions.Agent` follow required shape.
- Module wrappers do not instantiate concrete orchestrators inline.
- Phase flow remains deterministic and stage-visible at runtime.

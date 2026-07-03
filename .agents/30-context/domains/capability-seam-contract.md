# Capability Seam Contract (Summary)

Compressed context for the seam-first governance model. Binding rules:
[`capability-ethos-rules.md`](../../20-guardrails/core/capability-ethos-rules.md).
Decision record: [`docs/adr/0017`](../../../docs/adr/0017-capability-seam-ethos-and-tdd-architecture-governance.md)
(WorkItems application: [`docs/adr/0019`](../../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md)).

## Core Concept

Every concern (node translation, identity lookup, field transform, …) has exactly
one **canonical business seam**: one runtime boundary, one public reusable
contract surface, one centralized engine behind it. Adapters and extensions are
thin policy facades (when to apply, skip/fail semantics, checkpoint interaction) —
they never reimplement the engine, and no concern ever gains a second parallel
runtime entry point.

Seam interfaces live in `DevOpsMigrationPlatform.Abstractions` /
`Abstractions.Agent`; implementations live in infrastructure projects. Names
scream ownership (`XxxTool` for the seam surface, `Xxx...Policy` for adapters).

## Capability Seam Decision

Before implementation, the spec/plan must declare, per concern:

- concern name
- canonical seam owner
- canonical public surface
- allowed adapter responsibilities
- prohibited parallel entry points

Missing this block is non-compliant.

## Extension Seam Ethos (when may something be an IModuleExtension)

The test is not "can this be turned off?" — it is **"if this is absent, is the
entity still whole?"** A valid extension requires all three:

1. distinct domain object with its own identity and lifecycle
2. core entity complete and correct without it
3. the extension's write is a separate operation, not part of the core entity's atomic save

Concerns failing any condition are intrinsic and go inline in the core pipeline,
governed by module options — never wrapped as an extension.

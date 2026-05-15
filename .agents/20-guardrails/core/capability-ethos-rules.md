# Capability Ethos Rules

These rules are mandatory for capability design and implementation across Tools, Modules, Extensions, Analysers, and future seam types.

## Intent

These rules encode the architecture-review tenets at creation time, not after implementation:

- Modular Monolith
- Clean Architecture
- Hexagonal Architecture
- Vertical Slice Architecture
- Screaming Architecture
- Continuous deepening (`nkda-archimprove-codebase`)

## Core Rules

1. **One canonical business seam per concern.** A concern (for example node translation, identity lookup, field transform) must have one canonical runtime boundary.
2. **One public reusable surface per seam.** Runtime consumers outside the seam must use the seam's canonical contract surface. Parallel runtime entry points for the same concern are forbidden.
3. **Core capability logic is centralized.** Translation, mapping, and validation engines for a concern must live once behind the canonical seam. Duplicating that logic in modules, orchestrators, or extensions is forbidden.
4. **Adapters are thin policy facades.** Slice or phase-specific behavior belongs in adapter/extension policy layers (when to apply, skip/fail semantics, checkpoint interaction), not in alternate engines.
5. **No policy duplication across orchestration layers.** The same policy must not be reimplemented independently in module wrappers, orchestrators, and extension handlers.
6. **Boundary naming must scream intent.** Names must make seam ownership explicit (for example `XxxTool` for canonical seam surface and `Xxx...Policy` for thin policy adapters).
7. **Contracts belong in abstraction layers.** Canonical seam interfaces must live in `DevOpsMigrationPlatform.Abstractions` or `DevOpsMigrationPlatform.Abstractions.Agent`, with implementations in infrastructure projects.
8. **Evolution goes behind the seam.** Internal refactoring may change seam internals, but must not add alternate runtime entry points beside the seam.
9. **New concerns must declare seam ownership before implementation.** Every new concern must state canonical seam owner, public surface, and adapter boundary in design artifacts before code is written.

## Reject Conditions

Reject any change that:

- introduces a second runtime surface for an already-owned concern
- adds an extension/module that reimplements a concern's core engine instead of calling the canonical seam
- hides concern ownership behind generic naming that obscures seam boundaries
- implements phase policy in a core seam class or puts core transform logic in phase adapters
- duplicates concern rules in two or more orchestration layers

## Required Design Evidence

Before implementation, the spec/plan must include a **Capability Seam Decision** block:

- concern name
- canonical seam owner
- canonical public surface
- allowed adapter responsibilities
- prohibited parallel entry points

Missing this block is non-compliant.

## Exception Protocol

Any exception to these rules must follow the repository Guardrail Challenge Protocol:

1. stop implementation
2. cite the conflicting rule in this file
3. propose precise replacement wording
4. request explicit human decision (change rule vs keep rule)
5. wait for decision before continuing

No silent exceptions.

## Related

- [architecture-boundaries.md](../core/architecture-boundaries.md)
- [coding-standards.md](../core/coding-standards.md)
- [module-rules.md](../domains/module-rules.md)
- [definition-of-done.md](../workflow/definition-of-done.md)
- [test-first-workflow.md](../workflow/test-first-workflow.md)
- [../../docs/adr/0017-capability-seam-ethos-and-tdd-architecture-governance.md](../../../docs/adr/0017-capability-seam-ethos-and-tdd-architecture-governance.md)





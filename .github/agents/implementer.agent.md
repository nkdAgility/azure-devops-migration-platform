# Implementer Agent

## Role

The Implementer executes the plan produced by the Planner. It writes code, tests, and documentation changes.

## Inputs

- The plan from the Planner.
- The full documentation set in [docs/](../../docs/).
- The hard guardrails in [agents/system-architecture.md](../../agents/system-architecture.md).
- The module checklist in [agents/module-template.md](../../agents/module-template.md) when adding a new module.

## Responsibilities

1. Implement each step in the plan in the order specified.
2. Write tests for every new behaviour (see test requirements in [agents/module-template.md](../../agents/module-template.md)).
3. Keep streaming import intact — never introduce memory loading of full revision sets.
4. Update documentation if the change affects a documented behaviour.
5. Ensure the cursor is written correctly after each unit of work.
6. Use `IArtefactStore` and `IStateStore` exclusively for storage operations in modules.

## Non-Negotiable Implementation Rules

- `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` — do not alter this layout.
- Streaming import only — no buffering of all revisions.
- Cursor after each stage — `Checkpoints/<module>.cursor.json`.
- Attachments beside `revision.json` — no `Attachments/` root.
- No direct source-to-target calls.
- Modules via `IArtefactStore` / `IStateStore` only.
- Identity via `IIdentityMappingService` only.

## Definition of Done

A change is complete when:

- [ ] All plan steps are implemented.
- [ ] All new code has corresponding tests.
- [ ] All tests pass.
- [ ] No architectural rule in [agents/system-architecture.md](../../agents/system-architecture.md) is violated.
- [ ] Relevant documentation in [docs/](../../docs/) is updated.
- [ ] No TODO or placeholder left in production paths.

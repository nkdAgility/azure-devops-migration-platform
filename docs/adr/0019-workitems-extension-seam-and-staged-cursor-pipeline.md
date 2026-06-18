# ADR 0019 — WorkItems Extension Seam and Staged Cursor Pipeline

## Status

Implemented. Corrected in-session after initial implementation revealed a misclassification of Links
and Attachments as extensions. See **Corrective Refinement** below.

## Context

`WorkItemsModule` was not a thin module: `CaptureAsync` held the inventory loop inline, the constructor
was a ~30-dependency composition root, and per-revision capability dispatch was a boolean-flag `if (ext.X)`
switch driven by a `WorkItemsModuleExtensions` god-object. The capability set was baked into a fixed
`CursorStage` enum checkpoint sequence, meaning adding a capability required editing the enum and the
resume logic.

This was the same flag-dispatch anti-pattern the 039 extension model was created to dissolve.

## Decision

### Core decisions (in force)

1. **Single extension seam via `IModuleExtension`.** Per-revision capabilities that meet the Extension
   Seam Ethos (see below) become `IModuleExtension` implementations with their own `IOptions<T>`, both
   export and import directions, consuming `WorkItemExtensionContext : IExtensionContext`.

2. **`WorkItemResolutionProcessor` is the canonical per-revision sub-orchestrator.** It owns the loop,
   the cursor, metrics, and progress, and drives an ordered named stage pipeline: core stages
   (`CreatedOrUpdated`, `AppliedFields`) plus ordered enabled `IModuleExtension` stages.

3. **Cursor dispatch is name-keyed; the on-disk cursor format is preserved.** Stages are iterated as
   an ordered list and resume matches by stage name, reusing existing marker strings. Existing in-flight
   packages resume unchanged; a new capability adds a new marker string (additive, backward-compatible).
   No version bump, no upgrader.

4. **EmbeddedImages is a field-rewrite contributor, not a stage.** It is a field-value rewrite inside
   the core `AppliedFields` stage, not a separable extension.

5. **The module is thin.** Inventory lives in the orchestrator. Object-graph construction lives in DI.
   Phase methods delegate.

6. **Revisions are core, not an extension.** The revision stream is the loop spine.

7. **Revision save is a single atomic PATCH.** Fields, link relations, and attachment relations are
   sent in one `JsonPatchDocument` to `PATCH /_apis/wit/workItems/{id}`. Attachment binaries are
   uploaded first (separate endpoint) to obtain URLs, which then feed into the unified PATCH.

### Extension Seam Ethos (governs what may be an IModuleExtension)

A valid `IModuleExtension` for WorkItems requires **all** of the following:

- The concern operates on a **distinct domain object** with its own identity and lifecycle — not a
  property or sub-object of a `WorkItemRevision`.
- The core entity is **complete and correct** without the extension having run.
- The extension's write is a **separate operation** — not part of the work item's atomic save.

This test is not "can this be turned off?" It is "if this is absent, is the entity still whole?"

Full policy text is in `.agents/20-guardrails/core/capability-ethos-rules.md` § Extension Seam Ethos.

### Corrective Refinement — Links and Attachments are NOT extensions

The initial implementation incorrectly extracted `LinksWorkItemExtension` and
`AttachmentsWorkItemExtension` as `IModuleExtension` implementations. The Extension Seam Ethos test
rejects both:

- **Links** are a structural property of a `WorkItemRevision`. The revision is incomplete without its
  links. The ADO API models links as `/relations` on the work item resource — part of the same document
  written by the revision PATCH.
- **Attachments** are the same: attachment relations are part of the work item document. Their binary
  content is uploaded separately to obtain a URL, but the relation itself is written in the same PATCH
  as fields and links.

Both were deleted. Their replay logic is unconditional core behaviour in `WorkItemResolutionProcessor`
and `WorkItemExportOrchestrator`. No configuration can disable core entity concerns.

### CommentsWorkItemExtension — the only valid WorkItems extension

`WorkItemComment` is a distinct ADO entity (`/_apis/wit/workItems/{id}/comments`) with its own ID and
lifecycle. A work item is complete and correct without comments. The write is a separate `POST` call —
not part of the revision PATCH. `CommentsWorkItemExtension` passes all three seam ethos tests and
remains the sole `IModuleExtension` for WorkItems.

## Capability Seam Decision

| Field | Value |
|---|---|
| **Concern** | Per-work-item capability application (comments) during export/import |
| **Canonical seam owner** | `WorkItemResolutionProcessor` (per-revision sub-orchestrator) |
| **Canonical public surface** | `IModuleExtension` over `WorkItemExtensionContext : IExtensionContext` |
| **Allowed adapter/policy responsibilities** | Enablement (`IsEnabled` from own `IOptions<T>`), order, cursor interaction |
| **Prohibited parallel entry points** | No second per-revision dispatch; no reimplementation of create/update, id-map, field, or translation engines; no inline duplicate of any extension concern |

## Compatibility

- **Cursor / package format**: unchanged. Existing markers preserved; new capability markers are additive.
- **API call shape**: the single-PATCH consolidation changes the call shape from three separate PATCHes
  (fields, links, attachments) to one. Call count per revision decreases; semantics are identical.

## Architecture-perspective evidence

- **Modular Monolith**: only genuinely separable concerns (comments) are extensions; intrinsic concerns are inline.
- **Clean**: contracts in abstractions; engines in infra.
- **Hexagonal**: `CommentsWorkItemExtension` is a port into comment-specific behaviour; tools/adapters are the driven side.
- **Vertical Slice**: comments facet is end-to-end (export + import + config) in one type.
- **Screaming**: `CommentsWorkItemExtension` declares intent; `WorkItemResolutionProcessor` owns the loop.
- **Architecture Deepening**: flag-dispatch dissolved; future capability = add an extension iff it passes the seam ethos test.

## Consequences

- Adding a work-item capability that passes the Extension Seam Ethos requires no core edit.
- Adding a concern that fails the seam ethos test must go inline in the core pipeline — no extension wrapper.
- `WorkItemResolutionProcessor` is the single canonical per-revision seam owner.
- Extensions are registered as `IModuleExtension` in DI and flow via `IEnumerable<IModuleExtension>`;
  orchestrators must not construct extensions with `new` or hold a `?? new` fallback.

## Enforced By

- `.agents/10-contracts/specs/execution-contract.md`
- `.agents/20-guardrails/core/capability-ethos-rules.md` — Extension Seam Ethos section
- `.agents/20-guardrails/core/architecture-boundaries.md`
- `.agents/20-guardrails/workflow/test-first-workflow.md`

## Related

- [ADR-0003](0003-cursor-based-checkpointing.md) — cursor format this ADR preserves
- [ADR-0012](0012-imodule-five-phase-contract.md)
- [ADR-0017](0017-capability-seam-ethos-and-tdd-architecture-governance.md)
- [specs/039-team-board-settings/spec-addendum-workitems-module-refactor.md](../../specs/039-team-board-settings/spec-addendum-workitems-module-refactor.md)

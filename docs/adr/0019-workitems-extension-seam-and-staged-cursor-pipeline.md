# ADR 0019 — WorkItems Extension Seam and Staged Cursor Pipeline

## Status

Accepted — operator consent given in-session (Class C consent evidence per
`.agents/10-contracts/change-classes.yaml`). **No code implemented yet.** Implementation, when it
begins, is mandatory test-first (RED→GREEN→REFACTOR per `test-first-workflow.md`), parity-gated.

## Context

`WorkItemsModule` is not a thin module: `CaptureAsync` holds the inventory loop inline, the constructor
is a ~30-dependency composition root, it holds tools only to forward them, and it carries a dead
`ApplyImportReplayLevers` method (live copy lives in `WorkItemsImportRuntime`). Removing it is the
first Class A / Rule 30 remediation increment — to be done failing-test-first like everything else.

The per-work-item capability logic (Links, Attachments, Comments, EmbeddedImages) does **not** live in
`WorkItemsOrchestrator` — that type only configures and delegates. The real per-revision loop and the
capability dispatch live in `RevisionFolderProcessor`, interleaved with a **fixed `CursorStage` enum**
checkpoint sequence (`CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed`).

Two anti-patterns result:
1. Capability dispatch is a boolean-flag `if (ext.X)` switch (`WorkItemsModuleExtensions` god-object).
2. The capability set is baked into the **checkpoint contract** (`CursorStage` enum) — adding a
   capability means editing the enum and the resume logic.

This is the same flag-dispatch anti-pattern the 039 extension model
(`.agents/30-context/architecture/execution-model.md`) was created to dissolve.

## Decision

1. **Single extension seam.** Links, Attachments, and Comments become `IModuleExtension` implementations
   (the canonical extension contract — no `I{Domain}Extension`), each with its own `IOptions<T>`, both
   directions, consuming `WorkItemExtensionContext : IExtensionContext`.

2. **`RevisionFolderProcessor` is the canonical per-revision sub-orchestrator.** It owns the loop, the
   cursor, metrics, and progress, and drives an **ordered, named stage pipeline**: core stages
   (`CreatedOrUpdated`, `AppliedFields`, `Completed`) plus the ordered enabled `IModuleExtension`
   stages. No parallel per-revision dispatch path is introduced.

3. **Cursor dispatch becomes name-keyed; the on-disk cursor format is preserved.** Stages are iterated
   as an ordered list and resume matches by stage **name**, reusing the existing marker strings as the
   stage names. Existing in-flight packages resume unchanged; a new capability adds a new marker
   string, which is additive and backward-compatible ("marker absent → run the stage"). **No version
   bump and no upgrader** — the persisted contract (Architecture-boundary Rule 9 / ADR 0003) is
   unchanged; only the dispatch mechanism changes.

4. **EmbeddedImages is a field-rewrite contributor, not a stage.** It is a field-value rewrite invoked
   *inside* the core `AppliedFields` stage (download + store + rewrite `<img>` refs — a
   FieldTransform-with-I/O), keeping field application as one cursor marker.

5. **Tools stay singular and are consumed, not reimplemented.** Extensions and core stages call
   `IWorkItemTarget`, `IIdMapStore`, `IIdentityTranslationTool`, `INodeTranslationTool`,
   `IFieldTransformTool`, and the embedded-image replay service — they must not duplicate those engines
   (capability-ethos rules 2, 3, 5).

6. **Revisions are core, not an extension.** The revision stream is the loop spine; `RevisionsEnabled`
   is retired as an extension toggle.

7. **The module becomes thin.** Inventory moves into the orchestrator; object-graph construction moves
   to DI / `WorkItemsOrchestratorFactory`; tool fields leave the module; the constructor collapses to a
   handful of dependencies; phase methods delegate.

## Capability Seam Decision

- **Concern**: per-work-item capability application (links, attachments, comments) during export/import.
- **Canonical seam owner**: `RevisionFolderProcessor` (per-revision sub-orchestrator) running an ordered
  named stage pipeline.
- **Canonical public surface**: `IModuleExtension` over `WorkItemExtensionContext : IExtensionContext`.
- **Allowed adapter/policy responsibilities**: enablement (`IsEnabled` from own `IOptions<T>`), order,
  capability gating, checkpoint interaction.
- **Prohibited parallel entry points**: no second per-revision dispatch; no reimplementation of
  create/update, id-map, field, or translation engines.

## Compatibility

- **Cursor / package format**: unchanged. Existing markers preserved; new capability markers are
  additive. No upgrader required.
- **Public contract**: `IWorkItemsOrchestrator` gains inventory ownership (Stage 1) and the extension
  list flows to the per-revision sub-orchestrator (Stages 2–4). Contract-compatibility tests pin
  import/resume parity and cursor-string stability.

### Server call-count parity (invariant + required contract test)
- **Invariant**: the refactor makes **zero** change to source/target API call volume, order, or
  shape. It is structural only. Each capability issues exactly the calls it does today.
- **Extensions consume context, never re-query**: an extension reads its data from
  `WorkItemExtensionContext` (the already-streamed revision + package). It must **not** make a fresh
  source/target call for data the core already fetched. *Core streams once; extensions consume.*
- **Required contract test**: a call-count parity test using the Simulated adapters (which capture
  every write call) asserts the per-revision target-call sequence is identical before and after the
  refactor, and that no extension introduces an additional source/target round-trip. A change in call
  count is a parity failure, not an accepted outcome.

## Architecture-perspective evidence

- **Modular Monolith**: capability boundaries become explicit, independently-configured extensions.
- **Clean**: contracts (`IModuleExtension`, `IExtensionContext`) in abstractions; engines in infra.
- **Hexagonal**: extensions are ports into per-capability behaviour; tools/adapters are the driven side.
- **Vertical Slice**: each facet (links/attachments/comments) is end-to-end (export+import+config) in one type.
- **Screaming**: `{Facet}WorkItemExtension` names declare intent; `RevisionFolderProcessor` owns the loop.
- **Architecture Deepening**: dissolves flag-dispatch and enum-baked capability set; future capability = add an extension.

## Consequences

- Adding a work-item capability requires no enum edit, no core edit, no resume-logic edit.
- `RevisionFolderProcessor` is the single canonical per-revision seam owner.
- Touching `RevisionFolderProcessor` / `WorkItemsImportRuntime` triggers Rule 30 remediation of known
  non-compliance in touched scope (enumerated during implementation, or operator-approved follow-up).
- Delivery is test-first (RED→GREEN→REFACTOR), parity-gated, one facet per commit.

## Enforced By

- `.agents/10-contracts/specs/execution-contract.md`
- `.agents/20-guardrails/core/capability-ethos-rules.md`
- `.agents/20-guardrails/core/architecture-boundaries.md`
- `.agents/20-guardrails/workflow/test-first-workflow.md`

## Related

- [ADR-0003](0003-cursor-based-checkpointing.md) — cursor format this ADR preserves
- [ADR-0012](0012-imodule-five-phase-contract.md)
- [ADR-0017](0017-capability-seam-ethos-and-tdd-architecture-governance.md)
- [specs/039-team-board-settings/spec-addendum-workitems-module-refactor.md](../../specs/039-team-board-settings/spec-addendum-workitems-module-refactor.md)

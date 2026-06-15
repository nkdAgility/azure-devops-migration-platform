# Spec Addendum — WorkItems Module Refactor (pending work)

**Status**: Stages 1–3 taxonomy remediation COMPLETE. Stage 2 cursor-engine generalisation is next.

**ADR**: [ADR 0019](../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md)

---

## What is done

- Stage 1 — module thinned; `CaptureAsync` moved to orchestrator; factory deleted; ctor collapsed.
- Increments 3–5 — Links, Attachments, Comments extracted as `IModuleExtension` (import side, `SupportsExport: false`).
- Stage 5 — tool-definition fix (rescinded wrong "tools do no I/O" rule).
- Stage 5 — import-side facet enablement migrated: processor stage gates now read each extension's own `IsEnabled`.
- Stage 3 taxonomy remediation — `WorkItemsImportRuntime`, `WorkItemStreamOrchestrator`, `WorkItemRevisionImporter` deleted; all logic consolidated into `WorkItemsOrchestrator`. Self-instantiation anti-pattern (HX-C1) eliminated. `RevisionsEnabled` early-return guard added; `commentsEnabled` local extracted from loop body.
- Stage 2 (core) — `CommentsWorkItemExtension` moved from ad-hoc inline call into the ordered `extensionStages` array with `CursorStage.AppliedComments` cursor write. `WorkItemRevisionStage` made `public`. Extension stage list made injectable via `WorkItemResolutionProcessor` ctor parameter.
- Stage 3/4 (export gating) — `AttachmentsWorkItemExtension` and `CommentsWorkItemExtension` injected into `WorkItemsOrchestrator`. `ExportAsync` reads `IsEnabled` from extension objects instead of `ext.AttachmentsEnabled` / `ext.Comments.Enabled`.

---

## Pending

### Residual god-object cleanup (Stage 2 tail / Stage 5 prerequisite)

`EmitReplaySkipVisibilityEvents` in `WorkItemsOrchestrator` reads `ext.AttachmentsEnabled` and `ext.EmbeddedImages.Enabled` from the god-object. These values are post-lever (after `ApplyReplayLevers`), so they can't trivially be replaced by reading from the processor's extension objects (which are pre-lever). Retirement requires threading levered enablement into the stage objects or into the processor factory — Stage 5 work.

---

### Stage 3/4 — Export-side facet extraction

**Blocked by**: Stage 2 (needs the generalised folder writer on the export path).

Once the export-side pipeline is generalised, set `SupportsExport: true` for `LinksWorkItemExtension`, `AttachmentsWorkItemExtension`, `CommentsWorkItemExtension` and move their export logic out of the export path inline.

EmbeddedImages export stays as a field-rewrite contributor inside the core `AppliedFields` step — not a peer pipeline stage.

---

### EmbeddedImages — residual god-object gate

`ext.EmbeddedImages.Enabled` is still read from the god-object (`WorkItemsModuleExtensions`) in:
- `EmitReplaySkipVisibilityEvents` (`WorkItemsOrchestrator.cs` ~L1033)
- `CreateArtefactContext` (`WorkItemsOrchestrator.cs` ~L1100)

This should read from a proper `EmbeddedImagesExtensionOptions` bound to the same operator config. Fold into Stage 2 cleanup or do as a standalone Stage 5 increment before Stage 2 if it's simpler.

---

### Stage 4/5 — Retire the god-object (`WorkItemsModuleExtensions`)

Once all gates are migrated off it:
- `ext.LinksEnabled` (already migrated — processor reads `_linksExtension.IsEnabled`)
- `ext.AttachmentsEnabled` (residual: `EmitReplaySkipVisibilityEvents`, export-side log — Stage 2/3)
- `ext.EmbeddedImages.Enabled` (above)
- `ext.RevisionsEnabled` — now only in the early-return guard in `WorkItemsOrchestrator.ImportAsync` and the log line. Per §9 decided model this is core, not an extension toggle; the guard is the right shape. Retire the flag from the god-object; promote to an orchestrator-level option or remove entirely.
- `ext.Comments.Enabled` — residual: export-side log and export factory call in `ExportAsync`. Migrate to `CommentsExtensionOptions.IsEnabled`.
- Non-extension config (`Query`, filters, `ResolutionStrategy`) stays — not extension-owned.

---

## Architectural decisions (still in force)

- **Revisions are core, not an extension.** The per-entity loop IS the revision stream. `RevisionsEnabled` is a mislabelled core kill-switch; the early-return guard is the correct shape.
- **EmbeddedImages is not a peer pipeline stage.** It is a field-value rewrite inside core `AppliedFields`. Modelled as a field-rewrite contributor; extract to its own `IOptions<EmbeddedImagesExtensionOptions>` but do NOT make it a cursor stage.
- **Cursor format does not change.** Stage names are the existing marker strings. No upgrader.
- **`RevisionFolderProcessor` is the sub-orchestrator.** It owns the per-revision loop + cursor. `WorkItemsOrchestrator` delegates to it; the orchestrator does not loop entities itself.
- **`WorkItemRevisionStage`** is the descriptor (`CursorName`, `IsEnabled`, `ExecuteAsync`) — already exists, already used for extension stages.

---

## Cross-references

- [execution-contract.md](../../.agents/10-contracts/specs/execution-contract.md)
- [execution-model.md](../../.agents/30-context/architecture/execution-model.md)
- [contracts/IModuleExtension.md](contracts/IModuleExtension.md)
- [ADR 0019](../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md)

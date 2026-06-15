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
- `RevisionFolderProcessor` embedded-image gate — `ext.EmbeddedImages.Enabled` replaced by injected `EmbeddedImagesExtensionOptionsConfig`; `ImportEmbeddedImagesContext.BuildProcessor` updated to forward `Extensions.EmbeddedImages`.
- Import startup log — `ext.LinksEnabled` replaced by `_options.Value.Extensions.Links.Enabled`.
- `RevisionsEnabled` — removed from god-object (was never read; early-return guard and startup log read `_options.Value.Extensions.Revisions.Enabled`).
- Dead `FromModule` factory and all 8 private helpers deleted from `WorkItemsModuleExtensions`.
- Loop-level `commentsEnabled` gate — migrated from `ext.Comments.Enabled` to `_commentsExtension.IsEnabled`; `Comments` property removed from god-object; dead `Comments = ext.Comments` line removed from `ApplyReplayLevers`.
- Taxonomy rename — `AttachmentReplayService` → `AttachmentReplayTool`, `EmbeddedImageReplayService` → `EmbeddedImageRewriteTool`. `Service` is not a permitted role noun.
- HX-C1 DI wiring — `AttachmentsWorkItemExtension` and `CommentsWorkItemExtension` now resolved via `GetRequiredService` in the `WorkItemsOrchestrator` factory lambda; `??` fallback no longer fires in production.
- DC-C1 dead parameter — `WorkItemsModuleExtensions ext` removed from `IWorkItemResolutionProcessor.ProcessAsync`; was declared but never read.
- SA-H1 file rename — `RevisionFolderProcessor.cs` → `WorkItemResolutionProcessor.cs` to match class name.
- HX-C2 NullLogger injection — `AttachmentsWorkItemExtension` now receives `ILogger<AttachmentReplayTool>` via ctor instead of silently using `NullLogger` internally.
- Replay lever bug fix — `ApplyReplayLevers` computed levered booleans that previously only flowed to telemetry; processor used singleton extension `IsEnabled` (config-level), ignoring levers entirely. Fixed by adding `attachmentsEnabledByLever`, `linksEnabledByLever`, `embeddedImagesEnabledByLever` params to `IWorkItemResolutionProcessorFactory.Create()`; factory synthesises disabled extension instances when a lever suppresses an extension; orchestrator passes `ext.AttachmentsEnabled`, `ext.LinksEnabled`, `ext.EmbeddedImages.Enabled` (post-lever values) to `Create()`. Stages are now actually skipped, not just logged.

- `ApplyReplayLevers` deleted — replaced by `ComputeLeveredExtensionFlags()` which returns `(bool attachments, bool links, bool embeddedImages)` computed directly from `_options.Value.Extensions.*` and `_workItemOptions`; no god-object reads.
- `AttachmentsEnabled`, `LinksEnabled`, `EmbeddedImages` removed from `WorkItemsModuleExtensions` — god-object now carries only non-extension config: `Query`, `ResolutionStrategy`, `IncludeFilters`, `ExcludeFilters`.
- `EmitReplaySkipVisibilityEvents` signature changed to `(scope, bool attachmentsEnabled, bool embeddedImagesEnabled, resumeAtStage)`.

---

## Pending

### Export-side facet extraction

Once the export-side pipeline is generalised, set `SupportsExport: true` for `LinksWorkItemExtension`, `AttachmentsWorkItemExtension`, `CommentsWorkItemExtension` and move their export logic out of the export path inline.

EmbeddedImages export stays as a field-rewrite contributor inside the core `AppliedFields` step — not a peer pipeline stage.

---

## Architectural decisions (still in force)

- **Revisions are core, not an extension.** The per-entity loop IS the revision stream. `RevisionsEnabled` is a mislabelled core kill-switch; the early-return guard is the correct shape.
- **EmbeddedImages is not a peer pipeline stage.** It is a field-value rewrite inside core `AppliedFields`. Modelled as a field-rewrite contributor; extract to its own `IOptions<EmbeddedImagesExtensionOptions>` but do NOT make it a cursor stage.
- **Cursor format does not change.** Stage names are the existing marker strings. No upgrader.
- **`WorkItemResolutionProcessor` is the sub-orchestrator.** It owns the per-revision loop + cursor. `WorkItemsOrchestrator` delegates to it; the orchestrator does not loop entities itself.
- **`WorkItemRevisionStage`** is the descriptor (`CursorName`, `IsEnabled`, `ExecuteAsync`) — already exists, already used for extension stages.

---

## Cross-references

- [execution-contract.md](../../.agents/10-contracts/specs/execution-contract.md)
- [execution-model.md](../../.agents/30-context/architecture/execution-model.md)
- [contracts/IModuleExtension.md](contracts/IModuleExtension.md)
- [ADR 0019](../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md)

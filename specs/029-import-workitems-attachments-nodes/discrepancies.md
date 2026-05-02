# Architecture Discrepancies

**Feature**: Import — WorkItems, Attachments, and Nodes  
**Flagged by**: speckit.specify  
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### Attachment import not covered by a dedicated feature file

- **Source doc**: `features/import/work-items/` (directory)
- **Section**: No `attachments/` subdirectory under `features/import/work-items/`
- **Issue**: The spec requires acceptance scenarios for attachment import (Stage D: UploadedAttachments), idempotency via `idmap.db`, embedded image rewriting, and the `maxSizeBytes` skip behaviour. No Gherkin feature file currently exists for these scenarios (only `features/export/work-items/attachments/export-attachments.feature` covers the export side).
- **Suggested update**: Create `features/import/work-items/attachments/import-attachments.feature` covering the acceptance scenarios in User Story 3 of this spec.

### `maxSizeBytes` attachment option not yet in docs/configuration.md

- **Source doc**: `docs/configuration.md` (assumed)
- **Section**: WorkItems module extensions configuration
- **Issue**: `proposed-features.md` (M4) lists `extensions[Attachments].maxSizeBytes` as a planned option (❌ Not implemented). The spec includes FR-018 referencing this behaviour (skipping oversized attachments with a Warning). This option is not yet in the canonical configuration reference.
- **Suggested update**: After implementation, add `extensions.Attachments.maxSizeBytes` to `docs/configuration.md` under the WorkItems module extensions section.

### Embedded image import not covered by a dedicated feature file

- **Source doc**: `features/import/work-items/revisions/import-embedded-images.feature`
- **Section**: Scenarios in that file
- **Issue**: The file exists but may not cover the full embedded image URL-rewriting behaviour that FR-019 requires (upload binary, rewrite source URL to target URL in HTML field). The spec's FR-019 adds precision beyond what the existing feature file may describe.
- **Suggested update**: Review and extend `features/import/work-items/revisions/import-embedded-images.feature` to include the URL-rewriting scenario.

### Embedded image export NOT wired into WorkItemExportOrchestrator (blocking end-to-end)

- **Source doc**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs`
- **Section**: Export loop (post-`revision.json` write)
- **Issue**: `IEmbeddedImageExportService` (`EmbeddedImageExportService`) exists, is registered, and is fully implemented. `WorkItemRevision.EmbeddedImages` is the correct data structure to hold the `OriginalUrl → RelativePath` map in `revision.json`. However, **`WorkItemExportOrchestrator` never calls `IEmbeddedImageExportService.ProcessHtmlAsync/ProcessMarkdownAsync`**. As a result, `revision.json.EmbeddedImages` is always `[]` in every exported package. The import side (`RevisionFolderProcessor.RewriteEmbeddedImageUrlsAsync`) correctly consumes `EmbeddedImages` but silently does nothing when the list is empty. This means embedded image migration is broken end-to-end in the current implementation — even though both halves are individually implemented.
- **Severity**: **Blocking** — FR-019, FR-028, FR-029 cannot be satisfied until this gap is closed.
- **Suggested fix**: In `WorkItemExportOrchestrator`, inject `IEmbeddedImageExportService` (created on-demand with the current `IArtefactStore` and `IEmbeddedImageDownloader`). After reading each revision's field values, process every HTML/Markdown field through `ProcessHtmlAsync`/`ProcessMarkdownAsync`. Collect `EmbeddedImageMetadata` entries (by comparing original vs rewritten `src` values) and assign them to `revision.EmbeddedImages` before serialising `revision.json`.

### Field Mapping Tool not in scope — note for operators

- **Source doc**: `analysis/proposed-features.md`
- **Section**: M4 — WorkItemsModule planned options
- **Issue**: The legacy `azure-devops-migration-tools` implements a `FieldMappingTool` with 10 field map types (`FieldToFieldMap`, `RegexFieldMap`, `FieldValueMap`, `FieldValuetoTagMap`, `FieldToTagFieldMap`, `FieldMergeMap`, `FieldLiteralMap`, `FieldClearMap`, `FieldSkipMap`, `FieldCalculationMap`, `TreeToTagFieldMap`, `MultiValueConditionalMap`). This capability is entirely absent from the current platform. Operators who need field-level transformations (e.g. renaming fields, value mapping, tag generation from field values) cannot perform these today.
- **Severity**: **Not blocking for spec 029** — but represents a significant migration capability gap for complex migrations.
- **Suggested update**: Create a separate spec (e.g. `030-field-mapping-tool`) to capture the full field mapping requirement. This spec should note `030` as a dependency for migrations requiring field transformation.

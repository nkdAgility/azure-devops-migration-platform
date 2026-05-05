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
- **Issue**: `IEmbeddedImageExportService` (`EmbeddedImageExportService`) **is fully implemented** in `DevOpsMigrationPlatform.Infrastructure.Agent.Export`. `WorkItemRevision.EmbeddedImages` is the correct data structure to hold the `OriginalUrl → RelativePath` map in `revision.json`. However, **`WorkItemExportOrchestrator` never calls `IEmbeddedImageExportService.ProcessHtmlAsync/ProcessMarkdownAsync`**. As a result, `revision.json.EmbeddedImages` is always `[]` in every exported package. The import side (`RevisionFolderProcessor.RewriteEmbeddedImageUrlsAsync`) correctly consumes `EmbeddedImages` but silently does nothing when the list is empty. This means embedded image migration is broken end-to-end in the current implementation — even though both halves are individually implemented.
- **Severity**: **Blocking** — FR-019, FR-028, FR-029 cannot be satisfied until this gap is closed. No new service implementation is required; the only change needed is wiring the existing `EmbeddedImageExportService` into `WorkItemExportOrchestrator`.
- **Suggested fix**: In `WorkItemExportOrchestrator`, inject `IEmbeddedImageExportService` (created on-demand with the current `IArtefactStore` and `IEmbeddedImageDownloader`). After reading each revision's field values, process every HTML/Markdown field through `ProcessHtmlAsync`/`ProcessMarkdownAsync`. Collect `EmbeddedImageMetadata` entries (by comparing original vs rewritten `src` values) and assign them to `revision.EmbeddedImages` before serialising `revision.json`.

### Field Transform extension required for WorkItemsModule

- **Source doc**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/WorkItemsModuleExtensions.cs`
- **Section**: `FromModule` extension switch
- **Issue**: `FieldTransformTool` (`IFieldTransformTool`) is fully implemented. However, `WorkItemsModuleExtensions` does not recognise a `FieldTransform` extension type, so there is no way to enable it for a `WorkItemsModule` import run. Stage B (`AppliedFields`) has no hook for field transformation.
- **Severity**: **Blocking for FR-041/FR-042/FR-043** — field transformation cannot be enabled for import without this wiring.
- **Suggested fix**: Add `FieldTransformEnabled` (default `false`) to `WorkItemsModuleExtensions`. Add `case "FieldTransform":` to the switch in `FromModule` and a corresponding block in `FromOptions`. In `WorkItemsModule.ImportAsync`, inject `IFieldTransformTool?` (optional) and call `ApplyTransforms` on the field dictionary during Stage B when the extension is enabled.

### PrepareAsync not present in IModule interface

- **Source doc**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs`
- **Section**: Interface declaration
- **Issue**: `IModule` currently declares only `ExportAsync`, `ImportAsync`, and `ValidateAsync`. `PrepareAsync(PrepareContext context, CancellationToken ct)` is described in `docs/modules.md` but is absent from the actual C# interface. `PrepareContext` does not exist as a type. All module `PrepareAsync` implementations described in FR-P02 through FR-P09 cannot be built until this foundation is in place.
- **Severity**: **Blocking for FR-P01 through FR-P09** — no Prepare phase FRs can be implemented without this interface extension.
- **Suggested fix**: Add `Task PrepareAsync(PrepareContext context, CancellationToken ct)` to `IModule`. Create `PrepareContext` in `DevOpsMigrationPlatform.Abstractions.Agent.Context` (mirroring `ImportContext` / `ValidationContext` — should carry `IArtefactStore`, `Job`, `ITargetEndpointInfo`). Update all existing module implementations to provide a default `return Task.CompletedTask` body so they remain non-breaking. Add `SupportsPrepare` bool property to `IModule` (defaulting to `false`) following the pattern of `SupportsExport` / `SupportsImport`.

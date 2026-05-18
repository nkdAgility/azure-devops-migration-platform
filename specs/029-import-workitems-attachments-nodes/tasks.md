# Tasks: Import — WorkItems, Attachments, and Nodes (Reconciled)

**Reconciled**: 2026-05-17  
**Authority order used**: `.agents` guidance → newer specs (030-035) → this spec → implementation evidence

- [X] T001 [US0] Add `PrepareAsync` to module surface — `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs` — Status: complete
- [X] T002 [US0] Add `PrepareContext` contract for module prepare phase — `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareContext.cs` — Status: complete
- [X] T003 [US0] Implement WorkItems prepare orchestration and readiness report output — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` — Status: complete
- [ ] T004 [US0] Implement Nodes prepare path existence validation and blocking behavior — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` — Status: incomplete
- [X] T005 [US2] Persist import cursor and id map state in `.migration/Checkpoints` — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/ImportCheckpointService.cs` — Status: complete
- [X] T006 [US1] Execute node readiness before work item replay — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/NodeReadinessOrchestrator.cs` — Status: complete
- [X] T007 [US2] Stream revision folders in lexicographic order with resume support — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` — Status: complete
- [X] T008 [US2] Apply stage pipeline (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments) with per-stage cursor writes — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs` — Status: complete
- [X] T009 [US3] Upload attachments idempotently via idmap checks and mapping persistence — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs` — Status: complete
- [ ] T010 [US3] Wire `FieldTransform` extension toggle through `WorkItemsModuleExtensions` (`Type = "FieldTransform"`) — `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/WorkItemsModuleExtensions.cs` — Status: incomplete
- [ ] T011 [US3] Align field transform order with spec (`identity + node translation` before transform) — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs` — Status: incomplete
- [ ] T012 [US3] Wire embedded image export prerequisite into export orchestrator (`revision.EmbeddedImages` population) — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs` — Status: incomplete
- [ ] T013 [US3] Add attachment-import acceptance coverage in `features/import/work-items/attachments/` — `features/import/work-items/attachments/.gitkeep` — Status: incomplete
- [ ] T014 [US3] Document `extensions.Attachments.maxSizeBytes` in configuration reference — `docs/configuration-reference.md` — Status: incomplete
- [X] T015 [US4] Replace this spec's implementation backlog with the canonical detailed backlog in Spec 035 — `specs/035-workitem-import-support/tasks.md` — Status: complete/superseded; completed because superseded by specs/035-workitem-import-support/tasks.md T001-T160

## Incomplete evidence notes

- **T004**: `NodesModule.PrepareAsync` writes a minimal report and does not check referenced path existence or enforce blocking logic.
- **T010**: `WorkItemsModuleExtensions.FromModule` has no `case "FieldTransform"` and no `FieldTransformEnabled` property.
- **T011**: `RevisionFolderProcessor` executes field transform before node translation.
- **T012**: `WorkItemExportOrchestrator` does not wire `IEmbeddedImageExportService` population of `revision.EmbeddedImages`.
- **T013**: `features/import/work-items/attachments/` contains only `.gitkeep`; no executable attachment import feature file.
- **T014**: `docs/configuration-reference.md` has no `maxSizeBytes` entry for WorkItems attachment import controls.

## Superseded evidence notes

- **T015 supersession source**: `specs/035-workitem-import-support/tasks.md` is the active, detailed import implementation backlog and status authority for this capability set.

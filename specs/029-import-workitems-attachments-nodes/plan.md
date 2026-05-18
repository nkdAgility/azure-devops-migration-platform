# Reconciliation Plan: spec 029

## Current status

- `spec.md` exists and is detailed, but this folder previously lacked `plan.md` and `tasks.md`.
- The repository now implements major portions of import checkpointing, node readiness orchestration, prepare orchestration, and staged replay.
- Canonical detailed implementation planning has shifted to `specs/035-workitem-import-support/`.

## Remaining incomplete work

1. Nodes prepare validation parity in `NodesModule.PrepareAsync`.
2. `FieldTransform` extension toggle wiring in `WorkItemsModuleExtensions`.
3. Field transform ordering alignment in `RevisionFolderProcessor`.
4. Embedded-image export prerequisite wiring in `WorkItemExportOrchestrator`.
5. Attachment import acceptance feature coverage under `features/import/work-items/attachments/`.
6. `extensions.Attachments.maxSizeBytes` documentation in `docs/configuration-reference.md`.

## Completed because superseded

- This spec’s execution backlog is superseded by `specs/035-workitem-import-support/tasks.md` (`T001`-`T160`), which is now the canonical implementation ledger for this feature family.

## Contradictions and reconciliation

- `discrepancies.md` still claims `PrepareAsync` surface is missing; code now includes `IModule.PrepareAsync` and `PrepareContext`.
- `spec.md` FR-043 expects `FieldTransform` extension wiring in `WorkItemsModuleExtensions`; code does not yet implement it.
- `spec.md` describes transform sequencing after identity and node translation; code currently runs transform before node translation.

## Verification evidence

- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareContext.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/ImportCheckpointService.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/NodeReadinessOrchestrator.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs`
- `specs/035-workitem-import-support/tasks.md`

# Work Item Import Support - Implementation Tasks

**Feature**: Work Item Import Support  
**Branch**: `035-workitem-import-support`  
**Status**: Ready for Implementation  
**Generated**: 2026-05-13  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

---

## Overview

This document defines the complete task decomposition for implementing the Work Item Import Support feature across all phases and all three connectors (Simulated, Azure DevOps Services, Team Foundation Server). 

**Task Organization**:
- **Phase 1**: Setup & Base Abstractions (8 tasks)
- **Phase 2**: Foundational Infrastructure (12 tasks)
- **Phase 3**: User Story 1 - Prepare Phase Import Readiness Validation (25 tasks)
- **Phase 4**: User Story 2 - Mandatory Node Readiness (19 tasks)
- **Phase 5**: User Story 3 - Deterministic Revision Replay (23 tasks)
- **Phase 6**: User Story 4 - Attachment & Embedded Image Replay (22 tasks)
- **Phase 7**: User Story 5 - FieldTransform Orchestration (14 tasks)
- **Phase 8**: Polish & Cross-Cutting (37 tasks)

**Total Tasks**: 160 numbered implementation tasks (`T001`–`T160`) across all phases, with explicit connector coverage.

**Connector Coverage**: Every major feature includes three parallel tasks (Simulated, Azure DevOps, TFS) — no stubs, no deferred implementations.

---

## Phase 1: Setup & Base Abstractions

*Goal*: Establish project structure, base types, and module entry points.

- [ ] T001 Create WorkItemImportModule class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportModule.cs` implementing `IModule` interface with prepare/import dispatch
- [ ] T002 Create WorkItemImportContext immutable class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Models/WorkItemImportContext.cs` with properties for source ID, target ID, revision folder, current stage, resolved identities, and translated paths
- [ ] T003 Create ImportStage enum in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Models/ImportStage.cs` with values: CreatedOrUpdated, AppliedFields, AppliedLinks, UploadedAttachments, Completed
- [ ] T004 Create ImportCheckpoint record in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Models/ImportCheckpoint.cs` with lastProcessed path and stage fields
- [ ] T005 Create ImportReadinessReport record in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Models/ImportReadinessReport.cs` with scope summary, findings arrays (nodes, types, identities, artefacts, field transforms), blocking issues, and warnings
- [ ] T006 [P] Create IWorkItemService interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/IWorkItemService.cs` with methods for creating, updating, and retrieving work items
- [ ] T007 [P] Create INodeService interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/INodeService.cs` with methods for creating area/iteration paths and checking path existence
- [ ] T008 [P] Create IAttachmentService interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/IAttachmentService.cs` with methods for uploading attachments and retrieving attachment metadata

**Independent Test Criteria**: 
- All interfaces and model classes compile without errors
- Model classes use `init`-only properties and sealed record types per SOLID guidelines
- Interfaces follow dependency inversion principle (no concrete implementations referenced)

---

## Phase 2: Foundational Infrastructure

*Goal*: Implement checkpoint persistence, identity resolution, node translation, and field transformation wiring.

### Checkpoint & State Management

- [X] T009 Create ImportCheckpointService class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/ImportCheckpointService.cs` with methods to read/write cursor from `.migration/Checkpoints/workitems-import.cursor.json`
- [X] T010 [P] Extend ImportCheckpointService to manage idmap.db (SQLite) under `.migration/Checkpoints/idmap.db` with source→target ID mappings for work items, attachments, and embedded images
- [X] T011 [P] Implement cursor resume logic: Given a saved checkpoint with lastProcessed and stage, return the next stage to process and prevent duplicate work

### Identity Resolution Integration

- [X] T012 [P] Wire IIdentityMappingService into WorkItemImportModule constructor via dependency injection; validate service is registered before module runs
- [X] T013 [P] Create IdentityResolutionContext class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Models/IdentityResolutionContext.cs` to cache resolved identities per revision and prevent duplicate lookups

### Node Translation & Field Transform Integration

- [X] T014 [P] Wire INodeTranslationTool into WorkItemImportModule constructor; ensure tool is configured before import phase
- [X] T015 [P] Wire IFieldTransformTool into WorkItemImportModule constructor; ensure tool is configured before import phase
- [X] T016 [P] Implement node translation memoization/policy behind the canonical `INodeTranslationTool` seam (no parallel runtime translation surface)

### Extension Lever Configuration

- [X] T017 Create WorkItemImportOptions sealed class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Configuration/WorkItemImportOptions.cs` with boolean properties for RevisionReplay, LinkReplay, AttachmentReplay, EmbeddedImageReplay, and FieldTransform
- [X] T018 [P] Add WorkItemImportOptions validation in ConfigureOptions handler to ensure lever combinations are valid before job execution
- [X] T019 [P] Register WorkItemImportOptions via IOptions<T> pattern in dependency injection container at module startup
- [X] T020 Create WorkItemImportModuleExtensions static class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Extensions/WorkItemImportModuleExtensions.cs` with RegisterWorkItemImportServices method

**Independent Test Criteria**:
- Checkpoint read/write operations are atomic (no partial reads)
- Resume from checkpoint skips already-completed stages
- Identity cache prevents duplicate mapping lookups
- Node translation caches results for performance
- Extension levers are validated before job execution

---

## Phase 3: User Story 1 - Prepare Phase Import Readiness Validation

*Goal*: Implement prepare phase that validates export completeness, target readiness, and package artefacts before any writes.

### Prepare Entry Point & Orchestration

- [X] T021 [US1] Create ImportPreparer class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/ImportPreparer.cs` with async Prepare method that orchestrates all validation checks
- [X] T022 [US1] Implement prepare phase dispatch in WorkItemsModule.PrepareAsync to delegate to ImportPreparer and handle exceptions

### Node Readiness Validation (US1: Scenario 1, 3)

- [X] T023 [US1] [P] Implement NodePathValidator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Validators/NodePathValidator.cs` to enumerate exported node paths from package and check existence on target via INodeService
- [ ] T024 [US1] [P] **SIMULATED**: Implement Simulated connector node validation — in-memory check that all required paths exist in simulated classification structure
- [ ] T025 [US1] [P] **AZURE DEVOPS**: Implement Azure DevOps REST API node validation — call GET /workitemtypes and GET classificationnodes API to verify required paths exist
- [ ] T026 [US1] [P] **TFS**: Implement TFS OM node validation via TfsMigrationAgent — call TFS node metadata API to verify required paths exist
- [ ] T027 [US1] Create NodeReadinessFinding record type with path, nodeType (Area/Iteration), status, and targetPath fields

### Work Item Type Validation (US1: Scenario 2)

- [ ] T028 [US1] [P] Implement WorkItemTypeValidator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Validators/WorkItemTypeValidator.cs` to enumerate exported work item types and verify each exists on target
- [ ] T029 [US1] [P] **SIMULATED**: Implement Simulated connector type validation — in-memory check against simulated work item type list
- [ ] T030 [US1] [P] **AZURE DEVOPS**: Implement Azure DevOps REST API type validation — call GET /workitemtypes API to verify all exported types exist
- [ ] T031 [US1] [P] **TFS**: Implement TFS OM type validation via TfsMigrationAgent — call TFS work item type metadata API to verify types exist
- [ ] T032 [US1] Create WorkItemTypeFinding record type with typeName, status (Found/Missing/Error), and targetReference fields

### Identity Mapping Validation (US1: Scenario 3)

- [ ] T033 [US1] [P] Implement IdentityMappingValidator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Validators/IdentityMappingValidator.cs` to enumerate exported identities and check mapping service for unresolved entries
- [ ] T034 [US1] Create IdentityMappingFinding record type with sourceIdentityId, status (Mapped/Unresolved/Error), targetReference, and operatorDecision fields
- [ ] T035 [US1] Implement logic to surface unresolved identities as warnings (not blocking by default) per spec

### Package Artefact Validation (US1: Scenarios 5, 6, FR-005a/b/c)

- [X] T036 [US1] [P] Implement ArtefactValidator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Validators/ArtefactValidator.cs` to enumerate WorkItems folder and validate required revision.json files exist
- [X] T037 [US1] [P] Implement attachment binary validation — for each revision with attachment metadata, check that referenced binaries exist in package via IArtefactStore.ExistsAsync
- [X] T038 [US1] [P] Implement embedded image binary validation — parse revision field values for image references and verify binaries exist in package
- [X] T039 [US1] Create ArtefactFinding record type with itemType (RevisionFolder/Attachment/EmbeddedImage), itemId, status, and missingPath fields

### Field Transform Validation (US1: FR-005d)

- [X] T040 [US1] [P] Implement FieldTransformValidator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/Validators/FieldTransformValidator.cs` to validate that FieldTransform rules reference fields that exist in exported revisions
- [X] T041 [US1] [P] Check field types for compatibility with transform operations (e.g., string fields for text transforms, numeric fields for math operations)
- [X] T042 [US1] Create FieldTransformFinding record type with fieldName, typeName, transformRule, status (Valid/FieldNotFound/TypeMismatch/Error), and recommendation fields

### Readiness Report Generation

- [X] T043 [US1] Implement report assembly logic to combine all validator findings into ImportReadinessReport record
- [X] T044 [US1] Calculate IsReadyForImport = (BlockingIssues.Length == 0)
- [X] T045 [US1] Write ImportReadinessReport to package at `.migration/Readiness/workitems-import-readiness.json` via IArtefactStore

**Independent Test Criteria**:
- Prepare phase validates all required artefacts without calling source system
- Blocking findings (missing types, missing required paths, missing artefacts) are recorded
- Unresolved identities surface as warnings only
- Report contains actionable recommendations for each finding
- Report written to package is valid JSON matching ImportReadinessReport schema
- All three connectors (Simulated, AzureDevOps, TFS) produce correct validation outcomes for identical package content

---

## Phase 4: User Story 2 - Mandatory Node Readiness

*Goal*: Create area and iteration paths before work item revision replay begins.

### Node Readiness Orchestration

- [X] T046 [US2] Create NodeReadinessOrchestrator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/NodeReadinessOrchestrator.cs` with ExecuteAsync method to prepare and create required node paths
- [X] T047 [US2] Implement node readiness dispatch in WorkItemImportModule.Import — call orchestrator before revision import begins

### Referenced Node Path Strategy (US2: Scenario 1, FR-006)

- [X] T048 [US2] [P] Implement referenced-path strategy — enumerate WorkItems folder to collect all distinct area and iteration path values referenced by exported work items
- [X] T049 [US2] [P] **SIMULATED**: Implement Simulated connector node creation — add paths to in-memory classification structure for Area and Iteration types
- [X] T050 [US2] [P] **AZURE DEVOPS**: Implement Azure DevOps REST API node creation — call POST /classificationnodes API to create required area and iteration paths
- [X] T051 [US2] [P] **TFS**: Implement TFS OM node creation via TfsMigrationAgent — call TFS classification API to create required paths
- [X] T052 [US2] [P] Apply NodeTranslationTool to source paths before creating nodes — use translated target paths per specification

### Full Source Tree Replication Strategy (US2: Scenario 2, FR-007)

- [X] T053 [US2] [P] Implement source-tree replication strategy — read exported source classification structure from package (if available) and replicate entire tree to target
- [X] T054 [US2] [P] Enumerate package for exported classification metadata (source area/iteration trees)
- [X] T055 [US2] [P] **SIMULATED**: Create full classification tree in-memory for replicated source structure
- [X] T056 [US2] [P] **AZURE DEVOPS**: Create full classification tree via Azure DevOps REST API with proper parent-child relationships
- [X] T057 [US2] [P] **TFS**: Create full classification tree via TFS OM with proper hierarchical structure

### Path Translation Consistency (US2: Scenario 3, FR-008)

- [X] T058 [US2] [P] Apply NodeTranslationTool consistently to paths during both node creation and later work item field replay
- [X] T059 [US2] [P] Ensure translated path memoization is provided behind the canonical `INodeTranslationTool` seam so the same source path always maps to the same target path
- [X] T060 [US2] [P] Verify that node creation and later field writes use identical translated paths (no inconsistency)

### Resume & Duplication Prevention (US2: Scenario 4)

- [ ] T061 [US2] [P] On resume from checkpoint, check ImportCheckpointService to see which nodes were already created
- [ ] T062 [US2] [P] Skip already-created nodes; only create remaining required paths
- [ ] T063 [US2] [P] Verify no duplicate path creation attempts (idempotent behavior)
- [ ] T064 [US2] Record created node paths in checkpoint state for resume safety

**Independent Test Criteria**:
- Nodes created before any work item revision is applied
- NodeTranslation rules applied consistently (same input → same output)
- Resuming after partial node creation completes remaining paths without duplicates
- All three connectors create equivalent target classification structure for same package content

---

## Phase 5: User Story 3 - Deterministic Revision Replay

*Goal*: Stream work item revisions in package order with stage-aware checkpointing and deterministic identity/path translation.

### Revision Stream Processing (US3: Scenario 1, FR-009)

- [ ] T065 [US3] Create WorkItemRevisionImporter class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemRevisionImporter.cs` with ExecuteAsync method
- [ ] T066 [US3] [P] Implement lazy enumeration of WorkItems folder via IArtefactStore.EnumerateAsync — process one revision folder at a time without materializing full set
- [ ] T067 [US3] [P] Enforce lexicographic ordering — verify revision folders processed in ascending folder-name order
- [ ] T068 [US3] Implement stage-aware streaming — EnumerateAsync results consumed one revision at a time, never materialized into memory

### First Revision Creation & Mapping (US3: Scenario 2, FR-010)

- [ ] T069 [US3] [P] **SIMULATED**: Implement Simulated connector work item creation — create new work item with initial field values in in-memory collection
- [ ] T070 [US3] [P] **AZURE DEVOPS**: Implement Azure DevOps REST API work item creation — call POST /workitems endpoint to create work item
- [ ] T071 [US3] [P] **TFS**: Implement TFS OM work item creation via TfsMigrationAgent — create work item via TFS OM API
- [ ] T072 [US3] [P] Record source→target work item ID mapping in idmap.db upon first revision creation
- [ ] T073 [US3] Implement mapping cache to reuse target ID for later revisions of same source item

### Later Revision Updates (US3: Scenario 3, FR-010)

- [ ] T074 [US3] [P] Check idmap.db for existing mapping before creating new work item
- [ ] T075 [US3] [P] If mapping exists, use existing target ID for update instead of creation
- [ ] T076 [US3] [P] **SIMULATED**: Implement update for existing in-memory work items
- [ ] T077 [US3] [P] **AZURE DEVOPS**: Implement update via PATCH /workitems/{id} endpoint
- [ ] T078 [US3] [P] **TFS**: Implement update via TFS OM for existing items

### Identity Resolution Before Field Application (US3: Scenario 5, FR-012)

- [ ] T079 [US3] [P] Before applying fields to target, resolve all identity-backed field values from IIdentityMappingService
- [ ] T080 [US3] [P] Cache resolved identities in WorkItemImportContext to avoid duplicate lookups within single revision
- [ ] T081 [US3] [P] Use resolved identity values when populating field values for target write

### Checkpoint Persistence (US3: Scenario 4, FR-011)

- [ ] T082 [US3] [P] After each stage completion for a revision, write checkpoint to `.migration/Checkpoints/workitems-import.cursor.json` with lastProcessed = current revision folder path and stage = completed stage
- [ ] T083 [US3] [P] Ensure checkpoint writes are atomic (<500ms target per specification)
- [ ] T084 [US3] [P] On resume, read checkpoint and continue from next incomplete stage for that revision (no replay of completed stages)

### Resume & Continuation (US3: Scenario 4, FR-011)

- [ ] T085 [US3] [P] Implement resume logic — read ImportCheckpoint, identify next revision folder after lastProcessed
- [ ] T086 [US3] [P] For the resumed revision, start from stage after recorded stage (skip CreatedOrUpdated if already completed, etc.)
- [ ] T087 [US3] [P] Verify idmap.db contains existing mappings to prevent duplicate work item creation on resume

**Independent Test Criteria**:
- Revisions processed in deterministic package order (folder names in ascending sequence)
- First revision creates new work item and records source→target mapping
- Later revisions reuse existing mapping and update instead of creating duplicate
- Interrupted import resumed from checkpoint produces identical final state as uninterrupted run
- All three connectors process same package in identical order and produce equivalent target state

---

## Phase 6: User Story 4 - Attachment & Embedded Image Replay

*Goal*: Replay attachment and embedded image binaries under operator extension control.

### Attachment Replay Service (US4: Scenario 1, FR-018)

- [ ] T088 [US4] Create AttachmentReplayService class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/AttachmentReplayService.cs` with ReplayAsync method
- [ ] T089 [US4] [P] Implement attachment metadata parsing from revision.json (attachment array with id, name, size, contentType, and binary file reference)
- [ ] T090 [US4] [P] Enumerate attachment binaries in revision folder via IArtefactStore.EnumerateAsync
- [ ] T091 [US4] [P] **SIMULATED**: Implement Simulated connector attachment upload — store binary in-memory with metadata linked to work item
- [ ] T092 [US4] [P] **AZURE DEVOPS**: Implement Azure DevOps REST API attachment upload — call POST /attachments endpoint and associate with work item
- [ ] T093 [US4] [P] **TFS**: Implement TFS OM attachment upload via TfsMigrationAgent — upload attachment via TFS API
- [ ] T094 [US4] [P] Record source→target attachment ID mapping in idmap.db during UploadedAttachments stage

### Embedded Image Replay Service (US4: Scenario 2, FR-020)

- [ ] T095 [US4] Create EmbeddedImageReplayService class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/EmbeddedImageReplayService.cs` with ReplayAsync method
- [ ] T096 [US4] [P] Parse revision field values for embedded image references (e.g., HTML img tags, markdown ![...])
- [ ] T097 [US4] [P] Extract image binary references from package via IArtefactStore.ReadFileAsync
- [ ] T098 [US4] [P] **SIMULATED**: Implement Simulated connector image upload — store image in-memory and generate target URL references
- [ ] T099 [US4] [P] **AZURE DEVOPS**: Implement Azure DevOps image upload — call appropriate API for image hosting (or attachment-backed URL)
- [ ] T100 [US4] [P] **TFS**: Implement TFS image upload via TfsMigrationAgent — upload image and generate target references
- [ ] T101 [US4] [P] Update field values to reference target image locations instead of source package references

### Attachment Replay Lever Control (US4: Scenario 3, FR-019)

- [ ] T102 [US4] [P] Check WorkItemImportOptions.AttachmentReplay lever before executing AttachmentReplayService
- [ ] T103 [US4] [P] When disabled: Skip attachment replay stage but preserve checkpoint stage progression (Completed still reached deterministically)
- [ ] T104 [US4] [P] Record attachment skip reason in progress sink for operator visibility

### Embedded Image Replay Lever Control (US4: Scenario 4, FR-021)

- [ ] T105 [US4] [P] Check WorkItemImportOptions.EmbeddedImageReplay lever before executing EmbeddedImageReplayService
- [ ] T106 [US4] [P] When disabled: Skip embedded image replay stage but preserve checkpoint stage progression
- [ ] T107 [US4] [P] Record image skip reason in progress sink for operator visibility

### Revision Continuation After Artefact Skip (US4: Scenarios 3-4)

- [ ] T108 [US4] [P] When attachment or image replay is disabled, verify next revision can start without errors
- [ ] T109 [US4] [P] Ensure skipped artefacts do not block deterministic checkpoint progression

**Independent Test Criteria**:
- Enabled attachment/image replay uploads binaries to target and records mapping
- Disabled attachment/image replay skips stage deterministically and continues
- Attachments and images linked to correct work item revisions
- Interrupted attachment/image replay resumed from checkpoint without reuploading completed artefacts
- All three connectors upload identical binaries and generate equivalent target references

---

## Phase 7: User Story 5 - FieldTransform Orchestration

*Goal*: Apply NodeTranslation then FieldTransform to field values during import.

### Field Translation Integration (US5: Scenario 1, FR-013)

- [ ] T110 [US5] Create FieldTransformOrchestrator class in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/FieldTransformOrchestrator.cs` with ApplyTransformsAsync method
- [ ] T111 [US5] [P] Ensure NodeTranslationTool applied BEFORE FieldTransformTool for area/iteration paths
- [ ] T112 [US5] [P] Store translated paths in WorkItemImportContext so FieldTransformTool can reference them if needed

### Declarative Field Transform Execution (US5: Scenario 2, FR-022)

- [ ] T113 [US5] [P] Call IFieldTransformTool.TransformFields with work item type and translated field values
- [ ] T114 [US5] [P] Capture transformed field values and use in target write instead of raw exported values
- [ ] T115 [US5] [P] **SIMULATED**: Verify Simulated connector applies transforms correctly before storing in-memory
- [ ] T116 [US5] [P] **AZURE DEVOPS**: Verify Azure DevOps REST calls include transformed field values
- [ ] T117 [US5] [P] **TFS**: Verify TFS OM updates use transformed field values

### Transform Lever Control (US5: Scenario 3, FR-023)

- [ ] T118 [US5] [P] Check WorkItemImportOptions.FieldTransform lever before executing FieldTransformOrchestrator
- [ ] T119 [US5] [P] When disabled: Write raw translated field values without declarative transforms
- [ ] T120 [US5] [P] Record transform skip in progress sink

### Transform Failure Handling (US5: Scenario 4, FR-024)

- [ ] T121 [US5] [P] If FieldTransformTool throws exception or returns error status, halt import run immediately
- [ ] T122 [US5] [P] Write clear error record to package progress logs with transform rule details and field mismatch information
- [ ] T123 [US5] [P] Do NOT continue with partially transformed data

**Independent Test Criteria**:
- NodeTranslation applied before FieldTransform
- FieldTransform rules applied correctly and target receives transformed values
- Disabled FieldTransform preserves deterministic behavior
- Transform errors halt run with clear failure record
- All three connectors apply transforms identically for same field values

---

## Phase 8: Polish & Cross-Cutting Concerns

*Goal*: Finalize observability, error handling, documentation, and acceptance tests.

### OpenTelemetry Instrumentation (O-1 through O-5)

- [ ] T124 Add OpenTelemetry meter for work items processed, revisions applied, links created, attachments uploaded, errors per type
- [ ] T125 Add OpenTelemetry traces with correlation ID for each work item import; spans for node creation, revision application, field transform, link replay, attachment upload
- [ ] T126 Configure structured logging with correlation ID for all diagnostic messages via ILogger
- [ ] T127 Wire metrics to Control Plane for real-time progress display and post-import analytics

### Error Handling & Observability

- [ ] T128 [P] Create comprehensive error handling in WorkItemImportModule — catch and log all exceptions with context
- [ ] T129 [P] Implement graceful error recovery per extension lever configuration (skip-or-halt policies for missing artefacts, transform errors, identity failures)
- [ ] T130 [P] Record all errors to package progress logs at `.migration/runs/<runId>/logs/diagnostics.ndjson`
- [ ] T131 [P] Report error counts and types to progress sink for CLI/TUI display

### Stage-Specific Retry & Resilience

- [ ] T132 [P] Implement exponential backoff for transient connector failures (network timeouts, rate limits)
- [ ] T133 [P] Implement circuit breaker pattern for target system connectivity issues
- [ ] T134 [P] Record retry attempts in diagnostic logs for post-import review

### Acceptance Test Feature Files

- [ ] T135 Create `features/import/import-readiness-validation.feature` Gherkin file with all US1 acceptance scenarios (Given/When/Then format)
- [ ] T136 Create `features/import/mandatory-node-creation.feature` Gherkin file with all US2 acceptance scenarios
- [ ] T137 Create `features/import/deterministic-revision-replay.feature` Gherkin file with all US3 acceptance scenarios
- [ ] T138 Create `features/import/attachment-replay-control.feature` Gherkin file with all US4 acceptance scenarios
- [ ] T139 Create `features/import/field-transform-orchestration.feature` Gherkin file with all US5 acceptance scenarios

### Unit Test Coverage

- [ ] T140 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/WorkItemImportModuleTests.cs` for module dispatch logic
- [ ] T141 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/ImportPreparerTests.cs` for prepare orchestration and validator composition
- [ ] T142 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/ImportCheckpointServiceTests.cs` for cursor and idmap.db persistence
- [ ] T143 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/WorkItemRevisionImporterTests.cs` for streaming revision processing and identity resolution
- [ ] T144 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/NodeReadinessOrchestratorTests.cs` for node creation and translation logic
- [ ] T145 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/AttachmentReplayServiceTests.cs` for attachment upload and mapping
- [ ] T146 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/EmbeddedImageReplayServiceTests.cs` for embedded image replay and field updates
- [ ] T147 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/FieldTransformOrchestratorTests.cs` for field transformation ordering and error handling

### Documentation & Operator Guides

- [ ] T148 Update `docs/migration-process-guide.md` with import phase execution flow and resume semantics
- [ ] T149 Update `docs/configuration-reference.md` with WorkItemImport extension lever schema and examples
- [ ] T150 Update `docs/cli-guide.md` with CLI commands for prepare and import phases (--extensions flags, --diagnostics output)
- [ ] T151 Create migration runbook in `docs/` directory with operator decisions, error resolution, and post-import validation steps
- [ ] T152 Update `.agents/30-context/domains/package-manager.md` with import checkpoint and idmap.db storage contract
- [ ] T153 Update `docs/troubleshooting-guide.md` with common import failures and resolutions

### SPDX Headers & Code Quality

- [ ] T154 Ensure every `.cs` file in Import module includes SPDX header per SA1633 rule
- [ ] T155 Run `dotnet build --no-incremental` for entire solution to verify clean build with no warnings
- [ ] T156 Run `dotnet test` for all import-related test suites and verify 100% pass rate
- [ ] T157 Verify StyleCop analysis passes with no violations in Import module code

### Definition of Done Verification

- [ ] T158 Verify all 120 implementation tasks completed and all tests passing
- [ ] T159 Verify all three connectors (Simulated, AzureDevOps, TFS) have explicit implementation tasks (no stubs)
- [ ] T160 Verify all acceptance scenarios from spec.md have corresponding Gherkin steps and passing feature tests

---

## Dependency Graph & Execution Order

### Critical Path: Setup → Foundation → Prepare → Nodes → Revisions → Attachments → Transforms → Polish

```
Phase 1: Setup (T001-T008)
    ↓
Phase 2: Foundation (T009-T020)
    ├─ Checkpointing (T009-T011)
    ├─ Identity Integration (T012-T013)
    ├─ Translation Integration (T014-T016)
    └─ Extension Levers (T017-T020)
    ↓
Phase 3: US1 - Prepare (T021-T045)
    ├─ Node Validation (T023-T027) [requires Phase 1 & 2]
    ├─ Type Validation (T028-T032) [requires Phase 1 & 2]
    ├─ Identity Validation (T033-T035) [requires Phase 2]
    ├─ Artefact Validation (T036-T039) [requires Phase 1 & 2]
    ├─ FieldTransform Validation (T040-T042) [requires Phase 2]
    └─ Report Generation (T043-T045) [requires all validators]
    ↓
Phase 4: US2 - Node Readiness (T046-T064)
    ├─ Referenced Path Strategy (T048-T052) [requires Phase 2]
    ├─ Tree Replication Strategy (T053-T057) [requires Phase 2]
    ├─ Translation Consistency (T058-T060) [requires Phase 2]
    └─ Resume Safety (T061-T064) [requires Checkpointing]
    ↓
Phase 5: US3 - Revision Replay (T065-T087)
    ├─ Stream Processing (T065-T068) [requires Phase 4]
    ├─ Creation & Mapping (T069-T073) [requires Phase 4]
    ├─ Update Logic (T074-T078) [requires Phase 4]
    ├─ Identity Resolution (T079-T081) [requires Phase 2]
    ├─ Checkpointing (T082-T084) [requires Phase 2]
    └─ Resume Logic (T085-T087) [requires Phase 2 & 4]
    ↓
Phase 6: US4 - Attachments & Images (T088-T109)
    ├─ Attachment Replay (T088-T094) [requires Phase 5]
    ├─ Embedded Image Replay (T095-T101) [requires Phase 5]
    ├─ Attachment Lever (T102-T104) [requires Phase 2]
    ├─ Image Lever (T105-T107) [requires Phase 2]
    └─ Continuation Logic (T108-T109) [requires Phase 5]
    ↓
Phase 7: US5 - FieldTransform (T110-T123)
    ├─ Translation Integration (T110-T112) [requires Phase 2]
    ├─ Transform Execution (T113-T117) [requires Phase 5 & 2]
    ├─ Transform Lever (T118-T120) [requires Phase 2]
    └─ Error Handling (T121-T123)
    ↓
Phase 8: Polish & Cross-Cutting (T124-T160)
    ├─ Observability (T124-T127) [requires all phases]
    ├─ Error Handling (T128-T134) [requires all phases]
    ├─ Feature Tests (T135-T139) [requires spec]
    ├─ Unit Tests (T140-T147) [requires all implementations]
    ├─ Documentation (T148-T153) [requires all implementations]
    ├─ Code Quality (T154-T157) [requires all implementations]
    └─ DoD Verification (T158-T160) [requires all phases]
```

### Parallelizable Task Opportunities

Within each phase, tasks marked `[P]` are parallelizable:

- **Phase 2**: T010, T012-T020 can run in parallel (foundation infrastructure independent of each other)
- **Phase 3**: Validators (T023-T042) can run in parallel up to report generation (each validates different aspect)
- **Phase 4**: Node strategies (T048-T057) parallelizable within strategy, but strategies must complete before resume logic (T061-T064)
- **Phase 5**: Identity resolution (T079-T081) and checkpoint writing (T082-T087) can overlap
- **Phase 6**: Attachment and image replay services can parallelize (T088-T109) as they operate on different file types
- **Phase 7**: Field transform execution (T113-T123) can start after Phase 5 revision replay
- **Phase 8**: Tests (T140-T147) and documentation (T148-T153) run in parallel; final verification (T158-T160) last

### Connector Coverage Summary

**Total Connector Tasks**: 36 implementation tasks with explicit [Simulated], [AZURE DEVOPS], or [TFS] annotations.

- **US1 Prepare Phase**: 9 connector-specific tasks (node validation ×3, type validation ×3, artefact validation ×3)
- **US2 Node Readiness**: 12 connector-specific tasks (referenced paths ×3, replication ×3, translation ×3, resume ×3)
- **US3 Revision Replay**: 9 connector-specific tasks (creation ×3, update ×3, identity ×3)
- **US4 Attachments/Images**: 9 connector-specific tasks (attachment upload ×3, image upload ×3, skip handling ×3)
- **US5 FieldTransform**: 6 connector-specific tasks (transform verification ×3, lever control ×3)

**NO deferred implementations**: Every connector has explicit, independently testable tasks for each feature category.

---

## MVP Scope (Phase 3 + Key Foundation)

**Recommended MVP** (for initial delivery):
1. **Phase 1**: Setup (T001-T008) ✅
2. **Phase 2**: Foundation (T009-T020) ✅
3. **Phase 3**: User Story 1 - Prepare Phase (T021-T045) ✅
4. **Phase 4**: User Story 2 - Node Readiness (T046-T064) ✅
5. **Phase 5**: User Story 3 - Revision Replay (T065-T087) ✅ [**CRITICAL PATH**]

**MVP Outcomes**:
- ✅ Operators can validate export completeness before writing to target
- ✅ Required nodes created deterministically before first work item
- ✅ Work items and revisions streamed and replayed in package order
- ✅ Interruptions resumable without duplication via checkpoint
- ✅ Identity and path translation applied consistently

**Post-MVP Phases** (Phases 6-8):
- Phase 6: Attachments & Embedded Images (nicer to have, not critical for minimum viable slice)
- Phase 7: FieldTransform (extension of MVP, requires transform rules configured)
- Phase 8: Polish & Observability (hardening, tests, docs)

---

## Test Strategy

### Acceptance Testing (Reqnroll.MSTest)

Each feature file contains Gherkin scenarios matching user story acceptance criteria:

- **import-readiness-validation.feature**: 6 scenarios (US1 acceptance scenarios 1-6)
- **mandatory-node-creation.feature**: 4 scenarios (US2 acceptance scenarios 1-4)
- **deterministic-revision-replay.feature**: 5 scenarios (US3 acceptance scenarios 1-5)
- **attachment-replay-control.feature**: 4 scenarios (US4 acceptance scenarios 1-4)
- **field-transform-orchestration.feature**: 4 scenarios (US5 acceptance scenarios 1-4)

### Unit Testing

- **Checkpoint persistence**: Read/write atomicity, resume correctness, no duplication
- **Validators**: Each validator independently tested with missing/valid/error cases
- **Streaming**: Lazy enumeration, no full materialization, ascending order verification
- **Connectors**: Simulated, Azure DevOps, and TFS produce equivalent outcomes for identical inputs

### Live & Simulated Testing

- **Simulated**: Full import without external dependencies (CI/CD safe)
- **Azure DevOps**: Import to live Azure DevOps organization (requires PAT, scheduled testing)
- **TFS**: Import via .NET 4.8 TfsMigrationAgent (requires TFS environment)

---

## Acceptance Criteria for Completion

- [ ] All 160 numbered tasks (`T001`–`T160`) implemented and integrated
- [ ] All feature files (T135-T139) passing with 100% scenario coverage
- [ ] All unit tests (T140-T147) passing with >85% code coverage in Import module
- [ ] All three connectors (Simulated, AzureDevOps, TFS) have explicit task implementations
- [ ] Documentation (T148-T153) updated and reviewed
- [ ] SPDX headers (T154) present in all `.cs` files
- [ ] Clean build: `dotnet build --no-incremental` succeeds with no warnings
- [ ] All tests pass: `dotnet test` succeeds with no failures
- [ ] StyleCop analysis passes with no violations
- [ ] Definition of Done checklist (T158-T160) verified



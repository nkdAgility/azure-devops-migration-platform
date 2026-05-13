# Feature Specification: Work Item Import Support

**Feature Branch**: `035-workitem-import-support`  
**Created**: 2026-05-11  
**Status**: Draft  
**Input**: User description: "We need a spec to get import up and running for work items. So that includes nodes as wel as they are mandaroty, NodeTranslation and FieldTransform..."

## Clarifications

### Session 2026-05-11

- Q: What is the minimum work item import slice that must be in scope for this feature? → A: It must include revision replay, links, embedded images, and attachments at a minimum, along with the levers that enable, disable, or otherwise control those behaviours.
- Q: What export completeness must prepare validate? → A: The prepare phase must validate that the exported package contains all necessary artefacts for each enabled import category (revision folders, attachment binaries, embedded image binaries, field values that can be transformed).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Import Readiness Before Writing (Priority: P1)

As a migration operator, I want a prepare pass to tell me whether the exported package can be imported safely, so that I can fix blocking issues before any work item or node is written to the target project.

**Why this priority**: Import readiness is the gate that prevents partial or misleading migrations. If required node paths, work item types, or package artefacts are missing, the run must stop before writing to the target.

**Independent Test**: Can be fully tested by running a prepare pass against a package containing known missing node paths and unsupported work item types, then verifying that blocking findings are written to package reports and that import does not start.

**Acceptance Scenarios**:

1. **Given** an exported package whose referenced node paths are missing on the target and automatic node creation is disabled, **When** the operator runs prepare, **Then** the package records those missing paths as blocking findings and import is not allowed to start.
2. **Given** an exported package containing work item types that do not exist on the target, **When** the operator runs prepare, **Then** the package records those type mismatches as blocking findings and import is not allowed to start.
3. **Given** an exported package whose identities include unresolved mappings, **When** the operator runs prepare, **Then** the package records the unresolved identities for operator review without treating them as a blocking failure by default.
4. **Given** all required package artefacts, node paths, and work item types are valid, **When** the operator runs prepare, **Then** the package records a successful readiness result and import can proceed without rerunning source-side discovery.
5. **Given** an exported package with attachment replay enabled but required attachment binaries missing, **When** the operator runs prepare, **Then** the package records missing attachment artefacts as a blocking finding and import is not allowed to start.
6. **Given** an exported package with embedded image replay enabled but required embedded image binaries missing, **When** the operator runs prepare, **Then** the package records missing embedded image artefacts as a blocking finding and import is not allowed to start.

---

### User Story 2 - Create Mandatory Nodes Before Work Item Replay (Priority: P1)

As a migration operator, I want required area and iteration paths to exist before work item revisions are replayed, so that imported revisions do not fail or land in the wrong classification structure.

**Why this priority**: Nodes are a hard prerequisite for work item import. If the target does not have the required structure, the work item replay cannot be considered correct.

**Independent Test**: Can be fully tested by importing a package with referenced area and iteration paths into a target that initially lacks them, then verifying that the paths are created before the first work item revision is applied.

**Acceptance Scenarios**:

1. **Given** a package containing referenced node paths required by exported work items, **When** import starts with node creation enabled, **Then** the missing area and iteration paths are created before any work item revision is written.
2. **Given** a package containing a full source classification tree and source-tree replication is enabled, **When** import starts, **Then** the target receives the required classification structure before work item replay begins.
3. **Given** a source path needs remapping for the target project, **When** node preparation and import run, **Then** NodeTranslation rules are applied consistently so the created target paths match the intended target structure.
4. **Given** the node import is interrupted after some required paths are created, **When** the operator resumes the run, **Then** already-confirmed paths are not duplicated and the remaining required paths are completed before work item replay resumes.

---

### User Story 3 - Replay Work Item Revisions and Links in Package Order (Priority: P1)

As a migration operator, I want work item revisions and revision-owned links replayed from the exported package in the same deterministic order they were written, so that the target ends up with the correct item history, identity values, links, and classification paths.

**Why this priority**: This is the core migration outcome. The package is the source of truth, and replay must be deterministic, resumable, and package-driven.

**Independent Test**: Can be fully tested by importing a package with multiple work items and revisions, interrupting the run mid-stream, then resuming and verifying that the final target state matches a clean uninterrupted replay.

**Acceptance Scenarios**:

1. **Given** a package with work item revision folders in canonical chronological order, **When** import runs, **Then** revisions are processed in that package order without loading the full revision set into memory.
2. **Given** the first revision for a source work item has not yet been imported, **When** import reaches that revision, **Then** a corresponding target work item is created and the source-to-target mapping is recorded for later revisions.
3. **Given** later revisions for a source work item are encountered after a mapping already exists, **When** import reaches those revisions, **Then** the existing target work item is updated instead of creating a duplicate.
4. **Given** an import stops after some stages of a revision have completed, **When** the operator resumes the run, **Then** the import continues from the next incomplete stage for that revision instead of replaying completed stages.
5. **Given** a revision contains identity fields, node paths, and links, **When** the revision is applied, **Then** identities are resolved through the package’s mapping decisions, translated node paths are written, and links are applied only after the target item exists.

---

### User Story 4 - Replay Attachments and Embedded Images Under Operator Control (Priority: P1)

As a migration operator, I want attachments and embedded images replayed from the package under explicit extension controls, so that the imported work items retain their supporting artefacts while I can still disable expensive or risky behaviours when needed.

**Why this priority**: The user-visible minimum import slice includes attachments and embedded images. If those artefacts are missing or uncontrollable, the import is not operationally complete.

**Independent Test**: Can be fully tested by importing a package containing revision-owned attachment binaries and embedded image references, then verifying that enabled behaviours replay the artefacts and disabled behaviours skip them deterministically.

**Acceptance Scenarios**:

1. **Given** a revision folder contains attachment metadata and matching binaries, **When** attachments are enabled for import, **Then** the attachment binaries are replayed to the target work item from the package.
2. **Given** a revision contains embedded image references backed by package binaries, **When** embedded images are enabled for import, **Then** the image binaries are replayed and the written field values reference the target-side image locations.
3. **Given** attachments are disabled for the import run, **When** a revision containing attachments is replayed, **Then** the work item revision continues without replaying attachment binaries.
4. **Given** embedded images are disabled for the import run, **When** a revision containing embedded images is replayed, **Then** the work item revision continues without replaying embedded image binaries.

---

### User Story 5 - Apply FieldTransform After NodeTranslation During Import (Priority: P1)

As a migration operator, I want declarative field transformation rules to run during work item import after path translation has occurred, so that imported field values match the target project’s conventions without manual editing.

**Why this priority**: NodeTranslation alone handles area and iteration remapping, but real migrations also need declarative field cleanup and value remapping. The feature is incomplete without that transform step.

**Independent Test**: Can be fully tested by importing a package whose revisions require both path translation and field transformation, then verifying that the target receives the translated-and-transformed values rather than the original exported values.

**Acceptance Scenarios**:

1. **Given** a revision contains an area or iteration path that must be remapped, **When** the revision is prepared for writing, **Then** NodeTranslation is applied before any declarative field transforms run.
2. **Given** a field transform group changes one or more field values for the imported work item type, **When** the revision is written, **Then** the target receives the transformed values rather than the raw exported values.
3. **Given** the FieldTransform feature is disabled for the work item import run, **When** a revision is written, **Then** import proceeds without applying declarative field transforms.
4. **Given** a configured field transform cannot be applied successfully, **When** import reaches the affected revision, **Then** the run halts with a clear failure record rather than silently writing partially transformed data.

### Edge Cases

- What happens when the package is missing node artefacts needed for node readiness? The prepare pass must record a blocking failure and import must not start.
- What happens when a translated node path still cannot be resolved on the target? The run must follow the configured skip-or-halt policy, and the outcome must be visible in package reports and progress output.
- What happens when a work item type in the package does not exist on the target? The prepare pass must treat it as blocking so the operator corrects the target or mapping before import.
- What happens when an operator resumes after a mid-run interruption? The import must continue from the recorded folder and stage without duplicating already-completed node creation or work item replay.
- What happens when a field transform depends on a value that was changed by NodeTranslation? The transformed value must be based on the translated path, not the original exported path.
- What happens when no explicit identity mapping exists for a person referenced by a revision? The prepare pass must surface the unresolved identity for review and the import behaviour must remain deterministic.
- What happens when attachments or embedded images are disabled for a run? The revision replay must remain deterministic, skip only the disabled artefact categories, and record that those categories were intentionally not replayed.
- What happens when a package references an attachment or embedded image binary that is missing? The run must follow the configured skip-or-halt policy for that artefact type and keep the outcome visible to the operator.
- What happens when a revision folder referenced by a work item does not exist in the package? The prepare phase must record this as a blocking finding so the operator can verify export completeness.
- What happens when attachment metadata references binaries that do not exist in the package? The prepare phase must record missing attachment artefacts as blocking when attachment replay is enabled.
- What happens when embedded image metadata references binaries that do not exist in the package? The prepare phase must record missing embedded image artefacts as blocking when embedded image replay is enabled.
- What happens when FieldTransform rules reference fields that do not exist in the exported work items? The prepare phase must record the field mismatch and import must not start until the transform rules are corrected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST support a prepare phase for work item import that validates required package artefacts, target work item types, and required node readiness before any target writes occur.
- **FR-002**: The prepare phase MUST write package-local readiness reports for work items and nodes so operators can review blocking issues and rerun prepare after corrective action.
- **FR-003**: The prepare phase MUST treat missing required node paths as blocking when automatic node creation is disabled.
- **FR-004**: The prepare phase MUST treat unsupported target work item types found in the package as blocking.
- **FR-005**: The prepare phase MUST surface unresolved identities for operator review without blocking import by default.
- **FR-005a**: The prepare phase MUST validate that exported revision folders exist in the package for each work item in scope for import.
- **FR-005b**: The prepare phase MUST validate that required attachment binaries are present in the package when attachment replay is enabled.
- **FR-005c**: The prepare phase MUST validate that required embedded image binaries are present in the package when embedded image replay is enabled.
- **FR-005d**: The prepare phase MUST validate that field values referenced by FieldTransform rules exist in the exported revisions and can be transformed without type conflicts.
- **FR-006**: Import MUST ensure mandatory area and iteration paths exist on the target before the first work item revision is applied.
- **FR-007**: Import MUST support both referenced-path creation and full source-tree replication as node-readiness strategies, driven by NodeTranslation configuration.
- **FR-008**: Import MUST apply NodeTranslation rules consistently to area and iteration paths used during node readiness and during work item field replay.
- **FR-009**: Work item revisions MUST be replayed directly from the package in deterministic package order, one revision folder at a time.
- **FR-010**: Import MUST create a target work item when no mapping exists for the source work item and MUST reuse the existing mapped target item for later revisions.
- **FR-011**: Import MUST persist stage-aware checkpoint state so an interrupted run resumes from the next incomplete stage for the current revision folder.
- **FR-012**: Import MUST resolve identity-backed work item fields using the package’s mapping decisions before writing the revision to the target.
- **FR-013**: Import MUST apply node path translation before applying declarative field transforms.
- **FR-014**: Import MUST replay revision-owned links from the package after the target work item exists and in the same deterministic package-driven sequence as the revisions that own them.
- **FR-015**: Work item import MUST support extension levers that explicitly control revision replay, link replay, attachment replay, and embedded image replay for a run.
- **FR-016**: When revision replay is disabled, the import MUST skip work item revision processing entirely rather than partially replaying revisions.
- **FR-017**: When link replay is disabled, the import MUST skip link application while still allowing the rest of the enabled revision flow to proceed deterministically.
- **FR-018**: When attachment replay is enabled, the import MUST replay attachment binaries from the package and associate them with the correct target work item revision without consulting the source system.
- **FR-019**: When attachment replay is disabled, the import MUST skip attachment replay while preserving deterministic checkpoint behaviour for the revision.
- **FR-020**: When embedded image replay is enabled, the import MUST replay embedded image binaries from the package and update written work item content so it references the target-side image locations.
- **FR-021**: When embedded image replay is disabled, the import MUST skip embedded image replay while preserving deterministic checkpoint behaviour for the revision.
- **FR-022**: Work item import MUST support an optional FieldTransform capability that applies configured transform groups during import after NodeTranslation and before the target update is sent.
- **FR-023**: When FieldTransform is disabled for the run, the import MUST skip declarative field transforms and continue with the translated revision values.
- **FR-024**: A FieldTransform failure during import MUST halt the run with a clear error outcome rather than continuing with partially transformed data.
- **FR-025**: The import flow MUST remain package-driven and must not consult the source system during prepare or import.
- **FR-026**: The feature MUST provide equivalent observable outcomes for simulated testing and Azure DevOps target execution, and packages exported from Team Foundation Server MUST remain importable because import is driven solely by package content.
- **FR-027**: The feature scope for this increment MUST include prepare, node readiness, deterministic work item revision replay, link replay, attachment replay, embedded image replay, NodeTranslation, identity resolution, and FieldTransform.
- **FR-028**: The feature scope for this increment MUST NOT require unrelated import enhancements such as comment replay, migration audit comments, or revision-history attachments.

### Key Entities *(include if feature involves data)*

- **Exported Work Item Revision**: A package record representing one historical state change for a work item, including the fields and relationships that must be replayed in order.
- **Import Readiness Report**: A package report that records whether required work item types, node paths, and package artefacts are ready for a safe import run.
- **Referenced Node Path Set**: The distinct area and iteration paths in the package that must exist or be created before work item replay begins.
- **Source Tree Snapshot**: The exported classification structure used when node readiness is achieved by replicating the source hierarchy.
- **Identity Mapping Decision**: The operator-reviewed mapping outcome that determines how source identities are resolved during import.
- **NodeTranslation Rules**: Ordered mapping rules that convert source area and iteration paths into their intended target paths.
- **FieldTransform Group**: An ordered set of declarative field transformation rules applied to supported work item types during import.
- **Extension Control Lever**: The operator-facing enable or disable decision for revisions, links, attachments, embedded images, or field transformation during import.
- **Revision-Owned Attachment**: A binary artefact stored in the package for a specific work item revision and replayed only when attachment import is enabled.
- **Revision-Owned Embedded Image**: A package-backed binary referenced by revision content and replayed only when embedded image import is enabled.
- **Import Checkpoint**: The package-held record of the last processed revision folder and stage, used to resume safely after interruption.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operators can run prepare on a work item import package and receive a clear ready-or-blocked result before any target work items are written.
- **SC-002**: In a migration package containing required missing node paths, import creates those paths before the first work item revision is replayed when node creation is enabled.
- **SC-003**: An interrupted import resumed from package checkpoints produces the same final target result as an uninterrupted import, with no duplicate work items created.
- **SC-004**: For packages that require both path remapping and field transformation, target work items contain translated-and-transformed values rather than raw exported values.
- **SC-005**: For packages containing enabled links, attachments, and embedded images, the target work items contain those artefacts after replay, and disabling any one of those levers skips only that artefact category deterministically.
- **SC-006**: The simulated connector proves the full feature flow end to end without external dependencies, and Azure DevOps target execution produces the same functional outcomes for the same package content.
- **SC-007**: Operators can complete a representative work item import run without manual target-side cleanup caused by missing nodes, duplicate replay, missing minimum-scope artefacts, or partially applied field-transform behaviour.

## Assumptions

- The package has already been produced by a successful export and contains the work item artefacts required for import.
- The target project already exists; this feature only concerns import readiness and replay inside that target project.
- Identity mapping remains a prerequisite service and is reviewed through package reports before import.
- Team Foundation Server remains a source of package content rather than a required direct import target for this feature increment.
- Revisions, links, attachments, and embedded images are in scope for this spec increment because they are part of the minimum viable import slice.
- Comment replay, migration audit comments, and revision-history attachments remain out of scope for this spec increment and may be covered by later work.
- Existing package conventions, phase gates, and checkpoint semantics remain unchanged; this feature uses those contracts rather than replacing them.

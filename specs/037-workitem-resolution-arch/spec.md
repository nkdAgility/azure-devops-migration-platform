# Feature Specification: Work Item Orchestrator and Resolution Architecture Alignment

**Feature Branch**: `037-workitem-resolution-arch`  
**Created**: 2026-05-26  
**Status**: Draft  
**Input**: User description: "Update default spec to architecture: introduce WorkItemResolutionService wrapper, centralize cache management, and align connector boundaries."

## Clarifications

### Session 2026-05-26

- Q: Should this feature explicitly include the full Work Item orchestrator workflow and call-out sequence, not just resolution service boundaries? → A: Include full Work Item orchestrator workflow and call-out sequence as first-class scope.
- Q: How strict should the orchestrator call-out sequence be? → A: Define one mandatory sequence; deviations are failures.
- Q: Should the mandatory orchestrator sequence be visible during real runs? → A: Require runtime visibility of the sequence using emitted progress or log markers.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deterministic Resolution Flow (Priority: P1)

As a migration operator, I need the Work Item orchestrator to execute a consistent end-to-end workflow (startup policy setup, resolution preparation, revision dispatch, and downstream call-outs) so import outcomes are deterministic and resumable.

**Why this priority**: Deterministic resolution is the core safety requirement for reliable migration and checkpoint resume.

**Independent Test**: Run an import with a mixed set of existing and new work items; verify the orchestrator follows the defined end-to-end workflow and each revision is resolved through one shared path with expected create-or-update outcomes.

**Acceptance Scenarios**:

1. **Given** a revision with a valid prior mapping, **When** import processes the revision, **Then** the revision is updated using the mapped target item.
2. **Given** a revision with no prior mapping but a connector match exists, **When** import processes the revision, **Then** the target item is resolved and updated without creating a duplicate.
3. **Given** a revision with no mapping and no connector match, **When** import processes the revision, **Then** a new target item is created and mapped for future revisions.

---

### User Story 2 - Connector Boundary Consistency (Priority: P2)

As a platform maintainer, I need connector-specific components limited to lookup/query behavior so shared cache and lifecycle rules are applied consistently across connectors.

**Why this priority**: Clear boundaries reduce architecture drift and prevent connector-specific logic from breaking cross-connector behavior.

**Independent Test**: Review import behavior and integration tests to confirm cache lifecycle, mapping lifecycle, and resolution policy execution happen in one shared service while connectors provide only lookup results.

**Acceptance Scenarios**:

1. **Given** a configured lookup strategy, **When** import requests candidate resolution data, **Then** connector components return lookup results without managing cache lifecycle decisions.
2. **Given** a cache miss, **When** resolution runs, **Then** the shared resolution service applies the same miss and rebuild policy regardless of connector.

---

### User Story 3 - Connector Parity for Resolution Modes (Priority: P3)

As a migration operator using different target connectors, I need equivalent resolution capability across supported connectors so migration planning does not change by connector type.

**Why this priority**: Parity reduces migration risk and avoids degraded behavior when switching target connector.

**Independent Test**: Execute import test runs for each supported connector and confirm configured resolution modes are available through the same orchestration path.

**Acceptance Scenarios**:

1. **Given** a connector with supported lookup modes configured, **When** import starts, **Then** the modes are seeded and used through the shared resolution service.
2. **Given** a previously no-op connector path, **When** equivalent lookup behavior is now supported, **Then** import resolves items using that strategy path instead of no-op fallback behavior.

---

### Edge Cases

1. **Given** a stale mapping points to a missing target item, **When** resolution executes, **Then** the stale mapping is treated as unresolved, a fresh lookup is performed, and create-or-update decisioning continues without duplicate creation.
2. **Given** connector lookup returns multiple candidates for the same source work item, **When** resolution executes, **Then** deterministic tie-break rules are applied and the selected candidate is recorded with provenance.
3. **Given** cache rebuild is required during import, **When** processing resumes from checkpoint, **Then** stage progression and create-or-update outcomes remain identical to uninterrupted execution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST process every work item revision through a single shared resolution service before deciding create or update behavior.
- **FR-002**: The system MUST centralize resolution cache lifecycle management (initialize, seed, rebuild, stale handling) in that shared service.
- **FR-003**: The system MUST ensure connector-specific components are limited to lookup/query and normalization of lookup results.
- **FR-004**: The system MUST apply identical cache hit/miss and rebuild policies regardless of which connector is active.
- **FR-005**: The system MUST persist mapping and provenance outcomes after each resolution decision so resumed imports use the same deterministic state.
- **FR-006**: The system MUST route revision processing to create when unresolved and update when resolved, then continue replay activities for links, attachments, and comments.
- **FR-007**: The system MUST provide equivalent resolution strategy orchestration for all supported connectors where connector APIs support the required lookup mode.
- **FR-008**: The system MUST preserve existing field and node default policy behavior, including explicit project mapping defaults and no global exclusion of state values.
- **FR-009**: The system MUST expose checkpoints and progress states sufficient to resume import without reprocessing completed revision decisions.
- **FR-010**: The system MUST define and enforce one mandatory orchestrator call-out sequence across startup policy assembly, resolution preparation, deterministic revision dispatch, and post-resolution replay orchestration, and treat sequence deviations as failures.
- **FR-011**: The system MUST emit runtime-visible markers for each mandatory orchestrator stage so real-run progression and sequence deviations can be diagnosed.
- **FR-012**: The system MUST standardize WorkItems orchestration to a single `WorkItemsOrchestrator` abstraction contract consumed by module wrappers, and module wrappers must not instantiate concrete orchestrator implementations inline.
- **FR-013**: The system MUST keep `WorkItemsOrchestrator` as one symmetric phase contract containing Export, Prepare, Import, and Validate methods, without compile-time phase-method guards that remove phase methods per runtime target.
- **FR-014**: The system MUST enforce screaming architecture naming for WorkItems import runtime roles so class and contract names clearly express business/runtime intent (module wrapper, orchestrator, resolution service, revision processor, strategy) and do not collapse role boundaries.

### Key Entities *(include if feature involves data)*

- **Resolution Context**: The runtime state for resolving a source revision, including source identity, mapping keys, and active lookup mode.
- **Resolution Mapping Record**: Persisted relationship between source and target work items plus provenance metadata used for deterministic resume.
- **Lookup Candidate Set**: Normalized connector-provided candidate results used by the shared service to determine match/no-match outcomes.
- **Revision Processing Unit**: A single revision plus replay actions that are executed after resolution outcome is determined.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of imported revisions execute through one documented resolution flow before create/update decisioning.
- **SC-002**: For a representative migration package, resumed import after interruption reproduces the same create/update outcomes with no duplicate target work items.
- **SC-003**: All supported connectors execute configured resolution modes through the same orchestration path, with no no-op fallback where a supported lookup mode exists.
- **SC-004**: Architecture review of changed surfaces finds zero connector-specific implementations owning cache or mapping lifecycle management.
- **SC-005**: Using the same representative package and configuration, post-change import completion rate MUST be at least baseline and mismatch count (duplicate target creation, wrong create-or-update routing, or replay ordering mismatch) MUST be zero relative to baseline run evidence captured before implementation.
- **SC-006**: Conformance checks show 100% adherence to the mandatory orchestrator call-out sequence for all supported connectors during import execution.
- **SC-007**: For representative imports, operators can verify completion of each mandatory orchestrator stage from runtime progress or log markers without code inspection.
- **SC-008**: WorkItems module orchestration uses a single `WorkItemsOrchestrator` abstraction contract with zero inline concrete orchestrator construction in module wrappers.
- **SC-009**: `WorkItemsOrchestrator` follows the symmetric phase method shape (Export, Prepare, Import, Validate), with connector/runtime variance implemented behind abstractions rather than by contract shape divergence.
- **SC-010**: Architecture review confirms WorkItems import runtime naming and type responsibilities scream intent (module wrapper, orchestrator, resolution service, revision processor, strategy) with zero ambiguous role naming across touched files.

## Assumptions

- Current migration package structure, checkpoint format, and replay sequence remain valid and do not require user-facing format changes.
- Connector APIs already support the required lookup operations for at least one equivalent strategy path per supported connector.
- Existing policy defaults for field filtering and node translation remain the authoritative behavior and are not being redesigned in this feature.
- The feature is scoped to work item import orchestration and resolution behavior; unrelated migration phases are out of scope.

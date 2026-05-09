# Feature Specification: Package Manager Adoption

**Feature Branch**: `[034-package-manager-adoption]`  
**Created**: 2026-05-09  
**Status**: Draft  
**Input**: User description: "analyse .agents\context\package-manager.md and current code, create spec to implement Package Manager and migrate codebase usage"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Standardize Package Access (Priority: P1)

As a platform engineer, I need one package-facing boundary so package content, package metadata, and run logs are accessed consistently without each caller choosing paths and storage behavior independently.

**Why this priority**: This is the architectural core. Without it, package behavior remains fragmented and high-risk across orchestration, checkpointing, and telemetry flows.

**Independent Test**: Run a migration job and verify that package read/write/append operations are requested through a single package boundary contract while preserving existing package outputs.

**Acceptance Scenarios**:

1. **Given** a migration run with export, prepare, import, and validate phases, **When** package operations are executed, **Then** callers use the package boundary rather than composing raw package paths in runtime flow code.
2. **Given** a caller requests package metadata, **When** the metadata is persisted, **Then** authoritative package state is written to canonical authoritative locations and run-scoped copies are written only where audit mirroring applies.

---

### User Story 2 - Preserve Deterministic Resume and Phase Gates (Priority: P1)

As an operator, I need package-manager adoption to keep resumability and phase gating behavior unchanged so interrupted jobs continue safely and deterministically.

**Why this priority**: Correct resume and phase gate behavior are non-negotiable safety requirements for production migrations.

**Independent Test**: Interrupt a job mid-run, rerun the same job, and confirm it resumes from the expected checkpoint stage with no duplicate side effects.

**Acceptance Scenarios**:

1. **Given** a partially completed job with existing cursors and phase markers, **When** the job resumes, **Then** it continues from the same semantic checkpoint position as before this feature.
2. **Given** a job that requires phase prerequisites, **When** prerequisite markers are absent, **Then** automatic prerequisite execution and gating outcomes remain unchanged.

---

### User Story 3 - Ensure Cross-Connector Consistency (Priority: P2)

As a maintainer, I need package-manager behavior to be consistent across Simulated, Azure DevOps Services, and Team Foundation Server execution paths so no connector is left on legacy package access patterns.

**Why this priority**: Uneven connector adoption creates hidden drift and invalidates architecture guarantees.

**Independent Test**: Execute representative runs for all supported connectors and verify equivalent package semantics and expected outputs.

**Acceptance Scenarios**:

1. **Given** equivalent migration intents across connector types, **When** Simulated runs complete, **Then** package data, metadata, and log routing behavior follows the same contract semantics as the other supported connectors.
2. **Given** equivalent migration intents across connector types, **When** Azure DevOps Services runs complete, **Then** package data, metadata, and log routing behavior follows the same contract semantics as the other supported connectors.
3. **Given** equivalent migration intents across connector types, **When** Team Foundation Server runs complete, **Then** package data, metadata, and log routing behavior follows the same contract semantics as the other supported connectors.
4. **Given** connector-specific limitations, **When** a capability cannot be performed, **Then** behavior remains explicit, non-silent, and compliant with connector guardrails.

### Edge Cases

- A caller submits a package request without required scope information for a project-scoped artifact.
- A metadata write is flagged as run-related but the active run context is unavailable.
- A request references an unknown package content category.
- A resume operation encounters legacy package state locations from older packages.
- A long-running log stream rotates segments while the run is still active.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a single caller-facing package boundary for package content, authoritative metadata, and run-log append operations.
- **FR-002**: The package boundary MUST preserve canonical package layout rules, including authoritative root/project state and run-scoped audit/log separation.
- **FR-003**: The package boundary MUST preserve lexicographic streaming behavior for collection reads and MUST NOT introduce global buffering or in-memory sorting.
- **FR-004**: Runtime migration flows that currently build package paths directly MUST be migrated to use package-intent requests through the package boundary.
- **FR-005**: Package operations MUST fail fast with explicit errors when required context is missing, invalid, or incompatible with the requested operation.
- **FR-006**: The migration MUST retain existing resume semantics, phase gating semantics, and deterministic package outputs for equivalent inputs.
- **FR-007**: The feature MUST support all existing connector execution paths (Simulated, Azure DevOps Services, Team Foundation Server where supported).
- **FR-008**: Any remaining direct low-level persistence usage outside the package boundary MUST be limited to dedicated persistence internals and documented with rationale.
- **FR-009**: The package boundary MUST support run-log routing for both progress and diagnostics streams without changing operator-facing log availability.
- **FR-010**: Package-boundary operations MUST emit trace, metrics, and structured log telemetry aligned with repository observability requirements, including operation identity, outcome, duration, and correlation identifiers.
- **FR-011**: The feature MUST include behavioral test coverage proving package-manager routing, resume safety, and connector parity.
- **FR-012**: The feature MUST update architecture and context documentation so future contributors can implement package access through the package boundary by default.
- **FR-013**: All new package-boundary contracts and package-intent types MUST be added under `src/DevOpsMigrationPlatform.Abstractions.Agent/` and MUST NOT be added to higher/shared abstraction layers (including `src/DevOpsMigrationPlatform.Abstractions/`).

### Key Entities *(include if feature involves data)*

- **Package Request**: A typed request describing what package content is being read or persisted and in what scope.
- **Package Metadata Request**: A typed request for authoritative metadata categories and optional run-related mirroring behavior.
- **Package Log Request**: A typed request identifying run ID, log stream type, and append behavior expectations.
- **Package Payload**: Content stream and metadata associated with a package data or metadata operation.
- **Package Routing Rule**: Canonical mapping from typed package intent to authoritative state location, audit copy location, and log stream destination.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of package writes in migration runtime flows execute through the package boundary contract, excluding explicitly documented low-level persistence internals.
- **SC-002**: All existing resume and phase-gate behavior tests continue to pass with no regressions.
- **SC-003**: Equivalent migration runs for Simulated, Azure DevOps Services, and Team Foundation Server produce unchanged canonical package structure and semantic outputs.
- **SC-004**: For each package-boundary operation type (content, metadata, log append), tests prove emission of at least one span, one metric record, and one structured log carrying `job.id`, operation name, outcome, and duration.

### Error Contract

- Missing required operation context MUST produce a deterministic validation error (`PackageValidationException`) with a stable error code.
- Unsupported operation/context combinations MUST produce a deterministic operation error (`PackageOperationException`) with a stable error code.
- Error events MUST be emitted as structured logs with operation identity and correlation fields.

## Assumptions

- Existing package layout conventions remain authoritative and are not being redesigned in this feature.
- Existing storage backends remain in use; this feature standardizes the caller-facing boundary above them.
- Migration to the package boundary can be delivered incrementally so long as each merged increment preserves runtime correctness and connector coverage.
- Current tests and scenario assets provide sufficient baseline behavior coverage to detect regressions during adoption.

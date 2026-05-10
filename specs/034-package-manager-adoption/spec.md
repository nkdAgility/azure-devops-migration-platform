# Feature Specification: Package Manager Adoption

**Feature Branch**: `[034-package-manager-adoption]`  
**Created**: 2026-05-09  
**Status**: Planned  
**Input**: User description: "analyse .agents\context\package-manager.md and current code, create spec to implement Package Manager and migrate codebase usage"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Standardize Package Access (Priority: P1)

As a platform engineer, I need `IPackageAccess` to be the only permitted package-facing boundary so package content, package metadata, and run logs are accessed consistently without callers choosing paths or storage behavior independently.

**Why this priority**: This is the architectural core. Without it, package behavior remains fragmented and high-risk across orchestration, checkpointing, and telemetry flows.

**Independent Test**: Run a migration job and verify that package read/write/append operations are requested through `IPackageAccess`, while preserving existing package outputs.

**Acceptance Scenarios**:

1. **Given** a migration run with export, prepare, import, and validate phases, **When** package operations are executed, **Then** callers use `IPackageAccess` rather than composing raw package paths in runtime flow code.
2. **Given** a caller requests package metadata, **When** the metadata is persisted, **Then** authoritative package state is written to canonical authoritative locations and run-scoped copies are written only where audit mirroring applies.
3. **Given** module-owned content beneath a package-owned prefix, **When** content is requested or persisted, **Then** the caller supplies the module-owned suffix through `IPackageContentAddress` and the package boundary does not infer that suffix.

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

- A caller submits a package content request without the package-owned scope required for a project-scoped artefact.
- A caller omits `IPackageContentAddress` for module-owned content that requires a module-relative suffix.
- A supplied `IPackageContentAddress.RelativePath` is absolute or escapes the module root.
- A metadata write is flagged as run-related but the active run context is unavailable.
- A resume operation encounters legacy package state locations from older packages.
- A long-running log stream rotates segments while the run is still active.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST use `IPackageAccess` as the only permitted caller-facing package boundary for package content, authoritative metadata, and run-log append operations.
- **FR-002**: The package boundary MUST preserve canonical package layout rules, including authoritative root/project state and run-scoped audit/log separation.
- **FR-003**: The package boundary MUST preserve lexicographic streaming behavior for collection reads and MUST NOT introduce global buffering or in-memory sorting.
- **FR-004**: Runtime migration flows that currently build package paths directly MUST be migrated to use typed package requests through `IPackageAccess`.
- **FR-005**: Package operations MUST fail fast with explicit errors when required context is missing, invalid, or incompatible with the requested operation.
- **FR-006**: The migration MUST retain existing resume semantics, phase gating semantics, and deterministic package outputs for equivalent inputs.
- **FR-007**: The feature MUST support all existing connector execution paths (Simulated, Azure DevOps Services, Team Foundation Server where supported).
- **FR-008**: Package-facing runtime code MUST NOT perform direct package reads or writes through `IArtefactStore`, `IStateStore`, or similar lower-level persistence systems; those operations MUST go through `IPackageAccess`.
- **FR-009**: The package boundary MUST support run-log routing for both progress and diagnostics streams without changing operator-facing log availability.
- **FR-010**: Package-boundary operations MUST emit trace, metrics, and structured log telemetry aligned with repository observability requirements, including operation identity, outcome, duration, and correlation identifiers.
- **FR-011**: The feature MUST include behavioral test coverage proving package-manager routing, resume safety, and connector parity.
- **FR-012**: The feature MUST update architecture and context documentation so future contributors can implement package access through the package boundary by default.
- **FR-013**: The package boundary MUST use `PackageContentContext` and `PackageContentKind` for content requests, `PackageMetaContext` for metadata requests, and `PackageLogContext` for run-log appends rather than path-based content routing.
- **FR-014**: The package boundary MUST accept module-owned suffixes only through caller-supplied `IPackageContentAddress.RelativePath` and MUST NOT infer module layout from DTO names, route segments, or implicit type conventions.
- **FR-015**: Route validation MUST reject absolute paths and relative paths that escape the module root.
- **FR-016**: `LegacyPackagePathShim` MAY exist only as a transitional compatibility adapter and MUST NOT be treated as the target package architecture.
- **FR-017**: `PackageMigrationConfigLoader` MUST load `migration-config.json` through mandatory `IPackageAccess` usage and MUST NOT retain direct package-store fallback behavior.
- **FR-018**: All new package-boundary contracts and typed package request models MUST be added under `src/DevOpsMigrationPlatform.Abstractions.Agent/` and MUST NOT be added to higher/shared abstraction layers (including `src/DevOpsMigrationPlatform.Abstractions/`).

### Key Entities *(include if feature involves data)*

- **IPackageAccess**: The canonical package-facing contract for content, metadata, and run-log operations.
- **IPackageContentAddress**: A caller-supplied module-relative address that provides only the suffix owned by the module beneath the package-owned prefix.
- **PackageContentContext**: The typed content request and write context containing package-owned scope plus an optional caller-supplied address.
- **PackageContentKind**: The closed content-category set used by the package boundary: `Artefact`, `Collection`, and `Manifest`.
- **LegacyPackagePathShim**: A transitional compatibility adapter that keeps older string-path callers alive without defining the target architecture.
- **Package Routing Rule**: Canonical mapping from typed package content, metadata, and log intent to authoritative state locations, optional run-audit copies, and run-log destinations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of package-facing runtime reads, writes, and appends execute through `IPackageAccess`.
- **SC-002**: All existing resume and phase-gate behavior tests continue to pass with no regressions.
- **SC-003**: Equivalent migration runs for Simulated, Azure DevOps Services, and Team Foundation Server produce unchanged canonical package structure and semantic outputs.
- **SC-004**: For each package-boundary operation type (content, metadata, log append), tests prove emission of at least one span, one metric record, and one structured log carrying `job.id`, operation name, outcome, and duration.
- **SC-005**: All route-validation tests reject absolute or escaping module-relative addresses before any package write occurs.

### Error Contract

- Missing required operation context MUST produce a deterministic validation error (`PackageValidationException`) with a stable error code.
- Unsupported operation/context combinations MUST produce a deterministic operation error (`PackageOperationException`) with a stable error code.
- Error events MUST be emitted as structured logs with operation identity and correlation fields.

## Assumptions

- Existing package layout conventions remain authoritative and are not being redesigned in this feature.
- Existing storage backends remain in use; this feature standardizes the caller-facing boundary above them.
- Metadata and log operations remain distinct first-class package concepts rather than being folded into generic content routing.
- Migration to the explicit package boundary can be delivered incrementally so long as each merged increment preserves runtime correctness and connector coverage.
- Remaining uses of `LegacyPackagePathShim` are migration debt to be eliminated over time, not part of the desired steady state.
- Current tests and scenario assets provide sufficient baseline behavior coverage to detect regressions during adoption.

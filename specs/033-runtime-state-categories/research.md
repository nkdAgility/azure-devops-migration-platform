# Research: Runtime State Categories and Resume Semantics Alignment

## Decision 1: Authoritative state split is strict and role-based

- **Decision**: Keep root `.migration/` authoritative for package-wide orchestration only, project `/{org}/{project}/.migration/` authoritative for project-scoped module resume, and `.migration/runs/<runId>/` audit-only.
- **Rationale**: This prevents run artifact leakage into orchestration behavior and eliminates ambiguity across reruns.
- **Alternatives considered**:
  - Keep mixed authority in root + run folders (rejected: non-deterministic and doc/runtime drift).
  - Move all state into project folders (rejected: package-wide phase gates are global concerns).

## Decision 2: Cursor identity must include action + module

- **Decision**: Use action-qualified module resume identity for inventory/export/import so they never share a cursor namespace.
- **Rationale**: Prevents cross-phase collision and incorrect resume points for the same module family.
- **Alternatives considered**:
  - Module-only cursor identity (rejected: collisions between export/import/inventory).
  - Single unified cursor per project (rejected: insufficient granularity and unsafe semantics).

## Decision 3: Fine-grained progress and reasonable save cadence are universal

- **Decision**: All long-running processing emits fine-grained progress; save cadence is as fine as reasonable by workload.
- **Rationale**: Improves operator visibility and minimizes replay after interruption.
- **Alternatives considered**:
  - Coarse project-level updates only (rejected: poor observability and larger replay windows).
  - Per-item durable saves for all workloads (rejected: excessive overhead for some modules).

## Decision 4: Work items get stricter cadence than generic workloads

- **Decision**: Work-item processing persists at completed work-item-batch boundaries and emits work-item-level progress updates.
- **Rationale**: Work-item operations are high-volume and interruption-prone, requiring stronger resumability and traceability.
- **Alternatives considered**:
  - Save only at module completion (rejected: unacceptable replay cost).
  - Save every single work item as mandatory rule (rejected: may be unnecessary overhead versus batch-level checkpoints).

## Decision 5: Inventory aligns with the same state semantics

- **Decision**: Inventory must use the same action-aware authoritative state model and avoid legacy fallback behavior that conflicts with scope rules.
- **Rationale**: Avoids one-off behavior that undermines the contract and introduces hidden resume semantics.
- **Alternatives considered**:
  - Keep inventory custom cursor lifecycle (rejected: inconsistent and harder to reason about).

## Decision 6: Observability contract explicitly includes state and cadence behavior

- **Decision**: State path resolution, cursor advancement, batch-save commits, and progress cadence each have explicit observability coverage (spans/metrics/logs/progress events).
- **Rationale**: This feature changes correctness-critical control flow; observability is required to detect regressions and replay risk.
- **Alternatives considered**:
  - Only log final status (rejected: insufficient for diagnosing partial replay and cursor drift).

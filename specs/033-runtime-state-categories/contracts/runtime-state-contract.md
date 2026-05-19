# Runtime State Contract

## Purpose

Define the authoritative state boundaries and progress/save cadence contract for runtime migration processing.

## Contract Statements

1. **Authoritative package orchestration state**
   - Root `.migration/` is authoritative for package-wide orchestration decisions only.

2. **Authoritative scoped resume state**
   - `/{org}/{project}/.migration/` is authoritative for project-scoped module resume.
   - `/{org}/.migration/` is authoritative for organisation-scoped module resume.
   - `/.migration/` is authoritative for package-scoped module resume.
   - Cursor identity must include action and module.
   - Reads use precedence project → org → package; writes/resets target the most-specific resolved scope.

3. **Run-scoped audit-only state**
   - `.migration/runs/<runId>/` contains audit snapshots and logs only.
   - Run-scoped files must not drive resume, phase-gate, or orchestration behavior.

4. **Fine-grained progress contract**
   - Processing operations emit progress notifications at the finest practical cadence.
   - Progress granularity must allow operators to follow in-flight advancement.

5. **Reasonably fine-grained save contract**
   - Processing operations persist resume state at the finest reasonable cadence for that workload.
   - Save cadence must minimize replay after interruption while avoiding unreasonable persistence overhead.

6. **Work-item-specific cadence**
   - Work-item processing persists durable state at completed work-item-batch boundaries.
   - Work-item processing emits work-item-level progress updates.

## Compatibility and Validation Rules

- Action namespace collisions are invalid.
- Run-scope authority use is invalid.
- Legacy fallback behavior may be used only for compatibility reads where explicitly defined, and must not override authoritative-scope precedence.
- Contract compliance must be covered by:
  - unit tests for path authority and identity semantics
  - unit tests for save/progress cadence behavior
  - end-to-end tests for interruption and resume replay minimization

# Quickstart: Validate Runtime State Categories Alignment

## Prerequisites

- Feature branch: `033-runtime-state-categories`
- Feature spec and plan present in `specs/033-runtime-state-categories/`
- Local environment configured for normal build/test workflow

## 1. Confirm authoritative state behavior

1. Run a scenario that executes inventory/export/import with an intentional interruption.
2. Verify resume and phase gates resolve from:
   - root `.migration/` (package orchestration)
   - `/{org}/{project}/.migration/` (project module resume)
3. Verify `.migration/runs/<runId>/` does not alter resume/phase decisions.

## 2. Confirm action-qualified cursor identity

1. Run inventory, export, and import for the same project/module family.
2. Validate state keys/namespaces are action-separated.
3. Confirm no cross-action cursor reuse or overwrite.

## 3. Confirm fine-grained progress and save cadence

1. Run long-running operations and observe progress output.
2. Verify progress shows steady, fine-grained advancement.
3. For work items:
   - verify work-item-level progress updates
   - verify save state persists at completed work-item-batch boundaries

## 4. Confirm interruption replay minimization

1. Interrupt during active processing.
2. Resume with same package.
3. Validate restart occurs from latest durable checkpoint with minimal replay.

## 5. Execute repository validation commands

1. `dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo`
2. `dotnet test DevOpsMigrationPlatform.slnx --nologo`

## Expected Outcome

- Runtime behavior aligns with the four state categories in spec and plan.
- Resume correctness and visibility are improved without violating package/streaming guardrails.

## Execution Notes (T073-T078)

- RED-first gate runs were enforced by introducing failing runtime-state tests before implementation updates in this branch.
- Green-state verification evidence is recorded from the final required commands:
  - `dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo`
  - `dotnet test DevOpsMigrationPlatform.slnx --nologo`
- Commit tasks `T076`-`T078` are satisfied as implementation-phase checkpoints in this session but no git commit was created, per explicit instruction.

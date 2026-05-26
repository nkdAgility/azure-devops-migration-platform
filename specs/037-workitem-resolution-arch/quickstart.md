# Quickstart — Work Item Orchestrator and Resolution Architecture Alignment

## 1. Preconditions

- On branch: `037-workitem-resolution-arch`
- Review:
  - `specs/037-workitem-resolution-arch/spec.md`
  - `specs/037-workitem-resolution-arch/plan.md`
  - `specs/037-workitem-resolution-arch/research.md`

## 2. Implementation Focus

1. Standardize module/orchestrator split for WorkItems export/import.
2. Remove inline concrete orchestrator construction from module wrappers.
3. Keep canonical runtime chain:
   `Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`
4. Preserve deterministic import flow and runtime stage visibility.
5. Keep full adapter coverage (Simulated, AzureDevOpsServices, TeamFoundationServer where supported).

## 3. Verify Architecture Alignment

1. Confirm module wrappers are thin and delegate sequencing.
2. Confirm orchestrators own workflow sequencing/checkpoint/stage behavior.
3. Confirm Package operations occur through package boundary abstractions.
4. Confirm Adapter mechanics are isolated to adapter implementations.
5. Confirm Strategy behavior is strategy-scoped and invoked by orchestrator flow.

## 4. Verify Runtime Behavior

1. Run import path with mixed existing/new items.
2. Confirm deterministic create/update outcomes with resume.
3. Confirm stage markers expose progression and failures.
4. Confirm no duplicate target item creation on resume.

## 5. Verify Adapter Coverage

1. Simulated path behavior present and deterministic.
2. AzureDevOpsServices path behavior present.
3. TeamFoundationServer path behavior present where API supports.

## 6. Planning Exit

After tasks are generated and architecture checks pass, proceed with:
- `/speckit-tasks`

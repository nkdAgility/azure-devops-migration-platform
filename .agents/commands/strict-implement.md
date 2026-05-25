---
description: "Fail-closed implementation orchestration that blocks shortcuts, stubs, and partial states without editing Speckit core."
---

# Strict Implement (Fail-Closed Wrapper)

Use this command instead of `/speckit.implement` for delivery work in this repository.

It is a repository-local orchestration wrapper and is intentionally strict:
- no silent hook bypass,
- no checklist bypass,
- no requirement-to-task coverage gaps,
- no missing mandatory orchestrator artifacts.

## Mandatory Sequence

1. Run repository orchestrator preflight (hard fail on any issue):
   ```powershell
   pwsh ./scripts/guardrails/validate-orchestrator.ps1
   ```

2. Resolve the active feature context:
   ```powershell
   .specify/scripts/powershell/check-prerequisites.ps1 -Json -RequireTasks -IncludeTasks
   ```
   Use `FEATURE_DIR` from the script output.

3. Enforce checklist completeness (hard fail on any incomplete checklist item):
   ```powershell
   pwsh ./scripts/guardrails/enforce-checklists.ps1 -FeatureDir "<FEATURE_DIR>"
   ```

4. Enforce requirements-to-tasks mapping (hard fail on any unmapped requirement ID):
   ```powershell
   pwsh ./scripts/guardrails/enforce-task-coverage.ps1 -FeatureDir "<FEATURE_DIR>"
   ```

5. Run mandatory pre-implementation architecture/task gates:
   ```text
   /nkda-core-tasks-architecture-compliance
   /speckit.superb.review
   /speckit.superb.tdd
   ```

6. Execute implementation:
   ```text
   /speckit.implement
   ```

7. Run mandatory post-implementation gates:
   ```text
   /nkda-core-implementation-architecture-compliance
   /nkda-core-definition-of-done
   /speckit.superb.verify
   ```

## Fail-Closed Rules

- Any preflight parser failure is blocking.
- Any missing mandatory hook command/skill artifact is blocking.
- Any incomplete checklist is blocking.
- Any requirement coverage gap is blocking.
- Any non-pass verdict from mandatory gates is blocking.

Do not downgrade this command to advisory behavior.

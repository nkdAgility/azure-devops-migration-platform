---
description: Manual target-suite, architecture-refresh, and rebuild-planning entry point for the NKDA TDD Safety Net workflow.
---

# Command: nkda-tddsn-design

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this command.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for commands.

## Purpose

Create the target behavioural safety net and architecture update.

## Inputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- relevant subsystem architecture docs
- relevant subsystem production and test files when needed for clarification

## Workflow Steps

1. read `.agents/skill-sets/nkda-tddsn/manifest.md`
2. read `.agents/skill-sets/nkda-tddsn/workflow.md`
3. consume `.output/nkda-tddsn/<subsystem>/01-assessment.md`
4. run `nkda-tddsn-target-suite-design`
5. run `nkda-tddsn-architecture-refresh`
6. run `nkda-tddsn-rebuild-planning`
7. produce `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
8. produce `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
9. produce `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`

## Outputs

- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`

## Stop Conditions

- `01-assessment.md` is missing
- the behaviour model gate cannot be satisfied with confidence
- the target suite gate cannot be satisfied with explicit tests, assertions, and status decisions

## Must Not

- modify production code
- implement tests unless explicitly instructed

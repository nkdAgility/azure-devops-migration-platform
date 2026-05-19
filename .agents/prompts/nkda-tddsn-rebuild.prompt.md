---
description: Manual implementation entry point for rebuilding the approved TDD safety net and making only minimal production changes.
---

# Command: nkda-tddsn-rebuild

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this command.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for commands.

## Purpose

Implement the approved target test suite and minimal production code changes.

## Inputs

- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- relevant production and test files

## Workflow Steps

1. read `.agents/skill-sets/nkda-tddsn/manifest.md`
2. read `.agents/skill-sets/nkda-tddsn/workflow.md`
3. consume `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
4. consume `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
5. update tests according to the approved plan using `nkda-tddsn-test-implementation`
6. update production code only where required by approved target behavioural tests using `nkda-tddsn-code-adjustment`
7. produce `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`

## Outputs

- `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`

## Stop Conditions

- `02-target-test-suite.md` is missing
- `04-rebuild-plan.md` is missing
- the minimal change gate cannot be stated for a production code change
- scope starts expanding beyond the approved target suite

## Must Not

- expand scope beyond the target suite
- rewrite unrelated code
- add coverage padding
- silently change public behaviour

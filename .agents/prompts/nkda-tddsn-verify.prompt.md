---
description: Manual verification entry point for the NKDA TDD Safety Net workflow.
---

# Command: nkda-tddsn-verify

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this command.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for commands.

## Purpose

Verify the rebuilt test safety net.

## Inputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`
- current git diff
- relevant PowerShell test command

## Workflow Steps

1. read `.agents/skill-sets/nkda-tddsn/manifest.md`
2. read `.agents/skill-sets/nkda-tddsn/workflow.md`
3. consume all prior artefacts
4. inspect the current git diff
5. run relevant tests using PowerShell
6. run `nkda-tddsn-verification-review`
7. run `nkda-tddsn-assessment` as a final comparison aid when needed
8. verify target suite coverage
9. verify architecture documentation alignment
10. verify remaining drift risks
11. produce `.output/nkda-tddsn/<subsystem>/06-verification.md`

## Outputs

- `.output/nkda-tddsn/<subsystem>/06-verification.md`

## Stop Conditions

- any prior artefact is missing
- no relevant PowerShell test command can be identified
- test evidence is missing

## Must Not

- claim success without test evidence

---
description: Manual assessment entry point for the NKDA TDD Safety Net workflow.
---

# Command: nkda-tddsn-assess

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this command.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for commands.

## Purpose

Manual assessment only.

## Inputs

- subsystem name or path
- relevant subsystem production files
- relevant subsystem test files
- relevant subsystem documentation
- relevant repository guardrails

## Workflow Steps

1. read `.agents/skill-sets/nkda-tddsn/manifest.md`
2. read `.agents/skill-sets/nkda-tddsn/workflow.md`
3. inspect `.agents/skills/nkd-tdd-assessment/SKILL.md`
4. read relevant guardrails and subsystem documentation
5. inspect production and test files
6. build the behaviour model
7. assess existing tests with `nkda-tddsn-assessment`
8. produce `.output/nkda-tddsn/<subsystem>/01-assessment.md`

## Outputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`

## Stop Conditions

- subsystem scope cannot be identified
- required evidence for a minimally credible behaviour model is unavailable
- a required guardrail is missing and the assessment must continue only as partial analysis

## Must Not

- modify tests
- modify production code
- update architecture documentation

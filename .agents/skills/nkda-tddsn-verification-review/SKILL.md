---
name: nkda-tddsn-verification-review
description: Verifies the rebuilt TDD safety net against the target suite, architecture updates, minimal change gate, and repository guardrails, and produces 06-verification.md.
---

# Skill: NKDA TDD Safety Net Verification Review

## Responsibilities

- verify final state against the target suite and guardrails
- run relevant tests using PowerShell
- compare changed tests against `02-target-test-suite.md`
- check no weak replacement tests were introduced
- check architecture documentation matches implementation
- check remaining drift risks
- check that the implementation followed the minimal change gate
- produce `.output/nkda-tddsn/<subsystem>/06-verification.md`

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`
- current git diff
- relevant PowerShell test command output

## Verification Rules

- PowerShell must be used for commands
- do not claim success without test evidence
- verify target suite coverage explicitly
- verify architecture documentation alignment explicitly
- report remaining drift risks and any guardrail violations
- if required guardrails are missing, report that as a partial-analysis warning

## Output Contract

Produce `.output/nkda-tddsn/<subsystem>/06-verification.md` using exactly this structure:

```text
# Verification Report: <subsystem>

## Test Command

```powershell
<command>
```

## Test Result

<result>

## Target Suite Coverage

| Target Test | Status | Evidence |
| ----------- | ------ | -------- |
| <test> | Implemented/Missing/Changed | <evidence> |

## Guardrail Review

* testing rules
* coding standards
* architecture boundaries
* observability requirements
* definition of done

## Remaining Drift Risks

* <risk>

## Final Classification

<pass/fail/partial>

## Required Follow-Up

* <action>
```
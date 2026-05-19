---
description: End-to-end autonomous execution entry point for the NKDA TDD Safety Net workflow.
---

# Command: nkda-tddsn-autonomous

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this command.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for commands.

## Purpose

End-to-end autonomous execution.

## Inputs

- subsystem name or path
- relevant subsystem documentation
- relevant production and test files
- repository guardrails

## Workflow Steps

1. read the manifest and workflow
2. inspect `.agents/skills/nkd-tdd-assessment`
3. read all available guardrails and subsystem documentation
4. discover production and test files
5. build the subsystem behaviour model
6. assess current tests
7. identify drift risks
8. design the proposed target test suite
9. update or propose subsystem architecture documentation changes
10. create a rebuild plan
11. rebuild tests
12. make minimal production code changes required by the tests
13. run relevant tests using PowerShell
14. verify the final result against the target suite
15. produce all six workflow artefacts

## Outputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`
- `.output/nkda-tddsn/<subsystem>/06-verification.md`

## Stop Conditions

- assessment cannot produce a credible behaviour model
- target suite design cannot satisfy the target suite gate
- minimal change gate cannot be satisfied for required production changes
- verification cannot produce evidence

## Must Not

- skip assessment
- skip target suite design
- skip verification
- expand scope beyond the subsystem
- invent undocumented behaviour without marking it as inferred

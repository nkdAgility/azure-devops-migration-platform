---
description: "Run the NKDA TDD Safety Net autonomous pipeline for the active subsystem."
---

# NKDA TDDSN Autonomous Command

This command is the command-surface artifact required by the repository workflow guardrails.

## Invocation

```text
/nkda-tddsn-autonomous <subsystem-scope>
```

## Required Behavior

Execute the autonomous six-phase pipeline for the named subsystem:
1. Assessment
2. Target suite design
3. Architecture refresh
4. Rebuild planning
5. Test + minimal code implementation
6. Verification review

## Required Outputs

Produce these artifacts under `.output/nkda-tddsn/<subsystem>/`:
- `01-assessment.md`
- `02-target-test-suite.md`
- `03-architecture-update.md`
- `04-rebuild-plan.md`
- `05-implementation-summary.md`
- `06-verification.md`

Any missing artifact is a fail condition.

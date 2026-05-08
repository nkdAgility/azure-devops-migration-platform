---
name: nkda-tddsn-code-adjustment
description: Makes only the minimal production code changes required by the approved target behavioural tests while preserving architecture boundaries.
---

# Skill: NKDA TDD Safety Net Code Adjustment

## Responsibilities

- make minimal production code changes required by the approved target tests
- introduce testability seams only where justified
- preserve architecture boundaries
- do not rewrite unrelated code
- do not silently change public behaviour
- if behaviour changes, ensure the architecture update captures it
- prefer explicit dependencies over hidden state
- introduce clocks, ID providers, storage ports, schedulers, or adapters only when required to make valuable tests deterministic and behaviour-focused

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- current failing or missing target tests

## Minimal Change Gate

Before changing production code, state:

- which failing or missing target test requires the change
- what behaviour is being corrected or enabled
- why the change is minimal
- whether the architecture documentation needs to change

Do not proceed if that gate cannot be satisfied.
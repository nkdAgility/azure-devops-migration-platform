---
name: nkda-tddsn-rebuild-planning
description: Converts the target behavioural suite into a sequenced rebuild plan with stopping points, dependencies, and minimal seam guidance.
---

# Skill: NKDA TDD Safety Net Rebuild Planning

## Responsibilities

- convert the target suite into an implementation sequence
- identify tests to keep, rewrite, delete, merge, split, or add
- identify safe stopping points
- identify production code seams needed for valuable tests
- identify dependencies between tests and production changes
- sequence the rebuild so critical drift risks are protected first
- produce `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- relevant source and test files for dependency and seam confirmation

## Planning Rules

- critical drift risks first
- rewrite or delete weak tests only when the approved target suite replaces their protected behaviour
- identify the smallest safe stopping points that still leave the subsystem in a coherent state
- identify seams only when they are justified by valuable tests
- do not expand scope beyond the target suite

## Output Contract

Produce `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md` using exactly this structure:

```text
# TDD Safety Net Rebuild Plan: <subsystem>

## Priority 1: Stop Critical Drift

- <action>

## Priority 2: Replace Weak Verification Tests

- <action>

## Priority 3: Add Boundary Protection

- <action>

## Priority 4: Improve Design Pressure

- <action>

## Priority 5: Consolidate and Clean Up

- <action>

## Safe Stopping Points

- <point>

## Production Code Seams Required

- <seam>
```
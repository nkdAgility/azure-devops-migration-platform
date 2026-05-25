---
name: nkda-core-tasks-architecture-compliance
description: Design-time compliance review for tasks.md — validates task-level implementation coverage against spec.md and plan.md across all architecture perspectives and active guardrails before implementation.
---

# Skill: NKDA Core Tasks Architecture Compliance

Run this after task generation and before implementation.

## Mandatory Checks

1. Load `spec.md`, `plan.md`, and `tasks.md` for the active feature.
2. Validate requirement-to-task coverage:
   - each requirement has at least one concrete task,
   - no requirement is left partial or unmapped.
3. Validate architecture intent in tasks against all mandatory perspectives:
   - Modular Monolith
   - Clean Architecture
   - Hexagonal
   - Vertical Slice
   - Screaming Architecture
   - Architecture Deepening
4. Validate no task instructs shortcuts, stubs, deferred follow-ups, or partial refactor states.
5. Emit explicit pass/fail evidence per perspective.

## Verdict Rules

- **PASS**: full requirement coverage + all perspective checks pass.
- **FAIL**: any gap, partial mapping, or failed perspective.

A `FAIL` blocks implementation.

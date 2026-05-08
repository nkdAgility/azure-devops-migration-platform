---
name: nkda-tddsn-test-implementation
description: Implements the approved target behavioural tests, test support, and test refactors required by the rebuild plan without adding coverage padding.
---

# Skill: NKDA TDD Safety Net Test Implementation

## Responsibilities

- implement the approved target tests
- use MSTest unless the repository clearly uses a different framework
- prefer real domain objects and simple fakes
- mock only true external boundaries or nondeterminism
- do not test private methods directly
- do not add coverage padding
- create or update test classes, fakes, builders, and test contexts where justified
- rename unclear tests when part of the approved rebuild plan
- delete or replace weak tests only when the target suite covers the behaviour they claimed to protect

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- relevant production and test files

## Implementation Rules

- tests drive production changes
- follow repository testing rules and MSTest conventions
- prefer clear behavioural tests over broad verification tests
- prefer fakes, builders, and test contexts over excessive mocks
- do not expand scope beyond the approved target suite
- do not claim completion; verification is a separate phase
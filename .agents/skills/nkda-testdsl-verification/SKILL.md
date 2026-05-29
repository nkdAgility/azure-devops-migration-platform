---
name: nkda-testdsl-verification
description: Use when conversion and refactor are done and parity, artefact removal, and migration completion evidence must be recorded.
---

# Skill: NKDA Test DSL Verification

## Responsibilities

- verify behavioural parity mapping
- verify converted tests are code-first MSTest and non-vacuous
- score intent-derived tests with the test-validity model and confirm each is `USEFUL` or `HIGH VALUE` (>= 16/25)
- verify newly converted tests for the feature family are passing
- after converted tests are passing, run the full repository test suite and record the result
- remove migrated Reqnroll artefacts for the converted family when verification is `PASS` (`.feature`, generated `.feature.cs`, and legacy `*Steps.cs` files tied to that family)
- verify Reqnroll artefact removal status
- verify completion conditions in `contracts.md`
- produce `.output/nkda-testdsl/<feature-family>/06-verification.md`

## Required Test Execution Order

1. Run the converted/affected feature-family tests first.
2. Score any intent-derived tests with test-validity dimensions and reject `WASTE` or `LOW VALUE` tests.
3. If and only if tests are green and validity gate passes, run the full repository test suite.
4. Record commands, outcomes, and validity scores in `06-verification.md`.

## Required Verdict

- `PASS` when parity and completion conditions are met, intent-derived tests are `USEFUL`/`HIGH VALUE`, and the full repository test suite passes after converted tests are green
- `BLOCKED` when any completion prerequisite is missing
- `FAIL` when parity checks run and defects or regressions are found

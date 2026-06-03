---
name: nkda-testdsl-verification
description: Use when conversion and refactor are done and parity, artefact removal, and migration completion evidence must be recorded.
---

# Skill: NKDA Test DSL Verification

## Responsibilities

- read the wiring state recorded by assessment and apply the matching verification mode
- verify behavioural parity mapping for `wired` families
- for `miswired`/`unwired` families, verify intent coverage and that every assertion is confirmed against observed production behaviour (no parity baseline existed and none may be claimed)
- verify converted tests are code-first MSTest and non-vacuous
- score intent-derived tests with the test-validity model and confirm each is `USEFUL` or `HIGH VALUE` (>= 16/25)
- verify `00-scenario-test-inventory.md` has no `unmatched` scenarios for the converted family
- verify no duplicate coverage was created: every `pre-existing` scenario maps to the prior test (not a new copy), and no newly built test re-asserts behaviour an existing test already covered
- verify each mapped scenario row has concrete test evidence (`path:line`)
- verify expected vs actual test tags are compliant for every mapped scenario
- verify newly converted tests for the feature family are passing
- after converted tests are passing, run the full repository test suite and record the result
- remove migrated Reqnroll artefacts when verification is `PASS`, scoped to wiring state: for `wired`, the `.feature`, generated `.feature.cs`, and legacy `*Steps.cs` tied to the family; for `miswired`, the dead non-executing `*Steps.cs` and the retired `.feature` (there is no generated `.feature.cs`); for `unwired`, the retired `.feature` only (no bindings or generated test exist)
- verify there are no orphan generated `Features\*.feature.cs` files without matching `Features\*.feature` inputs in the affected test project; remove any found and record removals
- verify Reqnroll artefact removal status for the artefacts that existed for that wiring state
- verify completion conditions in `contracts.md`
- produce `.output/nkda-testdsl/<feature-family>/06-verification.md`

## Required Test Execution Order

1. Run the converted/affected feature-family tests first.
2. Score any intent-derived tests with test-validity dimensions and reject `WASTE` or `LOW VALUE` tests.
3. Confirm scenario inventory coverage and tag compliance are complete.
4. If and only if tests are green, validity gate passes, and inventory/tag checks pass, run the full repository test suite.
5. Record commands, outcomes, validity scores, and inventory/tag verdict in `06-verification.md`.

## Required Verdict

- `PASS` (`wired`) when parity and completion conditions are met, intent-derived tests are `USEFUL`/`HIGH VALUE`, scenario inventory has no `unmatched` rows, all mapped tests are tag-compliant, and the full repository test suite passes after converted tests are green
- `PASS` (`miswired`/`unwired`) when intent coverage is complete, every assertion is confirmed against observed production behaviour, no intent-vs-behaviour conflict remains unresolved, intent-derived tests are `USEFUL`/`HIGH VALUE`, scenario inventory has no `unmatched` rows, all tests are tag-compliant, the new tests are registered and executing, and the full repository test suite passes after they are green
- `BLOCKED` when any completion prerequisite is missing
- `FAIL` when parity checks run and defects or regressions are found

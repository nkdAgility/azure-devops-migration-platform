---
name: nkda-testdsl-autonomous
description: Use when the user wants a single autonomous entrypoint that migrates one or more Reqnroll feature families to internal DSL without manually running each phase.
---

# Skill: NKDA Test DSL Autonomous

## Entry Point

Invoke with:

`nkda-testdsl-autonomous {feature}`

Where `{feature}` is one of:

- feature family name
- feature folder
- feature file path
- step file path

## Required Behavior

1. Build a deterministic worklist of `.feature` files in scope.
2. For each `.feature` file, resolve its feature family and determine whether it is already adapted to DSL before attempting conversion.
3. For each not-yet-adapted family, classify its wiring state (`wired`, `miswired`, `unwired`). A `miswired` or `unwired` feature is NOT skipped: it is a valid candidate, and the run must build the tests that should have existed, with assertions confirmed against observed production behaviour.
4. Convert every family that is not already adapted and can be converted safely, applying the wiring-state conversion mode.
5. For every scenario, check the existing test corpus for equivalent coverage before building anything; map to the existing test (or extend it) instead of creating a duplicate, and build a new test only when no equivalent exists.
6. For scenarios with missing step implementations and no pre-existing coverage, generate intent-derived tests instead of skipping them.
7. Apply a test-validity gate to intent-derived tests and require `USEFUL` or `HIGH VALUE` classification.
8. Maintain a running scenario inventory per feature family at `.output/nkda-testdsl/<feature-family>/00-scenario-test-inventory.md`.
9. Require each scenario row to record mapped test(s), mapping status, expected tags, actual tags, and tag compliance.
10. Produce a final status summary that includes both totals and per-file outcomes.

Run this sequence for each selected feature family that is not already adapted:

1. `nkda-testdsl-feature-assessment`
2. `nkda-testdsl-dsl-design`
3. `nkda-testdsl-extraction`
4. `nkda-testdsl-feature-conversion`
5. `nkda-testdsl-refactor`
6. `nkda-testdsl-verification`
7. Remove migrated Reqnroll artefacts for that family, scoped to its wiring state, only after verification returns `PASS`: `wired` removes the `.feature`, generated `.feature.cs`, and legacy `*Steps.cs`; `miswired` removes the dead non-executing `*Steps.cs` and the retired `.feature`; `unwired` retires the `.feature` only
8. `nkda-testdsl-next-feature-selection` (after the final selected family is completed)

`nkda-testdsl-verification` must include: converted/affected tests passing first, followed by a full repository test-suite rerun.

### Already-Adapted Check

Treat a feature family as already adapted only when both conditions are true:

1. `.output/nkda-testdsl/<feature-family>/06-verification.md` exists and records a `PASS` verdict.
2. Legacy Reqnroll artefacts for that family are already removed or explicitly retained as unmigrated remainder in verification output.

If these conditions are not both met, the family is still in scope for conversion.

## Input Handling

- If `{feature}` is a feature family name, feature file path, or step file path, resolve it to one feature family and run the sequence once.
- If `{feature}` is a folder, resolve every `.feature` file under that folder, map each file to its feature family, run already-adapted checks, then run the sequence once per family until the folder scope is exhausted.
- If `{feature}` is missing, run `nkda-testdsl-next-feature-selection` to select one family, then continue.

## Required Final Status Output

At the end of every autonomous run, output:

1. Totals: `.feature` files discovered, already adapted, converted in this run, built-from-intent in this run, skipped, blocked/failed; plus a wiring breakdown (`wired`, `miswired`, `unwired`).
2. Per-file status for every discovered `.feature` file with:
   - file path
   - resolved feature family
   - wiring state (`wired`, `miswired`, `unwired`)
   - status (`already-adapted`, `converted`, `built-from-intent`, `skipped`, `blocked`, or `failed`)
   - concise reason
   - coverage-origin counts (`pre-existing`, `partial-existing`, `to-build`)
   - intent-derived tests created count
   - intent-derived tests passing validity gate count
   - scenario inventory counts (`matched`, `partial`, `unmatched`)
   - tag compliance counts (`compliant`, `non-compliant`, `unknown`)
   - converted artefact reference when applicable (`04-conversion-summary.md` and `06-verification.md`)

## Stopping Rules

Stop after all selected families are evaluated and every convertible family has been attempted.

For folder input:

- process families in deterministic path order
- skip duplicate family resolutions
- continue after per-family failures so remaining families are still attempted
- stop when every resolved family has a terminal status (`already-adapted`, `converted`, `built-from-intent`, `skipped`, `blocked`, or `failed`)

Stop early if:

- the folder/file scope cannot be enumerated or resolved
- a required shared input is missing for the entire run

A family being `miswired` or `unwired` is never by itself a reason to skip, block, or fail it; those families are built from intent.

For per-family failures (including unmatched steps where intent cannot be inferred safely, parity gaps for `wired` families, assertions that cannot be confirmed against observed production behaviour for `miswired`/`unwired` families, unresolved intent-vs-behaviour conflicts, unplanned production behaviour changes, unresolved failures, intent-derived tests failing validity gate, scenario inventory rows that remain `unmatched`, non-compliant test tags, or verification not returning `PASS`), mark the affected family `blocked` or `failed` and continue with remaining families.

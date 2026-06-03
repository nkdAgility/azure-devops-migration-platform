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

### Run-level setup (once per invocation)

1. Enumerate all `.feature` files in scope in deterministic path order. Record the list — this is the worklist. Do not preload file contents.
2. Ensure typed DSL foundation exists. If `tests/DevOpsMigrationPlatform.Testing` is missing, bootstrap it before the first family conversion. This is never a blocker.

### Per-file loop (repeat for every file in the worklist, one at a time)

For each `.feature` file in the worklist, execute all of the following steps to completion before moving to the next file:

1. Resolve the file to its feature family.
2. Run the already-adapted check. If already adapted, record status and skip to the next file.
3. Classify the wiring state (`wired`, `miswired`, `unwired`). `miswired` and `unwired` are valid candidates — do not skip them.
4. `nkda-testdsl-feature-assessment` — produce `01-feature-assessment.md`.
5. `nkda-testdsl-dsl-design` — produce `02-dsl-design.md`.
6. Purge orphaned generated `Features\*.feature.cs` files in the target test project.
7. `nkda-testdsl-extraction` — bootstrap DSL project if missing; produce `03-extraction-summary.md`.
8. `nkda-testdsl-feature-conversion` — produce `04-conversion-summary.md`.
   - For each scenario: build and run its mapped test. If the test passes, retire the scenario from the `.feature` file immediately. If it fails, retain the scenario.
   - Check the existing test corpus before building any test; map to pre-existing, extend partial-existing, build only `to-build`.
   - For missing-step scenarios with no pre-existing coverage, generate intent-derived tests.
9. `nkda-testdsl-refactor` — produce `05-refactor-summary.md`.
10. `nkda-testdsl-verification` — produce `06-verification.md`.
    - Run converted/affected tests first.
    - Verify every retired scenario has a mapped passing test with `path:line` evidence.
    - If all scenarios are retired and tests are green, run the full repository test suite.
    - If verification returns `PASS`: delete the `.feature` file, any generated `.feature.cs`, and legacy `*Steps.cs` scoped to wiring state.
    - If verification returns `BLOCKED` or `FAIL`: retain the `.feature` file (with only unconverted scenarios remaining), record the reason, and continue to the next file.
11. Record terminal status for this file (`already-adapted`, `converted`, `built-from-intent`, `blocked`, `failed`).
12. **Move to the next file in the worklist.**

### Run-level teardown (once, after all files processed)

- Run `nkda-testdsl-next-feature-selection`.
- Produce a final status summary with totals and per-file outcomes.

### Already-Adapted Check

Treat a feature family as already adapted only when both conditions are true:

1. `.output/nkda-testdsl/<feature-family>/06-verification.md` exists and records a `PASS` verdict.
2. Legacy Reqnroll artefacts for that family are already removed or explicitly retained as unmigrated remainder in verification output.

If these conditions are not both met, the family is still in scope for conversion.

## Input Handling

- If `{feature}` is a feature family name, feature file path, or step file path, resolve it to one feature family and run the sequence once.
- If `{feature}` is a folder, enumerate `.feature` files in deterministic path order and process them iteratively, one file at a time. For each current file: resolve family, run already-adapted check, run the full sequence through refactor and verification, record status, then move to the next file. Do not preload the full folder into memory before execution.
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

Stop after all discovered files are evaluated and every convertible file/family has been attempted.

For folder input:

- process `.feature` files in deterministic path order, one file at a time
- skip duplicate family resolutions
- continue after per-family failures so remaining families are still attempted
- stop when every discovered file has a terminal status (`already-adapted`, `converted`, `built-from-intent`, `skipped`, `blocked`, or `failed`)

Stop early if:

- the folder/file scope cannot be enumerated or resolved
- a required shared input is missing for the entire run

Missing typed DSL foundation is not a valid "required shared input missing" reason; the run must bootstrap it and continue.

A family being `miswired` or `unwired` is never by itself a reason to skip, block, or fail it; those families are built from intent.

For per-family failures (including unmatched steps where intent cannot be inferred safely, parity gaps for `wired` families, assertions that cannot be confirmed against observed production behaviour for `miswired`/`unwired` families, unresolved intent-vs-behaviour conflicts, unplanned production behaviour changes, unresolved failures, intent-derived tests failing validity gate, scenario inventory rows that remain `unmatched`, non-compliant test tags, or verification not returning `PASS`), mark the affected family `blocked` or `failed` and continue with remaining families.

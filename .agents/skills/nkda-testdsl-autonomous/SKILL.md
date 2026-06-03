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

## ONE FILE PER INVOCATION — CRITICAL

**This skill processes exactly one `.feature` file per invocation and then stops.**

When `{feature}` is a folder, the skill selects the **next unprocessed file** (first in deterministic path order that does not yet have a `PASS` verdict in `.output/nkda-testdsl/`), processes it fully to completion including commit, then stops. Re-invoke to process the next file.

Do NOT loop internally over multiple files. Do NOT preload or batch-process multiple files. One invocation = one file = one commit = done.

## Required Behavior (single file)

### Step 1 — Select target file

- If `{feature}` is a specific file or family name: that is the target.
- If `{feature}` is a folder: scan the folder for `.feature` files in deterministic path order. Select the first file that does NOT have `.output/nkda-testdsl/<family>/06-verification.md` with a `PASS` verdict. If all files are already PASS, report completion and stop.
- If `{feature}` is missing: run `nkda-testdsl-next-feature-selection` to select one family, then continue.

Output the selected file path and family name before proceeding.

### Step 2 — Bootstrap DSL foundation (if missing)

If `tests/DevOpsMigrationPlatform.Testing` does not exist, bootstrap it now. This is never a blocker — create it and continue.

### Step 3 — Already-adapted check

Apply the already-adapted check (see below). If the family is already adapted, report status and stop. Do not process further.

### Step 4 — Classify wiring state

Classify as `wired`, `miswired`, or `unwired`. `miswired` and `unwired` are valid candidates — never skip them.

### Step 5 — Assessment

Run `nkda-testdsl-feature-assessment` → produce `01-feature-assessment.md`.

### Step 6 — DSL design

Run `nkda-testdsl-dsl-design` → produce `02-dsl-design.md`.

### Step 7 — Extraction

Purge orphaned generated `Features\*.feature.cs` files in the target test project.

Run `nkda-testdsl-extraction` → produce `03-extraction-summary.md`.

### Step 8 — Conversion

Run `nkda-testdsl-feature-conversion` → produce `04-conversion-summary.md`.

For each scenario:
- Build and run its mapped test.
- If the test passes: retire the scenario from the `.feature` file immediately (remove that scenario block).
- If the test fails: retain the scenario in the `.feature` file.
- Check the existing test corpus before building any test; map to pre-existing, extend partial-existing, build only `to-build`.
- For missing-step scenarios with no pre-existing coverage, generate intent-derived tests.

### Step 9 — Refactor

Run `nkda-testdsl-refactor` → produce `05-refactor-summary.md`.

### Step 10 — Verification

Run `nkda-testdsl-verification` → produce `06-verification.md`.

- Run converted/affected tests first.
- Verify every retired scenario has a mapped passing test with `path:line` evidence.
- If all scenarios are retired and tests are green, run the full repository test suite.
- If verification returns `PASS`:
  - Delete the `.feature` file.
  - Delete any generated `.feature.cs` and legacy `*Steps.cs` scoped to wiring state.
  - **Commit all changes** with message: `migrate: <family-name> feature → DSL`.
- If verification returns `BLOCKED` or `FAIL`:
  - Retain the `.feature` file (with only unconverted scenarios remaining).
  - Record the reason in `06-verification.md`.
  - **Append every retained scenario as an entry in `analysis/dsl-gaps-detected.md`** with the gap-type, family, file path, scenario title, wiring state, and specific engineering detail. Do not leave a scenario retained without a gap entry.
  - **Commit partial progress** (retired scenarios removed, new tests added, gap entries written) with message: `migrate(partial): <family-name> <N> scenarios retired`.

### Step 11 — Report and stop

Output the terminal status for this file: `already-adapted`, `converted`, `built-from-intent`, `blocked`, or `failed`.

**Stop. Do not proceed to the next file.**

### Already-Adapted Check

Treat a feature family as already adapted only when both conditions are true:

1. `.output/nkda-testdsl/<feature-family>/06-verification.md` exists and records a `PASS` verdict.
2. Legacy Reqnroll artefacts for that family are already removed or explicitly retained as unmigrated remainder in verification output.

If these conditions are not both met, the family is still in scope for conversion.

## Required Final Status Output

At the end of every invocation, output:

- Selected file path and resolved feature family
- Wiring state (`wired`, `miswired`, `unwired`)
- Terminal status: `already-adapted`, `converted`, `built-from-intent`, `blocked`, or `failed`
- Concise reason
- Scenario counts: retired, retained, total
- Commit reference (SHA or message)
- If folder input: how many files remain unprocessed in the folder

## Stopping Rules

**Always stop after processing one file.** Do not loop.

Stop without processing if:

- The scope cannot be enumerated or resolved.
- All files in the folder scope already have a `PASS` verdict — report "all done" and stop.
- Missing typed DSL foundation is NOT a stop reason — bootstrap it and continue.

Per-family failures (`blocked`, `failed`) do not prevent the invocation from completing — they are reported as the terminal status and the invocation ends normally.

`miswired` and `unwired` wiring states are never a reason to skip or block a file.

Failure reasons that result in `blocked` or `failed` status include: unmatched steps where intent cannot be inferred safely, parity gaps for `wired` families, assertions that cannot be confirmed against observed production behaviour for `miswired`/`unwired` families, unresolved intent-vs-behaviour conflicts, unplanned production behaviour changes, unresolved test failures, intent-derived tests failing validity gate, scenario inventory rows that remain `unmatched`, non-compliant test tags, or verification not returning `PASS`.

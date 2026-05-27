---
name: nkda-testdsl-autonomous
description: Use when the user wants a single autonomous entrypoint that migrates one Reqnroll feature family to internal DSL without manually running each phase.
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

Run this sequence for exactly one feature family:

1. `nkda-testdsl-feature-assessment`
2. `nkda-testdsl-dsl-design`
3. `nkda-testdsl-extraction`
4. `nkda-testdsl-feature-conversion`
5. `nkda-testdsl-refactor`
6. `nkda-testdsl-verification`
7. `nkda-testdsl-next-feature-selection`

## Input Handling

- If `{feature}` is provided, use it directly.
- If `{feature}` is missing, run `nkda-testdsl-next-feature-selection` to select one, then continue.

## Stopping Rules

Stop after one feature family.

Stop early if:

- feature and step files cannot be matched
- behaviour parity cannot be established
- conversion requires unplanned production behaviour changes
- failures cannot be resolved in feature-family scope


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

Run this sequence for each selected feature family:

1. `nkda-testdsl-feature-assessment`
2. `nkda-testdsl-dsl-design`
3. `nkda-testdsl-extraction`
4. `nkda-testdsl-feature-conversion`
5. `nkda-testdsl-refactor`
6. `nkda-testdsl-verification`
7. `nkda-testdsl-next-feature-selection` (after the final selected family is completed)

## Input Handling

- If `{feature}` is a feature family name, feature file path, or step file path, resolve it to one feature family and run the sequence once.
- If `{feature}` is a folder, resolve every `.feature` file under that folder, map each file to its feature family, then run the sequence once per family until the folder scope is exhausted.
- If `{feature}` is missing, run `nkda-testdsl-next-feature-selection` to select one family, then continue.

## Stopping Rules

Stop after all selected families complete.

For folder input:

- process families in deterministic path order
- skip duplicate family resolutions
- stop when every resolved family has completed or when an early-stop condition is met

Stop early if:

- feature and step files cannot be matched
- behaviour parity cannot be established
- conversion requires unplanned production behaviour changes
- failures cannot be resolved in feature-family scope

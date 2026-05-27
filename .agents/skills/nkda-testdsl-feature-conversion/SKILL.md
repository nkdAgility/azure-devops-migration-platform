---
name: nkda-testdsl-feature-conversion
description: Use when one feature family is ready to be converted from Reqnroll to code-first MSTest tests using the extracted internal DSL.
---

# Skill: NKDA Test DSL Feature Conversion

## Responsibilities

- consume `01-feature-assessment.md` and `02-dsl-design.md`
- create code-first MSTest tests that preserve behaviour
- remove `.feature` project inclusion after equivalent coverage exists
- remove obsolete step/context files only after parity is established
- produce `.output/nkda-testdsl/<feature-family>/04-conversion-summary.md`

## Stop Conditions

Stop and report if:

- behaviour parity cannot be shown
- conversion requires unplanned production behaviour changes
- failures cannot be resolved in scope


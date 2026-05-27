---
name: nkda-testdsl-verification
description: Use when conversion and refactor are done and parity, artefact removal, and migration completion evidence must be recorded.
---

# Skill: NKDA Test DSL Verification

## Responsibilities

- verify behavioural parity mapping
- verify converted tests are code-first MSTest and non-vacuous
- verify Reqnroll artefact removal status
- verify completion conditions in `contracts.md`
- produce `.output/nkda-testdsl/<feature-family>/06-verification.md`

## Required Verdict

- `PASS` when parity and completion conditions are met
- `BLOCKED` when any completion prerequisite is missing
- `FAIL` when parity checks run and defects or regressions are found

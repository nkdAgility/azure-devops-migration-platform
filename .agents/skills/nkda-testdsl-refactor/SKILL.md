---
name: nkda-testdsl-refactor
description: Use when one conversion slice is complete and the internal DSL needs cleanup for reuse before the next feature family.
---

# Skill: NKDA Test DSL Refactor

## Responsibilities

- remove duplication from extracted DSL
- improve naming and ownership boundaries
- keep builders, runners, and assertions separated
- avoid speculative abstraction for unmigrated families
- produce `.output/nkda-testdsl/<feature-family>/05-refactor-summary.md`

## Rules

- preserve passing converted tests
- no production behaviour changes


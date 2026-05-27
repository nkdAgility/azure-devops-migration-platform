---
name: nkda-testdsl-extraction
description: Use when an approved DSL design exists and reusable test infrastructure must be extracted into the shared testing DSL project.
---

# Skill: NKDA Test DSL Extraction

## Responsibilities

- create or update reusable DSL types under `tests/DevOpsMigrationPlatform.Testing`
- extract only concepts needed by the selected family
- separate scenarios, builders, runners, results, assertions, and fixtures
- keep behaviour unchanged
- produce `.output/nkda-testdsl/<feature-family>/03-extraction-summary.md`

## Rules

- no speculative abstractions
- no production behaviour changes
- no Reqnroll APIs


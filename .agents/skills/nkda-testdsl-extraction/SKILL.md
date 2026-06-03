---
name: nkda-testdsl-extraction
description: Use when an approved DSL design exists and reusable test infrastructure must be extracted into the shared testing DSL project.
---

# Skill: NKDA Test DSL Extraction

## Responsibilities

- bootstrap `tests/DevOpsMigrationPlatform.Testing` when missing (project file + initial folders + minimal compile-safe seed types)
- create or update reusable DSL types under `tests/DevOpsMigrationPlatform.Testing`
- extract only concepts needed by the selected family
- separate scenarios, builders, runners, results, assertions, and fixtures
- keep behaviour unchanged
- keep `00-scenario-test-inventory.md` in sync with any renamed planned test surfaces
- produce `.output/nkda-testdsl/<feature-family>/03-extraction-summary.md`

## Rules

- no speculative abstractions
- no production behaviour changes
- no Reqnroll APIs

## Bootstrap Rule

- Missing typed DSL foundation is **not** a conversion blocker.
- If `tests/DevOpsMigrationPlatform.Testing` does not exist, create it during extraction and continue the run.
- The bootstrap must be minimal and family-driven: only create reusable primitives required for the selected family plus skeletal folder structure (`Builders`, `Runners`, `Assertions`, `Fixtures`, `Scenarios`).
- Record bootstrap actions in `03-extraction-summary.md` with `path:line` references.

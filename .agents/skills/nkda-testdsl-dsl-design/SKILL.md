---
name: nkda-testdsl-dsl-design
description: Use when assessment output is complete and a typed internal DSL surface must be designed for one feature-family migration.
---

# Skill: NKDA Test DSL Design

## Responsibilities

- consume `01-feature-assessment.md`
- consume and update `00-scenario-test-inventory.md`
- define typed scenario entry points and builders
- define runners, fixtures, results, and assertion extensions
- define target MSTest examples
- define per-scenario target test names and planned test tags in the running inventory
- define deletion plan for legacy Reqnroll artefacts
- produce `.output/nkda-testdsl/<feature-family>/02-dsl-design.md`
- do not modify code

## Design Rules

- C# only, MSTest-compatible
- no Reqnroll dependency
- no string-matched step APIs
- behaviour-first naming
- group DSL surface and target tests by business capability in the system under test, not by migration phase buckets

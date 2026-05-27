---
name: nkda-testdsl-next-feature-selection
description: Use when one feature-family migration is complete and the next family must be chosen based on DSL reuse and behavioural risk.
---

# Skill: NKDA Test DSL Next Feature Selection

## Responsibilities

- inspect remaining feature families
- score candidates for DSL reuse, risk reduction, and observability
- recommend exactly one next family
- produce `.output/nkda-testdsl/<feature-family>/07-next-feature-recommendation.md`

## Scoring

Score each candidate on:

1. DSL reuse potential
2. behaviour risk reduction
3. removal of Reqnroll surface area
4. test determinism feasibility


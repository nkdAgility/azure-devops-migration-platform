---
name: nkda-testdsl-feature-assessment
description: Use when selecting one Reqnroll feature family to map behaviour, hidden step operations, context state, and migration risks before DSL design.
---

# Skill: NKDA Test DSL Feature Assessment

## Responsibilities

- inspect selected `.feature` file(s), `*Steps.cs`, and related context files
- map scenarios to observable behaviours
- map step methods to hidden operations
- identify scenarios where step implementations are missing and extract test intent from feature wording plus available context/state mappings
- identify assertion quality and vacuous checks
- propose required DSL concepts
- create `.output/nkda-testdsl/<feature-family>/00-scenario-test-inventory.md` with one row per scenario
- produce `.output/nkda-testdsl/<feature-family>/01-feature-assessment.md`
- do not modify code

## Scenario Inventory Contract

Maintain `00-scenario-test-inventory.md` as the authoritative running inventory for the feature family.

Each scenario row must include:

1. feature file path
2. scenario name
3. planned or actual DSL test name(s)
4. mapping status (`matched`, `partial`, `unmatched`)
5. expected test tags (from existing repository test-tag conventions)
6. actual test tags (or `pending` during assessment/design)
7. tag compliance (`compliant`, `non-compliant`, `unknown`)
8. evidence (`path:line` for mapped tests when available)

## Required Output

Use these sections:

1. Scope
2. Behaviour Inventory
3. Step Implementation Map
4. Context State Map
5. Assertion Quality
6. Proposed DSL Concepts
7. Missing-Step Intent Backlog
8. Migration Recommendation
9. Scenario-to-Test Inventory Snapshot

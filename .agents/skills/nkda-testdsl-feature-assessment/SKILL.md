---
name: nkda-testdsl-feature-assessment
description: Use when selecting one Reqnroll feature family to map behaviour, hidden step operations, context state, and migration risks before DSL design.
---

# Skill: NKDA Test DSL Feature Assessment

## Responsibilities

- inspect selected `.feature` file(s), `*Steps.cs`, and related context files
- classify the wiring state of the feature family (see Wiring Classification) and record it
- for every scenario, search the existing test corpus for equivalent coverage before proposing a new test (see Pre-Existing Coverage Check)
- map scenarios to observable behaviours
- map step methods to hidden operations
- identify scenarios where step implementations are missing and extract test intent from feature wording plus available context/state mappings
- identify assertion quality and vacuous checks
- propose required DSL concepts
- create `.output/nkda-testdsl/<feature-family>/00-scenario-test-inventory.md` with one row per scenario
- produce `.output/nkda-testdsl/<feature-family>/01-feature-assessment.md`
- do not modify code

## Wiring Classification

Before mapping behaviour, determine and record the family's wiring state. A feature only executes when it is listed in an `ExternalFeatureFiles` item in a test `.csproj` (the build target copies it into that project's `Features/` directory and Reqnroll generates the adjacent `.feature.cs`). Classify as one of:

1. `wired` - listed in `ExternalFeatureFiles`, generated `.feature.cs` present, and `*Steps.cs` bindings exist. Behaviour is currently executing.
2. `miswired` - `*Steps.cs` bindings exist for the feature but it is NOT listed in `ExternalFeatureFiles`, so no `.feature.cs` is generated and the bindings do not execute. Bindings may be stale or partially applicable.
3. `unwired` - `.feature` exists on disk with no `ExternalFeatureFiles` entry and no `*Steps.cs` bindings. No executing baseline exists.

Record the verdict, the evidence (`csproj path:line` for any `ExternalFeatureFiles` entry; `*Steps.cs` paths for any bindings), and a per-binding applicability note in `01-feature-assessment.md`.

For `miswired` and `unwired` families there is no executing baseline. Each scenario is a valid conversion candidate - do not skip them - but a scenario is only built as a new intent-derived test after the Pre-Existing Coverage Check confirms no equivalent test already exists. For `miswired`, reuse any sound existing binding logic as implementation reference, but treat the scenario as intent-derived because the binding is not proven to execute.

## Pre-Existing Coverage Check

Building "the tests that should have existed" must never create a duplicate of a test that already exists. For every scenario - in all wiring states, but especially `miswired`/`unwired` - search the existing test corpus (code-first MSTest tests, DSL `Scenarios/`, and any other already-converted tests) for a test that already asserts the same observable behaviour. Search by behaviour and assertion shape, not just by name, since equivalent coverage may live under a different name or capability grouping.

Record one coverage origin per scenario:

1. `pre-existing` - an equivalent test already exists. Map the scenario to it with `path:line` evidence and set mapping status `matched`. Do not plan a new test. Note any gap (missing tag, weaker assertion) as a follow-up to adjust the existing test rather than duplicate it.
2. `to-build` - no equivalent exists. Plan a new intent-derived test.
3. `partial-existing` - a related test exists but does not fully cover the scenario. Plan to extend the existing test rather than create a parallel duplicate; set mapping status `partial` until coverage is complete.

Record the coverage origin and evidence in both `01-feature-assessment.md` and the scenario inventory.

## Scenario Inventory Contract

Maintain `00-scenario-test-inventory.md` as the authoritative running inventory for the feature family.

Each scenario row must include:

0. wiring state of the owning family (`wired`, `miswired`, `unwired`)
0a. coverage origin (`pre-existing`, `partial-existing`, `to-build`)
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
2. Wiring Classification
3. Pre-Existing Coverage Map
4. Behaviour Inventory
5. Step Implementation Map
6. Context State Map
7. Assertion Quality
8. Proposed DSL Concepts
9. Missing-Step Intent Backlog
10. Migration Recommendation
11. Scenario-to-Test Inventory Snapshot

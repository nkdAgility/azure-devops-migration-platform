---
name: nkda-testdsl-feature-conversion
description: Use when one feature family is ready to be converted from Reqnroll to code-first MSTest tests using the extracted internal DSL.
---

# Skill: NKDA Test DSL Feature Conversion

## Responsibilities

- consume `01-feature-assessment.md` and `02-dsl-design.md`
- consume and update `00-scenario-test-inventory.md`
- create code-first MSTest tests that preserve behaviour
- place converted tests into business-focused groupings that mirror the system-under-test capability boundaries
- for missing-step scenarios, generate intent-derived code-first tests instead of skipping those scenarios
- map every converted scenario to concrete test method(s) with `path:line` evidence
- apply required test tags to each converted test using existing repository tag conventions
- record expected tags, actual tags, and compliance per scenario row in the running inventory
- branch on the wiring state recorded by assessment (see Wiring-State Conversion Modes)
- produce `.output/nkda-testdsl/<feature-family>/04-conversion-summary.md`

## Wiring-State Conversion Modes

### `wired`

- preserve behaviour parity against the currently executing tests
- remove `.feature` project inclusion (`ExternalFeatureFiles` entry) after equivalent coverage exists
- remove obsolete step/context files only after parity is established

### `miswired` and `unwired` (build the tests that should have existed)

There is no executing baseline, so behaviour parity against prior tests is not available and must not be claimed. Instead:

- honour the coverage origin recorded by assessment before creating anything: for `pre-existing` scenarios, map to the existing test and create no duplicate; for `partial-existing` scenarios, extend the existing test rather than add a parallel one; build a new test only for `to-build` scenarios
- if a candidate test name or behaviour collides with an existing test during conversion, re-run the Pre-Existing Coverage Check and reconcile instead of creating a duplicate
- treat every `to-build` scenario as intent-derived and build the code-first MSTest test that should have existed
- bind every assertion to observed production behaviour exercised through the DSL runners/fakes against the real system under test; do not assert from feature prose alone
- where feature intent conflicts with actual production behaviour, record the conflict as a finding in `04-conversion-summary.md` and stop; do not silently encode either side
- for `miswired`, you may reuse sound logic from the existing non-executing `*Steps.cs` as implementation reference, then delete those dead bindings once equivalent coverage exists
- for `unwired`, there are no legacy bindings to remove
- in both cases, register the new tests so they execute, and record that no parity baseline existed (intent coverage + behaviour-confirmed assertions replace parity)

## Stop Conditions

Stop and report if:

- behaviour parity cannot be shown for a `wired` family
- for a `miswired`/`unwired` family, an assertion cannot be confirmed against observed production behaviour, or feature intent conflicts with actual behaviour
- conversion requires unplanned production behaviour changes
- failures cannot be resolved in scope
- only pipeline-phase grouping (Inventory/Export/Import/Validate style) is available and business-focused grouping cannot be established
- missing-step intent cannot be inferred with enough confidence to create a deterministic behaviour test
- any scenario in `00-scenario-test-inventory.md` remains `unmatched` after conversion
- converted tests are missing required tags or have non-compliant tags

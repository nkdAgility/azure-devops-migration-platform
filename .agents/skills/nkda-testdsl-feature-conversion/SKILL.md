---
name: nkda-testdsl-feature-conversion
description: Use when one feature family is ready to be converted from Reqnroll to code-first MSTest tests using the extracted internal DSL.
---

# Skill: NKDA Test DSL Feature Conversion

## Responsibilities

- consume `01-feature-assessment.md` and `02-dsl-design.md`
- consume and update `00-scenario-test-inventory.md`
- create code-first MSTest tests that preserve behaviour
- before conversion codegen runs, remove stale generated `Features\*.feature.cs` files that have no matching `Features\*.feature` input
- place converted tests into business-focused groupings that mirror the system-under-test capability boundaries
- for missing-step scenarios, generate intent-derived code-first tests instead of skipping those scenarios
- map every converted scenario to concrete test method(s) with `path:line` evidence
- apply required test tags to each converted test using existing repository tag conventions
- record expected tags, actual tags, and compliance per scenario row in the running inventory
- retire each converted scenario from the source `.feature` file only after its mapped code-first test passes
- never delete an entire `.feature` file in conversion; full file retirement is deferred to verification `PASS`
- branch on the wiring state recorded by assessment (see Wiring-State Conversion Modes)
- produce `.output/nkda-testdsl/<feature-family>/04-conversion-summary.md`

## Wiring-State Conversion Modes

### `wired`

- preserve behaviour parity against the currently executing tests
- in the target test project, delete orphaned generated `*.feature.cs` files whose paired `.feature` file is already retired, so Reqnroll/MSBuild cannot fail on missing inputs
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

## Scenario Retirement Gate

- A scenario is retired from the `.feature` file when its mapped DSL test is **passing**.
- A scenario whose test is failing must have its production code fixed until the test passes — only then is it retired.
- Record, per scenario row in `00-scenario-test-inventory.md`, whether it is retained or retired, with test evidence (`path:line`).
- All scenarios must be retired by end of conversion. If all scenarios are retired, mark the `.feature` file as eligible for deletion; actual file deletion occurs only in verification after overall `PASS`.

## Completion Standard

Every scenario in the `.feature` file MUST result in a passing DSL test. There is no other acceptable outcome.

Per the project guardrails (`definition-of-done.md`, `testing-rules.md`):
- `[Ignore]` is banned. Never add it.
- `Assert.Inconclusive` is banned. Never add it.
- A failing test is not acceptable output — fix the production code until the test passes.
- Retaining a scenario in the `.feature` file because the test is hard to write is not acceptable.

**If production code is missing: implement it** — following all guardrails (coding-standards.md, architecture-boundaries.md, DI wiring rules).
**If a seam or interface is missing: create it** — following the canonical seam patterns in the guardrails.
**If a DI binding is missing: add it** — register in the correct host startup extension.
**If production behaviour conflicts with the scenario intent: fix the production behaviour** — the scenario describes the correct behaviour.
**If a subprocess cannot be intercepted: introduce the abstraction** that makes it interceptable, then wire it through DI.

## Stop Conditions

Stop and report ONLY if:

- a scenario's intent is genuinely ambiguous and cannot be resolved by reading the feature file, the DSL design, and the production code together — ask for clarification rather than guessing
- converted tests are missing required tags or have non-compliant tags (fix the tags, then continue)

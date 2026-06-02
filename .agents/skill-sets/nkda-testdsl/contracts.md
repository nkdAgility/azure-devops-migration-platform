# NKDA Test DSL Contracts

## Non-Negotiable Rules

1. Do not mechanically translate Gherkin wording into code-first helper names.
2. Do not create string-based `Given(...)`, `When(...)`, or `Then(...)` APIs.
3. Do not preserve Reqnroll `[Binding]`, `[Given]`, `[When]`, `[Then]`, or `ScenarioContext` patterns in migrated tests.
4. Keep MSTest as the execution shell for migrated behaviour tests.
5. Represent behaviour using typed domain concepts in C#.
6. Keep setup builders, execution runners, and assertions separated.
7. Preserve behaviour coverage before removing legacy artefacts.
8. Remove vacuous assertions (for example `Assert.IsTrue(true)`).
9. Do not introduce live connector dependency for migration of feature-test shape.
10. Prefer deterministic fixtures and fakes.
11. Organise converted tests and DSL entry points by business capability of the system under test, not by migration pipeline phase labels (for example Inventory, Export, Import, Validate).
12. Do not drop scenario intent when step implementations are missing; create code-first tests from intent instead of silently skipping coverage.
13. Any test created from inferred intent must meet the `USEFUL` threshold or higher (score >= 16/25) using the test-validity scoring model.
14. A `.feature` that is not wired into execution is still a valid conversion candidate. Classify each family as `wired` (registered in `ExternalFeatureFiles`, generated `.feature.cs`, executing bindings), `miswired` (bindings exist but not registered, so not executing), or `unwired` (no bindings at all). Do not skip `miswired`/`unwired` families; build the tests that should have existed.
15. For `miswired`/`unwired` families there is no executing baseline, so behaviour parity against prior tests must not be claimed. Every assertion in an intent-derived test for these families must be confirmed against observed production behaviour exercised through the DSL, not asserted from feature prose alone. Any conflict between feature intent and actual production behaviour must be recorded as a finding, not silently encoded.
16. Do not create a test that duplicates existing coverage. Before building any test, search the existing test corpus for an equivalent test by behaviour and assertion shape (not name alone) and record a coverage origin per scenario (`pre-existing`, `partial-existing`, `to-build`). Map `pre-existing` scenarios to the existing test, extend the existing test for `partial-existing`, and build a new test only for `to-build`.

## DSL Layering Contract

```text
tests/DevOpsMigrationPlatform.Testing/
  Scenarios/
  Builders/
  Fakes/
  Runners/
  Results/
  Assertions/
  Fixtures/
```

## Completion Contract for One Feature Family

1. Equivalent code-first MSTest tests exist.
2. For `wired` families, behaviour parity is documented. For `miswired`/`unwired` families, intent coverage is documented and every assertion is confirmed against observed production behaviour (no parity baseline existed).
3. Missing-step scenarios are converted into intent-derived tests or explicitly marked `BLOCKED` with reason when intent cannot be inferred safely.
4. Intent-derived tests are scored by test-validity dimensions and each scores >= 16/25 before `PASS`.
5. Converted/affected tests for the feature family pass, then the full repository test suite passes in a follow-up run.
6. Wiring-state artefact resolution is complete:
   - `wired`: the `.feature` entry is removed from `ExternalFeatureFiles`.
   - `miswired`: the dead non-executing `*Steps.cs` bindings are removed and the `.feature` is retired (it was never registered, so there is no `ExternalFeatureFiles` entry to remove).
   - `unwired`: the `.feature` is retired (no `ExternalFeatureFiles` entry and no bindings exist).
7. Legacy `*Steps.cs` and `*Context.cs` files are deleted or narrowed to still-unmigrated scope (none exist for `unwired`).
8. Verification output exists at `.output/nkda-testdsl/<feature-family>/06-verification.md` and records the wiring state.

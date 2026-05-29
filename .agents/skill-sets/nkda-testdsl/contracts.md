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
2. Behaviour parity is documented.
3. Missing-step scenarios are converted into intent-derived tests or explicitly marked `BLOCKED` with reason when intent cannot be inferred safely.
4. Intent-derived tests are scored by test-validity dimensions and each scores >= 16/25 before `PASS`.
5. Converted/affected tests for the feature family pass, then the full repository test suite passes in a follow-up run.
6. The `.feature` entry is removed from `ExternalFeatureFiles`.
7. Legacy `*Steps.cs` and `*Context.cs` files are deleted or narrowed to still-unmigrated scope.
8. Verification output exists at `.output/nkda-testdsl/<feature-family>/06-verification.md`.

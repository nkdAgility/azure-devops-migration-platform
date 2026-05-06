# Testing Guide

Audience: Contributors.

## Testing Model Overview

The project uses three test categories:

| Category | What it tests | Speed | External dependencies |
|---|---|---|---|
| Unit | Individual classes with mocked dependencies | Fast | None |
| Integration | Multiple components working together | Medium | None (uses Simulated connectors) |
| SystemTest | Live external systems | Slow | Requires real credentials |

## Running Tests

```bash
# All tests
dotnet test

# Exclude system tests (CI default)
dotnet test --filter "TestCategory!=SystemTest"

# System tests only
dotnet test --filter "TestCategory=SystemTest"
```

## MSTest Conventions

- All test classes must use `[TestClass]`.
- All test methods must use `[TestMethod]`.
- Use `[TestCategory("SystemTest")]` for tests requiring live infrastructure.
- No `Assert.Inconclusive()` — either implement the assertion or delete the test.
- No `[Ignore]` — remove tests that should not run.
- No vacuous assertions (`Assert.IsTrue(count >= 0)`, `Assert.IsTrue(true)`).

## Test Naming

```
{Subject}_{Scenario}_{ExpectedOutcome}

Examples:
WorkItemExportModule_WhenSourceHasRevisions_WritesRevisionFilesToPackage
SimulatedWorkItemSource_WhenSeeded_ReturnsAtLeastTwoItems
```

## Simulated Connectors

Use Simulated connectors for module and integration tests. They must:
- Return at least 2 items per `EnumerateAsync` call (a zero-item source silently makes tests vacuously pass).
- Be deterministic given a seed value.

## Module Test Requirements

Every export module `SystemTest_Simulated` must:
- Assert that the expected artefact path exists in `IArtefactStore`.
- Assert that the artefact contains non-empty content (line count > 0 or byte count > 0).

Every import module `SystemTest_Simulated` must:
- Assert that the target connector received data (e.g. `SimulatedTeamTarget.Teams.Count > 0`).
- Never use `count >= 0` as the assertion.

## Reqnroll (BDD) Tests

Feature files live in `features/`. Step definitions follow Reqnroll.MSTest conventions.

- Feature files must comply with [.agents/guardrails/acceptance-test-format.md](../.agents/guardrails/acceptance-test-format.md).
- Step definitions must be in a class annotated `[Binding]`.

## System Test Setup

System tests require environment variables:

| Variable | Purpose |
|---|---|
| `AZDEVOPS_SYSTEM_TEST_ORG` | Azure DevOps organisation name |
| `AZDEVOPS_SYSTEM_TEST_PAT` | Personal Access Token |

```powershell
$env:AZDEVOPS_SYSTEM_TEST_ORG = "your-org"
$env:AZDEVOPS_SYSTEM_TEST_PAT = "your-pat"
dotnet test --filter "TestCategory=SystemTest"
```

## Test Data

- Use the Simulated source connector for deterministic test data.
- Never rely on live data for non-system tests.
- The `Seed` property in Simulated config controls determinism.

## Assertion Standards

What every test must prove:
- For export: artefact exists at expected path AND has non-trivially non-empty content.
- For import: target connector shows evidence of received data.
- For connectors: the SDK was actually called (not just a hard-coded return).

## Further Reading

- [contributor-guide.md](contributor-guide.md) — contribution workflow
- [.agents/guardrails/testing-rules.md](../.agents/guardrails/testing-rules.md) — enforced test rules
- [module-development-guide.md](module-development-guide.md) — module test expectations
# Testing Guide

Audience: Contributors.

## Required Development Flow

All additions, bug fixes, and behaviour changes must follow RED → GREEN → REFACTOR:

- RED: write or update the smallest behavioural test first and verify it fails for the intended reason.
- GREEN: implement the minimum production change needed to make that test pass, then progressively broaden verification through the next wider relevant layers before the final full-suite run so the repository is restored to an all-green state.
- REFACTOR: improve naming, structure, and duplication only after the relevant tests are green.

GREEN is not satisfied by a slice-only pass. The targeted test proves the intended behaviour; progressively wider layers rebuild confidence without paying the slowest cost first; the fresh full-suite pass proves no regression was introduced.

Preferred widening order is fastest to slowest relevant evidence:

- Slice test
- Unit or feature test
- Simulated or integration test
- Live or system test

The final green gate is still the full test suite for the repository.

Adding production code before the intended failing test exists is not compliant with the repository workflow.

## Testing Model Overview

The project uses three test categories:

| Category | What it tests | Speed | External dependencies |
| --- | --- | --- | --- |
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

```text
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
| --- | --- |
| `AZDEVOPS_SYSTEM_TEST_ORG` | Azure DevOps organisation name |
| `AZDEVOPS_SYSTEM_TEST_PAT` | Personal Access Token |

```powershell
$env:AZDEVOPS_SYSTEM_TEST_ORG = "your-org"
$env:AZDEVOPS_SYSTEM_TEST_PAT = "your-pat"
dotnet test --filter "TestCategory=SystemTest"
```

## Debugging Simulated And Live Tests

When a `SystemTest_Simulated`, `SystemTest`, or `SystemTest_Live` test runs through `CliRunner`, every spawned CLI and agent process writes OTel file diagnostics next to that test's working folder.

Path pattern:

```text
<repo-root>/.output/workingtests/{TestMethodName}/.otel-diagnostics/
```

How it works:

- `CliRunner.TestWorkingFolder` is `.output\workingtests`.
- Each test sets `DEVOPS_MIGRATION_TEST_STORAGE=.output\workingtests\{TestMethodName}`.
- `CliRunner` maps `Telemetry__DiagnosticsPath` to `{repoRoot}/{testWorkingFolder}/{TestMethodName}/.otel-diagnostics`.

Debugging workflow:

1. Identify the exact MSTest method name.
2. Open `{TestWorkingFolder}/{TestMethodName}/.otel-diagnostics/` under the repo root.
3. Review the emitted `*-logs.log`, `*-traces.log`, and `*-metrics.log` files for the CLI, ControlPlaneHost, and MigrationAgent processes.
4. Include the relevant error entries and trace context in your failure analysis.

If you re-run the same simulated or live scenario with `--diagnostics` enabled, the CLI also records the raw control-plane JSON payloads exactly as received under:

```text
<repo-root>/.output/workingtests/{TestMethodName}/.otel-diagnostics/inbox/
```

Typical files include timestamped `bootstrap`, `telemetry`, `progress-{module}-{stage}`, and platform `diagnostics` payloads. Use these when you need to inspect the exact JSON returned by each control-plane call, not just the derived logs and traces.

These folders are local debug artefacts and are ignored by Git. If the failure happened in CI, use the captured test output unless you have reproduced it locally.

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

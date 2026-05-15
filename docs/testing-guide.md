# Testing Guide

Audience: Contributors.

This is the canonical human-facing guide for how testing works in this repository. Guardrails still constrain the rules, but contributor workflow, test selection, live-test setup, and diagnostics should be learned here first.

## Repository Tests-First Workflow

The repository delivery flow is tests-first, not just test-after:

1. Specification
2. Spec hardening
3. Test generation
4. Implementation
5. Review
6. Doc sync

Within implementation, every addition, bug fix, and behaviour change must follow RED → GREEN → REFACTOR:

- RED: write or update the smallest behavioural test first and verify it fails for the intended reason.
- GREEN: implement the minimum production change needed to make that test pass, then progressively broaden verification through the next wider relevant layers before the final full-suite run so the repository is restored to an all-green state.
- REFACTOR: improve naming, structure, and duplication only after the relevant tests are green.

GREEN is not satisfied by a slice-only pass. The targeted failing test proves intent. Wider layers rebuild confidence. The final green gate is a fresh full-suite pass for the repository.

Adding production code before the intended failing test exists is not compliant with the repository workflow.

### What Spec Hardening Means Here

After a spec is approved and before implementation proceeds, the repository expects the feature scope to be challenged for:

- architecture risk
- observability coverage
- red-team blind spots

The agent-oriented enforcement lives in [test-first-workflow.md](../.agents/20-guardrails/workflow/test-first-workflow.md). Contributors should treat that file as the exact contract and this guide as the human explanation.

## Test Hierarchy

The repository uses a four-level hierarchy. Prefer the lowest level that can prove the behaviour.

| Level | Typical marker | What it proves | Speed | External dependencies |
| --- | --- | --- | --- | --- |
| Unit | `[TestClass]` / `[TestMethod]` | Isolated logic, branching, transforms, validation | Fastest | None |
| Feature | `.feature` + `[Binding]` | Behaviour scenarios with in-memory seams | Fast | None |
| Smoke system | `[TestCategory("SystemTest_Smoke")]` | Startup wiring and process boundary health before broader system coverage | Fast-medium | None |
| Simulated system | `[TestCategory("SystemTest_Simulated")]` | End-to-end behaviour with deterministic Simulated connectors | Medium | None |
| Live system | `[TestCategory("SystemTest")]` or `[TestCategory("SystemTest_Live")]` | Real Azure DevOps or TFS behaviour | Slowest | Real credentials and environment |

### Selection Rule

Choose the first layer that can falsify the behaviour without real infrastructure:

- Unit when the logic does not need connector or process boundaries.
- Feature when behaviour matters but real I/O still does not.
- Smoke system when you need a fast startup/lifecycle guard on host-to-agent wiring without running a migration workload.
- Simulated system when you need end-to-end wiring, package output, or process boundaries.
- Live system only when lower layers cannot prove the connector or environment-specific behaviour.

If a proposed live test can be rewritten as a unit, feature, or simulated test, do that instead.

## Running Tests

```bash
# All tests
dotnet test

# Fast local pass that excludes live system tests
dotnet test --filter "TestCategory!=SystemTest"

# Smoke system tests only
dotnet test --filter "TestCategory=SystemTest_Smoke"

# Simulated system tests only
dotnet test --filter "TestCategory=SystemTest_Simulated"

# Live system tests only
dotnet test --filter "TestCategory=SystemTest"
```

Some suites may also use `SystemTest_Live` as an additional category marker. Treat those as live tests as well.

## Test Conventions

### MSTest

- All test classes use `[TestClass]`.
- All test methods use `[TestMethod]`.
- No `Assert.Inconclusive()` in committed tests.
- No `[Ignore]` in committed tests.
- No vacuous assertions such as `Assert.IsTrue(true)` or `Assert.IsTrue(count >= 0)`.

### Naming

```text
{Subject}_{Scenario}_{ExpectedOutcome}

Examples:
WorkItemExportModule_WhenSourceHasRevisions_WritesRevisionFilesToPackage
SimulatedWorkItemSource_WhenSeeded_ReturnsAtLeastTwoItems
```

### Reqnroll Feature Tests

Feature files live in `features/`. Step definitions follow Reqnroll.MSTest conventions.

- Feature files must comply with [acceptance-test-format.md](../.agents/20-guardrails/workflow/acceptance-test-format.md).
- Step definitions must be in a class annotated `[Binding]`.
- Use feature tests for behaviour scenarios with in-memory fakes or mocks, not for live environment coverage.

## Simulated System Tests

Use Simulated connectors for deterministic end-to-end coverage.

They must:

- return at least 2 items per `EnumerateAsync` call
- be deterministic for a given seed
- avoid live network dependencies

### Module Expectations

Every export module `SystemTest_Simulated` should prove:

- the expected artefact path exists in `IArtefactStore`
- the artefact content is non-empty

Every import module `SystemTest_Simulated` should prove:

- the target connector received data
- assertions are about observable output, not only absence of exceptions

## Live System Tests

Live system test setup, CI wiring, environment variables, and troubleshooting live in [live-system-testing-guide.md](live-system-testing-guide.md).

The important repository rule is simple: do not make committed live tests self-skip with `Assert.Inconclusive()` or `[Ignore]`. If an environment is unavailable, exclude that category from the run instead of committing a self-skipping test body.

## Diagnostics For Simulated And Live Tests

When a `SystemTest_Simulated`, `SystemTest`, or `SystemTest_Live` test runs through `CliRunner`, every spawned CLI and agent process writes OTel diagnostics under:

```text
<repo-root>/.output/workingtests/{TestMethodName}/.otel-diagnostics/
```

Use this directory to inspect:

- `*-logs.log`
- `*-traces.log`
- `*-metrics.log`

If the same run is reproduced with `--diagnostics`, the CLI also writes raw control-plane payloads under:

```text
<repo-root>/.output/workingtests/{TestMethodName}/.otel-diagnostics/inbox/
```

Typical inbox files include `bootstrap`, `telemetry`, `progress-{module}-{stage}`, and diagnostics payloads. These folders are local debug artefacts and are ignored by Git.

## Test Data

- Use the Simulated source connector for deterministic test data.
- Never rely on live data for unit, feature, or simulated tests.
- The `Seed` property in Simulated config controls determinism.

## Assertion Standards

What every test must prove:

- For export: artefact exists at the expected path and has non-trivially non-empty content.
- For import: the target connector shows evidence of received data.
- For connectors: the SDK or backing service was actually exercised, not hard-coded.

## Further Reading

- [contributor-guide.md](contributor-guide.md) — contributor entry point
- [live-system-testing-guide.md](live-system-testing-guide.md) — live environment setup and CI patterns
- [module-development-guide.md](module-development-guide.md) — module test expectations
- [.agents/20-guardrails/workflow/testing-rules.md](../.agents/20-guardrails/workflow/testing-rules.md) — enforced testing constraints
- [.agents/20-guardrails/workflow/test-first-workflow.md](../.agents/20-guardrails/workflow/test-first-workflow.md) — exact tests-first workflow contract


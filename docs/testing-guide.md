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

Tests are grouped into two parent families that reflect the runtime requirement:

- **`[TestCategory("CodeTest")]`** — runs entirely in-process. No system must be active. Covers priorities 1–3.
- **`[TestCategory("SystemTest")]`** — requires the full system to be active. Covers priorities 4–6.

Every test carries **both** the specific category tag and its parent family tag.

| Priority | Parent | Specific marker | Intent & makeup | Speed budget |
| --- | --- | --- | --- | --- |
| 1 | `CodeTest` | `[TestCategory("UnitTests")]` | Single class in isolation. All deps mocked. No I/O, no real infrastructure. | < 50 ms |
| 2 | `CodeTest` | `[TestCategory("DomainTests")]` | Business behaviour via the internal DSL. Real domain objects, no connectors or infrastructure. | < 500 ms |
| 3 | `CodeTest` | `[TestCategory("IntegrationTests")]` | Real infrastructure components (e.g. retry policies, HTTP clients, serialisers) wired together in-process. No external network or connector. | < 30 s |
| 4 | `SystemTest` | `[TestCategory("SystemTest_Smoke")]` | Critical-path subset of system tests run on every PR. Operator-designated only. | < 120 s |
| 5 | `SystemTest` | `[TestCategory("SystemTest_Simulated")]` | End-to-end with the `Simulated` connector. No network. | < 60 s |
| 6 | `SystemTest` | `[TestCategory("SystemTest_Live")]` | Requires live ADO/TFS. Environment-gated. | < 300 s |

Speed budget is not a classifier: a test's category is determined solely by its intent and makeup. A test that exceeds its category's speed budget must be fixed (e.g. inject a time abstraction, replace real delays with fakes), not moved to a slower category.

### Distinguishing the CodeTest categories

| Criterion | UnitTests | DomainTests | IntegrationTests |
| --- | --- | --- | --- |
| Scope | Single class/method in isolation | Business behaviour across collaborating domain objects | Real infrastructure components wired together in-process |
| Dependencies | All mocked/stubbed | Real domain objects via DSL builders/runners/assertions | Real library/framework components (e.g. Polly, HttpClient) — no external network |
| DSL usage | No `DevOpsMigrationPlatform.Testing` usage | Uses the internal DSL library (builders, runners, assertions) | No DSL |
| Arrange style | Direct `new Foo()` + mock setup | Builder pattern (`A.WorkItem().WithField(...)`) | Direct construction of real infrastructure components |
| Assert style | Assert on return value / state of one object | Assert on observable business outcome | Assert on real component behaviour (retry count, response, serialised output) |
| I/O | None | None | None (in-process only; no filesystem, no network) |
| External connectivity | None | None | None |

### Selection Rule

Push tests downward — choose the first layer that can falsify the behaviour without real infrastructure:

- Unit when the logic does not need collaborators, connectors, or process boundaries.
- Domain when business behaviour across real domain objects matters but real I/O does not — use the internal DSL.
- Integration when real infrastructure components (retry, serialisation, HTTP pipeline) must be proven in-process.
- Simulated system when you need end-to-end wiring, package output, or process boundaries.
- Live system only when lower layers cannot prove the connector or environment-specific behaviour.

If a proposed live test can be rewritten at a lower layer, do that instead. Smoke system tests are a curated subset designated by a human operator — never self-assign `SystemTest_Smoke`.

### Category tagging is mandatory

Whenever a test file is touched, every `[TestMethod]` and `[TestClass]` in it must carry both the parent family tag and the specific category tag, using only the canonical names above. The enforced gate ("Touch = Tag") is defined in [testing-rules.md](../.agents/20-guardrails/workflow/testing-rules.md).

## Running Tests

```bash
# All in-process tests (no running system required)
dotnet test --filter "TestCategory=CodeTest"

# Unit tests only
dotnet test --filter "TestCategory=UnitTests"

# Domain (internal DSL) tests only
dotnet test --filter "TestCategory=DomainTests"

# Everything except system tests
dotnet test --filter "TestCategory!=SystemTest"

# Smoke system tests only
dotnet test --filter "TestCategory=SystemTest_Smoke"

# Simulated system tests only
dotnet test --filter "TestCategory=SystemTest_Simulated"

# Live system tests only
dotnet test --filter "TestCategory=SystemTest_Live"
```

The repository build script provides equivalent gates used by the completion workflow:

```powershell
pwsh ./build.ps1 Test                    # unit gate
pwsh ./build.ps1 SystemTest_Simulated    # simulated gate
pwsh ./build.ps1 SystemTest_Live         # live gate
```

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

### Internal DSL Tests and the Reqnroll Migration

Code-first behavioural tests use the typed internal DSL and are the target style for all behaviour coverage:

```text
tests/DevOpsMigrationPlatform.Testing/<Domain>/...             ← reusable typed DSL
tests/<Project>.Tests/<Area>/<Behaviour>Tests.cs               ← code-first MSTest behavioural tests
features/<operation>/...                                       ← legacy Reqnroll feature files pending migration only
tests/<Project>.Tests/<Area>/<Feature>Steps.cs                 ← legacy Reqnroll step definitions pending migration only
```

Legacy Reqnroll is migration debt, not an editable style. If you need to change the behaviour of a legacy `.feature` file or its step definitions, migrate the whole feature family to the internal DSL first by running:

```text
nkda-testdsl-autonomous {feature}
```

The skill runs the full loop (assess → DSL design → extraction → conversion → refactor → verification) and produces code-first `DomainTests` under `tests/<Project>.Tests/<Area>/<Behaviour>Tests.cs`. After migration the legacy `.feature` and `*Steps.cs` files for that family are removed. The enforced gate ("Touch = Convert"), including its narrow carve-outs for retirement, typo fixes, and orphaned feature files, is defined in [testing-rules.md](../.agents/20-guardrails/workflow/testing-rules.md).

For families not yet migrated:

- Feature files must comply with [acceptance-test-format.md](../.agents/20-guardrails/workflow/acceptance-test-format.md).
- Step definitions live in a class annotated `[Binding]`, named `<FeatureName>Steps`, with PascalCase step methods whose attribute strings exactly match the `.feature` step text.
- Steps communicate via a constructor-injected shared context object — never by calling each other directly. Use `(.*)` for string captures and `(\d+)` for integers.
- Do not add new `[Binding]` step definitions in migrated areas, and do not add new feature behaviour as `.feature` files without explicit approval.

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
- [test-first-workflow.md](test-first-workflow.md) — full tests-first session model
- [failing-tests-workflow.md](failing-tests-workflow.md) — mandatory procedure when tests fail
- [live-system-testing-guide.md](live-system-testing-guide.md) — live environment setup and CI patterns
- [module-development-guide.md](module-development-guide.md) — module test expectations
- [.agents/20-guardrails/workflow/testing-rules.md](../.agents/20-guardrails/workflow/testing-rules.md) — enforced testing constraints
- [.agents/20-guardrails/workflow/test-first-workflow.md](../.agents/20-guardrails/workflow/test-first-workflow.md) — exact tests-first workflow contract

## Lifecycle-enabled connector tests

For project-lifecycle scenarios, mark tests with explicit lifecycle eligibility and ensure the run:

1. Creates a run-correlated ephemeral project name before the main assertions execute.
2. Binds execution to that created project identity.
3. Always attempts teardown in a finally path, including when the test body fails.
4. Emits a lifecycle record with create result, teardown result, blocking reason (if any), and teardown latency.

### SC-004 measurement plan (cleanup intervention reduction)

Track manual cleanup interventions before and after rollout:

- **Source**: lifecycle outcome logs (`ProjectLifecycle outcome ...`) and operator incident notes.
- **Metric**: count of runs requiring human cleanup follow-up per week.
- **Target**: reduce manual cleanup interventions by at least 50% over the first two reporting windows.
- **Collection**: run `scripts/project-lifecycle/collect-cleanup-metrics.ps1` against test logs and publish weekly trend snapshots.


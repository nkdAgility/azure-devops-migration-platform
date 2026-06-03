# Testing Standards

MSTest conventions, test naming, and organisation. See also: [coding-standards.md](../core/coding-standards.md), [module-rules.md](../domains/module-rules.md).

---

## Test Priority Hierarchy

| Priority | Category | Marker | Speed | Use |
| --- | --- | --- | --- | --- |
| 1 (highest) | Unit Tests | `[TestCategory("UnitTests")]` | < 50 ms | All logic, branching, transforms. No I/O, no DI. Single class/method in isolation. |
| 2 | Domain Tests (Internal DSL + MSTest) | `[TestCategory("DomainTests")]` | < 500 ms | Business behaviour across collaborating domain objects via the internal DSL. |
| 3 | Simulated System Tests | `[TestCategory("SystemTests_Simulated")]` | < 10 s | End-to-end with `Simulated` connector. No network. |
| 3a | Smoke System Tests | `[TestCategory("SystemTests_Smoke")]` | < 30 s | Critical-path subset of system tests run on every PR. |
| 4 (lowest) | Live System Tests | `[TestCategory("SystemTests")]`/`[TestCategory("SystemTests_Live")]` | < 60 s | Requires live ADO/TFS. Environment-gated. |

### Distinguishing UnitTests from DomainTests

| Criterion | UnitTests | DomainTests |
| --- | --- | --- |
| Scope | Single class/method in isolation | Business behaviour across collaborating domain objects |
| Dependencies | All dependencies mocked/stubbed | Uses real domain objects, DSL builders/runners/assertions |
| DSL usage | No `DevOpsMigrationPlatform.Testing` usage | Uses the internal DSL library (builders, runners, assertions) |
| Arrange style | Direct `new Foo()` + mock setup | Builder pattern (`A.WorkItem().WithField(...)`) |
| Assert style | Assert on return value / state of one object | Assert on observable business outcome |
| I/O | None | None (still in-process, no connectors) |

**Rule:** If the test references `DevOpsMigrationPlatform.Testing` DSL infrastructure (builders, runners, or domain assertions), it is a `DomainTests` test. Otherwise it is a `UnitTests` test.

**Principles:** Fast validation is the goal. Push tests downward (can it be a unit test?). Live tests are a last resort. Simulated replaces live where possible. CI gates run UnitTests + DomainTests by default.

**Anti-patterns (instant reject):** Simulated/Live test for logic with no external dependency. Feature test with real I/O when mocks suffice. New Live test without proving lower level can't cover it. Feature/Simulated/Live outnumbering Unit + Domain tests.

---

## Mandatory Category Tagging (Touch = Tag)

**Every time a test class or test method is created, edited, moved, or debugged, the agent MUST ensure the correct `[TestCategory]` attribute is present.**

| Condition | Required attribute |
| --- | --- |
| Test uses `DevOpsMigrationPlatform.Testing` DSL | `[TestCategory("DomainTests")]` |
| Test is isolated unit test (no DSL, no I/O) | `[TestCategory("UnitTests")]` |
| Test uses `Simulated` connector end-to-end | `[TestCategory("SystemTests_Simulated")]` |
| Test is a critical-path smoke subset | `[TestCategory("SystemTests_Smoke")]` |
| Test targets live ADO/TFS | `[TestCategory("SystemTests")]` or `[TestCategory("SystemTests_Live")]` |

**Enforcement rules:**

1. If a test is missing its `[TestCategory]` when touched, add it before completing the edit.
2. If a test is recategorised (e.g., promoted from SystemTests to DomainTests), update the attribute.
3. The `nkda-testdsl-*` skills must apply `[TestCategory("DomainTests")]` to all converted tests.
4. The `nkda-testdsl-refactor` skill must preserve or correct category tags during refactor.
5. A test with no `[TestCategory]` attribute is non-compliant — fix on contact.

---

## Framework

- Unit runner: MSTest only.
- Code-first behavioural tests: use `tests/DevOpsMigrationPlatform.Testing` internal DSL.
- Legacy BDD: Reqnroll remains only for feature families not yet migrated.
- New feature behaviour must not be added as `.feature` files unless explicitly approved.
- Do not add new `[Binding]`, `[Given]`, `[When]`, or `[Then]` classes for migrated areas.

### Layer structure

```text
tests/DevOpsMigrationPlatform.Testing/<Domain>/...             ← reusable typed DSL
tests/<Project>.Tests/<Area>/<Behaviour>Tests.cs               ← code-first MSTest behavioural tests
features/<operation>/...                                       ← legacy Reqnroll feature files pending migration only
tests/<Project>.Tests/<Area>/<Feature>Steps.cs                 ← legacy Reqnroll step definitions pending migration only
```

---

## Naming

- Unit test class: `<ClassName>Tests`
- Unit test method: `<MethodName>_<Condition>_<ExpectedResult>` (PascalCase)
- Legacy-only Reqnroll step definition class: `<FeatureName>Steps` (maps to `Feature:` name, PascalCase, `Steps` suffix)
- Legacy-only Reqnroll step methods: PascalCase action name. `[Given]`/`[When]`/`[Then]` attribute string must exactly match `.feature` step text.

---

## Step Definitions

Legacy-only (`Reqnroll` / `[Binding]`) guidance:

- Constructor-injected shared context object for communication between steps.
- `(.*)` for string captures, `(\d+)` for integers.
- Steps MUST NOT call each other directly — communicate via context only.
- No new `[Binding]` step definitions in migrated areas.

---

## Mock Rules

- `Mock<T>` (Moq) or hand-written fakes for infrastructure interfaces.
- Never use real `FileSystemArtefactStore` in unit tests.
- Never use live Azure DevOps in unit tests.
- Real filesystem → `[TestCategory("SystemTests_Simulated")]`.

---

## Required Coverage Per Module

| Behaviour | Required |
| --- | --- |
| `ValidateAsync` — valid artefact passes | Yes |
| `ValidateAsync` — missing field fails | Yes |
| `ExportAsync` — writes artefacts via `IArtefactStore` | Yes |
| `ExportAsync` — updates cursor via `IStateStore` | Yes |
| `ImportAsync` — reads one revision at a time (streaming) | Yes |
| `ImportAsync` — uses `IIdentityMappingService` | Yes (if applicable) |
| Cursor resume — re-run starts from cursor position | Yes |
| Cursor resume — first run with no cursor starts from beginning | Yes |

---

## Prohibited Patterns

- `Assert.IsTrue(true)` or empty step bodies.
- `Thread.Sleep` in steps.
- Static mutable fields on `[Binding]` classes.
- Steps calling each other directly.
- Catching all exceptions without re-asserting.
- Steps depending on execution order beyond Given/When/Then.
- `Assert.Inconclusive` in any test unless explicitly operator-approved (see below).

---

## Assert.Inconclusive Is Banned

`Assert.Inconclusive` (and any equivalent — `Assert.Ignore`, `Assert.Skip`, or any mechanism that produces a skipped/inconclusive result) **must not appear in any test** without explicit operator approval.

A skipped test is not a passing test. It provides no verification signal and silently hides defects.

### Missing prerequisites must use Assert.Fail

If a test cannot execute because a required environment variable, credential, external service, or fixture is absent, the test must call `Assert.Fail` with a clear message naming the missing prerequisite.

Do not use `Assert.Inconclusive` to hide a missing configuration. The absence of a required prerequisite is a defect in the environment. `Assert.Fail` makes that defect visible.

### Operator-approved exceptions

The **only** permitted use of `Assert.Inconclusive` is for a live test that targets infrastructure that is explicitly known to be absent in all CI environments (for example, an on-premises TFS server with no hosted equivalent).

To use `Assert.Inconclusive` for this purpose, all three conditions must be met:

1. A comment in the test body names the missing infrastructure and states why no CI environment has it.
2. The suite documentation records the test as intentionally absent from CI.
3. A human operator has approved the exception in writing (e.g. in a PR comment or decision record).

If any condition is not met, the `Assert.Inconclusive` call is a defect and must be replaced with `Assert.Fail`.

---

## Diagnosing System Test Failures

When a `SystemTest_Simulated`, `SystemTest`, or `SystemTest_Live` test fails, every CLI and agent process spawned by that test writes full OTel file diagnostics to:

```text
.output/workingtests/{TestName}/.otel-diagnostics/
```

where `{TestName}` is the **exact MSTest method name** (e.g. `QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles`), and the path is relative to the **repository root**.

**Absolute path pattern:**

```text
<repo-root>\.output\workingtests\<TestMethodName>\.otel-diagnostics\
```

`CliRunner.TestWorkingFolder` is `.output\workingtests`. Each spawned process writes logs, traces, and metrics for that test run into the `.otel-diagnostics` folder above.

When reproducing the same simulated or live run with `--diagnostics`, also inspect `.output/workingtests/{TestMethodName}/.otel-diagnostics/inbox/` for raw `bootstrap`, `telemetry`, `progress-{module}-{stage}`, and diagnostics payloads.

The contributor-facing debugging workflow lives in [docs/testing-guide.md](../../../docs/testing-guide.md). This guardrail only establishes the required location of the evidence and the expectation that debugging analysis uses it.

---

## CLI Feature → System Test Requirement

Every CLI command MUST have `[TestCategory("SystemTests")]` test that:

1. Guards on env vars (calls `Assert.Fail` with a clear message if absent — see Assert.Inconclusive Is Banned above).
2. Exercises the feature against real/simulated system.
3. Asserts observable output (files, zip, records).
4. Co-located in `.Tests` project under `Commands/`.

| CLI command | Test class | Assertion |
| --- | --- | --- |
| `queue` (`Mode: Export`, ADO source) | `AzureDevOpsExportCommandTests` | `WorkItems/` directory + zip |
| `queue` (`Mode: Inventory`) | `InventoryCommandTests` | Records against live ADO |
| `migrate` (Simulated) | `SimulatedMigrationCommandTests` | `WorkItems/`, `Checkpoints/`, `Logs/progress.jsonl` |
| `queue` (`Mode: Export`, TFS source) | (environment-gated: requires live TFS) | — |



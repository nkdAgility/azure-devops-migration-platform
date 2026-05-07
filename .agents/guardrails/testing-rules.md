# Testing Standards

MSTest conventions, test naming, and organisation. See also: [coding-standards.md](./coding-standards.md), [module-rules.md](./module-rules.md).

---

## Test Priority Hierarchy

| Priority | Category | Marker | Speed | Use |
|----------|----------|--------|-------|-----|
| 1 (highest) | Unit Tests | `[TestClass]`/`[TestMethod]` | < 50 ms | All logic, branching, transforms. No I/O, no DI. |
| 2 | Feature Tests (Reqnroll) | `[Binding]` + `.feature` | < 500 ms | Behaviour scenarios with in-memory fakes/mocks. |
| 3 | Simulated System Tests | `[TestCategory("SystemTest_Simulated")]` | < 10 s | End-to-end with `Simulated` connector. No network. |
| 4 (lowest) | Live System Tests | `[TestCategory("SystemTest")]`/`[TestCategory("SystemTest_Live")]` | < 60 s | Requires live ADO/TFS. Environment-gated. |

**Principles:** Fast validation is the goal. Push tests downward (can it be a unit test?). Live tests are a last resort. Simulated replaces live where possible. CI gates run Unit + Feature by default.

**Anti-patterns (instant reject):** Simulated/Live test for logic with no external dependency. Feature test with real I/O when mocks suffice. New Live test without proving lower level can't cover it. Feature/Simulated/Live outnumbering Unit tests.

---

## Framework

- **BDD:** Reqnroll (`Reqnroll.MSTest`). Reads `.feature` files, generates test runner glue.
- **Unit runner:** MSTest only. No xUnit, NUnit.
- Async steps: `async Task` return type (not `async void`).

### Layer structure
```
features/<operation>[/<connector>/<module>]/<feature>.feature  ← Gherkin
tests/<Project>.Tests/<Area>/<Feature>Steps.cs                ← Reqnroll [Binding]
tests/<Project>.Tests/<Area>/<Feature>Context.cs              ← shared context/mocks
tests/<Project>.Tests/<Area>/<ClassName>Tests.cs              ← plain MSTest unit tests
```

---

## Naming

- Unit test class: `<ClassName>Tests`
- Unit test method: `<MethodName>_<Condition>_<ExpectedResult>` (PascalCase)
- Step definition class: `<FeatureName>Steps` (maps to `Feature:` name, PascalCase, `Steps` suffix)
- Step methods: PascalCase action name. `[Given]`/`[When]`/`[Then]` attribute string must exactly match `.feature` step text.

---

## Step Definitions

- Constructor-injected shared context object for communication between steps.
- `(.*)` for string captures, `(\d+)` for integers.
- Steps MUST NOT call each other directly — communicate via context only.

---

## Mock Rules

- `Mock<T>` (Moq) or hand-written fakes for infrastructure interfaces.
- Never use real `FileSystemArtefactStore` in unit tests.
- Never use live Azure DevOps in unit tests.
- Real filesystem → `[TestCategory("Integration")]`.

---

## Required Coverage Per Module

| Behaviour | Required |
|-----------|----------|
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

---

## Diagnosing System Test Failures

When a `SystemTest_Simulated` or `SystemTest` test fails, every CLI and agent process spawned by that test writes full OTel file diagnostics (structured NDJSON logs) to:

```
storage/{TestName}/.otel-diagnostics/
```

where `{TestName}` is the **exact MSTest method name** (e.g. `QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles`), and the path is relative to the **repository root**.

**Absolute path pattern:**
```
<repo-root>\storage\<TestMethodName>\.otel-diagnostics\
```

**How it works:**
- Each test sets `DEVOPS_MIGRATION_TEST_STORAGE=storage/{TestName}` in the spawned process environment.
- `CliRunner` reads this env var and overrides `Telemetry__DiagnosticsPath` to `{repoRoot}/{testStorageRel}/.otel-diagnostics`.
- All processes (CLI, ControlPlaneHost, MigrationAgent) log to this path for the duration of that test invocation.

**Debugging workflow:**
1. Identify the failing test method name exactly.
2. Navigate to `storage/{TestMethodName}/.otel-diagnostics/` in the repo root.
3. Open the NDJSON log files — each line is a structured log event with `{Level}`, `{Message}`, `{Exception}`, and span context.
4. Look for `Error` or `Critical` entries; the `{Exception}` field contains the full stack trace.

> **Note**: `storage/` is `.gitignore`d. These files exist only after a local test run. They are not available in CI — use the test output/stderr captured in the test result for CI failures.

---

## CLI Feature → System Test Requirement

Every CLI command MUST have `[TestCategory("SystemTest")]` test that:
1. Guards on env vars (marks `Inconclusive` if absent).
2. Exercises the feature against real/simulated system.
3. Asserts observable output (files, zip, records).
4. Co-located in `.Tests` project under `Commands/`.

| CLI command | Test class | Assertion |
|---|---|---|
| `export` (ADO) | `AzureDevOpsExportCommandTests` | `WorkItems/` directory + zip |
| `queue` (`Mode: Inventory`) | `InventoryCommandTests` | Records against live ADO |
| `migrate` (Simulated) | `SimulatedMigrationCommandTests` | `WorkItems/`, `Checkpoints/`, `Logs/progress.jsonl` |
| `tfsexport` | (environment-gated: requires live TFS) | — |

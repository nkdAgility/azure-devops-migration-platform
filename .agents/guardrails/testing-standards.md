# Testing Standards

MSTest conventions, test naming, and organisation. See also: [coding-standards.md](./coding-standards.md), [module-template.md](./module-template.md).

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

## CLI Feature → System Test Requirement

Every CLI command MUST have `[TestCategory("SystemTest")]` test that:
1. Guards on env vars (marks `Inconclusive` if absent).
2. Exercises the feature against real/simulated system.
3. Asserts observable output (files, zip, records).
4. Co-located in `.Tests` project under `Commands/`.

| CLI command | Test class | Assertion |
|---|---|---|
| `export` (ADO) | `AzureDevOpsExportCommandTests` | `WorkItems/` directory + zip |
| `discovery inventory` | `InventoryCommandTests` | Records against live ADO |
| `migrate` (Simulated) | `SimulatedMigrationCommandTests` | `WorkItems/`, `Checkpoints/`, `Logs/progress.jsonl` |
| `tfsexport` | (environment-gated: requires live TFS) | — |

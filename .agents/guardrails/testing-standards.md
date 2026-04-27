# Testing Standards

This document defines MSTest conventions, test naming rules, and test organisation for the Azure DevOps Migration Platform.

See also: [.agents/guardrails/coding-standards.md](./coding-standards.md) for the broader coding standards and [.agents/guardrails/module-template.md](./module-template.md) for per-module test requirements.

---

## Test Priority Hierarchy

Tests MUST be prioritised in this order — overwhelmingly favour the top of the list:

| Priority | Category | Marker | Speed Target | When to Use |
|----------|----------|--------|--------------|-------------|
| 1 (highest) | **Unit Tests** | `[TestClass]` / `[TestMethod]` | < 50 ms each | All logic, branching, transforms, calculations. No I/O, no DI container. |
| 2 | **Feature Tests** (Reqnroll) | `[Binding]` + `.feature` | < 500 ms each | Behaviour scenarios against in-memory fakes/mocks. Validates contracts. |
| 3 | **Simulated System Tests** | `[TestCategory("SystemTest_Simulated")]` | < 10 s each | End-to-end through real code with `Simulated` connector. No network. |
| 4 (lowest) | **Live System Tests** | `[TestCategory("SystemTest")]` or `[TestCategory("SystemTest_Live")]` | < 60 s each | Requires live Azure DevOps / TFS. Environment-gated. |

### Guiding Principles

1. **Fast validation is the goal.** The default inner-loop must complete in seconds, not minutes.
2. **Push tests downward.** When reviewing or writing tests, actively ask: *"Can this be a unit test instead?"* If the answer is yes, make it a unit test.
3. **Live tests are a last resort.** A live test is only justified when verifying connector-specific behaviour that cannot be reproduced with the `Simulated` connector or mocks.
4. **Simulated tests replace live tests where possible.** The `Simulated` connector exists precisely to eliminate external dependencies while still exercising the full pipeline.
5. **Refactor toward unit.** When fixing or extending existing Feature/Simulated/Live tests, extract testable logic into pure functions or small classes and cover them with unit tests. The higher-level test may then become a thin integration check.
6. **CI gates run Unit + Feature by default.** Simulated and Live tests run in separate, slower pipelines.

### Anti-patterns (reject immediately)

- Writing a Simulated or Live test for logic that has no external dependency.
- A Feature test that spins up real I/O when mocks would suffice.
- Adding a new Live test without first proving the behaviour cannot be covered at a lower level.
- Test suites where Feature/Simulated/Live tests outnumber Unit tests — this signals missing unit coverage.

---

## Test Framework

- **BDD layer:** Reqnroll (`Reqnroll.MSTest` NuGet package). Reqnroll reads the Gherkin `.feature` files and generates the test runner glue.
- **Unit test runner:** MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`) — Reqnroll runs on top of MSTest.
- No xUnit, NUnit, or other frameworks.
- Step definition classes use `[Binding]` (Reqnroll). The `[Given]`, `[When]`, `[Then]` attributes come from `Reqnroll`.
- Async steps must use `async Task` return type, not `async void`.

### How the layers fit together

```
features/<operation>[/<connector>/<module>[/<sub-module>]]/<feature>.feature  ← Gherkin (human-readable, Reqnroll reads this)
tests/<Project>.Tests/<Area>/<Feature>Steps.cs         ← Reqnroll [Binding] step definitions
tests/<Project>.Tests/<Area>/<Feature>Context.cs       ← shared ScenarioContext / mocks
tests/<Project>.Tests/<Area>/<ClassName>Tests.cs       ← plain MSTest unit tests for internal logic
```

Reqnroll matches each `Given/When/Then` step in the `.feature` file to a `[Given]`/`[When]`/`[Then]` method in the corresponding `Steps.cs` file. MSTest executes the resulting test.

The `*Tests.cs` files are **not** Reqnroll step definitions — they are standard `[TestClass]`/`[TestMethod]` classes that test internal logic directly. They are required for any class with branching logic, calculation, or state transformation.

### Unit test naming convention

- Class name: `<ClassName>Tests` (the class under test, with `Tests` suffix).
- Method name: `<MethodName>_<Condition>_<ExpectedResult>` in PascalCase.
- Example: `CursorReader_WhenCursorFileIsMissing_ReturnsNull`

```csharp
[TestClass]
public class CursorReaderTests
{
    [TestMethod]
    public void ReadCursor_WhenFileExists_ReturnsParsedCursor()
    {
        // Arrange ...
        // Act ...
        // Assert ...
    }

    [TestMethod]
    public void ReadCursor_WhenFileIsMissing_ReturnsNull()
    {
        // Arrange ...
        // Act ...
        // Assert ...
    }
}
```

---

## Test Project Structure

```
tests/
  <ProjectName>.Tests/
    reqnroll.json              ← Reqnroll configuration (one per test project)
    <Area>/
      <FeatureName>Steps.cs   ← Reqnroll [Binding] step definitions
      <FeatureName>Context.cs ← shared mocks / DI context for the feature (optional)
```

Examples:
```
tests/DevOpsMigrationPlatform.Export.Tests/WorkItems/ExportWorkItemRevisionsSteps.cs
tests/DevOpsMigrationPlatform.Export.Tests/WorkItems/ExportAttachmentsSteps.cs
tests/DevOpsMigrationPlatform.Infrastructure.Storage.Tests/ArtefactStore/FileSystemArtefactStoreSteps.cs
```

### Required NuGet packages (per test project)

```xml
<PackageReference Include="Reqnroll.MSTest" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
```

---

## Step Definition Class Naming

- `[Binding]` class name = `<FeatureName>Steps`
- Maps directly to the Gherkin `Feature:` name (PascalCase, spaces removed, `Steps` suffix).
- Example: Feature `Export Work Item Revisions` → `ExportWorkItemRevisionsSteps`.

---

## Step Method Naming

- Step methods are named after the action they implement, in PascalCase.
- The `[Given]`, `[When]`, `[Then]` attribute string must **exactly** match the step text in the `.feature` file (Reqnroll uses regex matching).
- Example:
  ```csharp
  [Given(@"a work item with id (\d+) has (\d+) revisions")]
  public void GivenAWorkItemWithIdHasRevisions(int id, int revisionCount) { ... }
  ```
- Use `(.*)` for string captures and `(\d+)` for integer captures.
- Prefer literal step text over regex when the step has no parameters.

---

## Step Definition Structure

Step definitions follow Given/When/Then and delegate to a shared context object:

```csharp
[Binding]
public class ExportWorkItemRevisionsSteps
{
    private readonly ExportWorkItemRevisionsContext _ctx;

    public ExportWorkItemRevisionsSteps(ExportWorkItemRevisionsContext ctx)
        => _ctx = ctx;   // Reqnroll injects shared context via constructor

    [Given(@"a work item with id (\d+) has (\d+) revisions")]
    public void GivenAWorkItemWithIdHasRevisions(int id, int revisionCount)
    {
        _ctx.WorkItemId = id;
        _ctx.RevisionCount = revisionCount;
        // configure _ctx.ArtefactStore mock to return test paths
    }

    [When(@"the WorkItems export module runs")]
    public async Task WhenTheWorkItemsExportModuleRuns()
    {
        await _ctx.Sut.ExportAsync(_ctx.MigrationContext, CancellationToken.None);
    }

    [Then(@"the cursor file at ""(.*?)"" is updated with the last processed revision path")]
    public void ThenTheCursorFileIsUpdated(string cursorPath)
    {
        _ctx.StateStore.Verify(s => s.WriteAsync(
            It.Is<string>(k => k == cursorPath),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
```

The **context class** holds shared mocks and the system-under-test:

```csharp
public class ExportWorkItemRevisionsContext
{
    public Mock<IArtefactStore> ArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<IStateStore> StateStore { get; } = new(MockBehavior.Strict);
    public WorkItemsExportModule Sut { get; }
    public int WorkItemId { get; set; }
    public int RevisionCount { get; set; }

    public ExportWorkItemRevisionsContext()
        => Sut = new WorkItemsExportModule(ArtefactStore.Object, StateStore.Object);
}
```

---

## Mock and Fake Rules

- Use `Mock<T>` (Moq) or hand-written fakes for all infrastructure interfaces.
- Never use a real `FileSystemArtefactStore` in a unit test.
- Never use a live Azure DevOps connection in a unit test.
- If a test requires a real filesystem, it is an **integration test** and must be marked with `[TestCategory("Integration")]`.

---

## Required Test Coverage Per Module

Every new module must have tests covering:

| Behaviour | Required |
|---|---|
| `ValidateAsync` — valid artefact passes | Yes |
| `ValidateAsync` — missing required field fails | Yes |
| `ExportAsync` — writes artefacts via `IArtefactStore` | Yes |
| `ExportAsync` — updates cursor via `IStateStore` | Yes |
| `ImportAsync` — reads one revision at a time (streaming) | Yes |
| `ImportAsync` — uses `IIdentityMappingService` for identities | Yes (if applicable) |
| Cursor resume — re-run starts from cursor position | Yes |
| Cursor resume — first run with no cursor starts from beginning | Yes |

---

## Prohibited Patterns

- `Assert.IsTrue(true)` or empty step bodies (steps must assert or set state meaningfully).
- `Thread.Sleep` in steps — use `CancellationToken` or async/await patterns.
- Static mutable fields on `[Binding]` classes — use Reqnroll's injected context objects for per-scenario state.
- Steps that call each other directly — steps communicate only via the shared context object.
- Catching all exceptions without re-asserting — steps that swallow exceptions silently pass.
- Steps that depend on execution order beyond the Given/When/Then sequence — each scenario must be independent.

---

## CLI Feature → System Test Requirement

Every feature exposed through a CLI command **MUST** have at least one `[TestCategory("SystemTest")]` test that:

1. Guards on required environment variables (marks `Inconclusive` if absent, never fails).
2. Exercises the feature against a real external system (no mocks for the external system). For the `Simulated` source/target, no external connectivity is required — the simulated source/target IS the system under test.
3. Asserts observable output — files written, zip produced, records returned, etc.
4. Is co-located in the relevant `.Tests` project under `Commands/` or `Commands/<Area>/`.

A system test that only loads config or validates round-trip JSON **does not satisfy this requirement**. The test must exercise the actual data path.

### Requirement traceability

| CLI command | Required system test class |
|---|---|
| `export` (ADO) | `AzureDevOpsExportCommandTests` — asserts `WorkItems/` directory and zip produced |
| `discovery inventory` | `InventoryCommandTests` — asserts inventory records returned against live ADO organisation (`AZDEVOPS_SYSTEM_TEST_ORG` + `AZDEVOPS_SYSTEM_TEST_PAT`) |
| `migrate` (Simulated) | `SimulatedMigrationCommandTests` — runs full end-to-end migrate with `source.type: Simulated` and `target.type: Simulated`, no external connectivity; asserts `WorkItems/` structure, `Checkpoints/` cursor, and `Logs/progress.jsonl` |
| `tfsexport` | (environment-gated: requires live TFS) |

Add a row here whenever a new CLI command is introduced.

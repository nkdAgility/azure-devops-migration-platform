# Testing Standards

This document defines MSTest conventions, test naming rules, and test organisation for the Azure DevOps Migration Platform.

See also: [agents/coding-standards.md](../../agents/coding-standards.md) for the broader coding standards and [agents/module-template.md](../../agents/module-template.md) for per-module test requirements.

---

## Test Framework

- **BDD layer:** Reqnroll (`Reqnroll.MSTest` NuGet package). Reqnroll reads the Gherkin `.feature` files and generates the test runner glue.
- **Unit test runner:** MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`) — Reqnroll runs on top of MSTest.
- No xUnit, NUnit, or other frameworks.
- Step definition classes use `[Binding]` (Reqnroll). The `[Given]`, `[When]`, `[Then]` attributes come from `Reqnroll`.
- Async steps must use `async Task` return type, not `async void`.

### How the layers fit together

```
tests/acceptance/<area>/<feature>.feature   ← Gherkin (human-readable, Reqnroll reads this)
tests/<Project>.Tests/<Area>/<Feature>Steps.cs  ← Reqnroll [Binding] step definitions
tests/<Project>.Tests/<Area>/<Feature>Context.cs ← shared ScenarioContext / mocks
```

Reqnroll matches each `Given/When/Then` step in the `.feature` file to a `[Given]`/`[When]`/`[Then]` method in the corresponding `Steps.cs` file. MSTest executes the resulting test.

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

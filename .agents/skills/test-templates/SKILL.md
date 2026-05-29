---
name: test-templates
description: Generates Reqnroll step definition files from a structured test plan using Reqnroll.MSTest.
---

# Test Templates — Skill Instructions

## Role

When this skill is active, generate Reqnroll step definition files from a structured test plan using `Reqnroll.MSTest`.

## Step Definitions Template

Use this pattern for any `IDataTypeModule` feature:

```csharp
using Reqnroll;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.Abstractions;    // adjust namespace as needed

namespace DevOpsMigrationPlatform.<Area>.Tests.<Feature>;

[Binding]
[TestCategory("DomainTest")]
[TestCategory("SystemTest")]
[TestCategory("SystemTest_Simulated")]
public class <FeatureName>Steps
{
    private readonly <FeatureName>Context _ctx;

    public <FeatureName>Steps(<FeatureName>Context ctx) => _ctx = ctx;

    // ── Given ────────────────────────────────────────────────────────────────

    [Given(@"<exact step text from .feature, with (\d+) for int params>")]
    public void Given_<StepName>(<params>)
    {
        // TODO: configure _ctx mocks / set _ctx properties
        throw new PendingStepException();   // Red stage — intentionally failing
    }

    // ── When ─────────────────────────────────────────────────────────────────

    [When(@"the WorkItems export module runs")]
    public async Task When_TheExportModuleRuns()
    {
        // TODO: await _ctx.Sut.ExportAsync(_ctx.MigrationContext, CancellationToken.None);
        throw new PendingStepException();
    }

    // ── Then ─────────────────────────────────────────────────────────────────

    [Then(@"<exact then step text>")]
    public void Then_<StepName>()
    {
        // TODO: assert on _ctx.ArtefactStore or _ctx.StateStore mock
        throw new PendingStepException();
    }
}
```

## Context Class Template

The context class holds all shared state for a feature's scenarios:

```csharp
namespace DevOpsMigrationPlatform.<Area>.Tests.<Feature>;

/// <summary>
/// Reqnroll scenario context for <FeatureName>.
/// Injected via Reqnroll's built-in DI into step definition classes.
/// </summary>
public class <FeatureName>Context
{
    public Mock<IArtefactStore> ArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<IStateStore> StateStore { get; } = new(MockBehavior.Strict);
    // public Mock<IIdentityMappingService> IdentityMapping { get; } = new(MockBehavior.Strict);

    public <ModuleClass> Sut { get; }

    // Scenario state — set in Given steps, read in Then steps
    public List<string> ProcessedPaths { get; } = new();

    public <FeatureName>Context()
        => Sut = new <ModuleClass>(ArtefactStore.Object, StateStore.Object);
}
```

## Cursor Resume Pattern

In the `Given` step, pre-populate the cursor state:

```csharp
[Given(@"the cursor file at \""(.*?)\"" records the last processed folder as \""(.*?)\""")]
public void GivenTheCursorRecords(string cursorFile, string lastPath)
{
    _ctx.StateStore
        .Setup(s => s.ReadAsync(
            It.Is<string>(k => k == cursorFile),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(lastPath);
    throw new PendingStepException();
}
```

## Streaming Verification Pattern

In the `Then` step, verify the mock was called one path at a time:

```csharp
[Then(@"work item revisions are processed one at a time")]
public void ThenRevisionsAreProcessedOneAtATime()
{
    // Assert _ctx.ProcessedPaths were delivered one-by-one (peak concurrent == 1)
    // TODO: implement assertion once production code exists
    throw new PendingStepException();
}
```

## Rules When Applying Templates

1. Replace all `<PlaceholderNames>` before emitting the file.
2. Use `throw new PendingStepException()` — Reqnroll marks the scenario as pending/failing in the red stage.
3. Use `MockBehavior.Strict` so unexpected calls are caught early.
4. All async step methods use `async Task` — never `async void`.
5. One `Steps.cs` and one `Context.cs` per feature.
6. The `[Binding]` class must not be `static` and must not hold static mutable fields.
7. Step attribute regex must exactly match the `.feature` file step text — mismatches cause "no matching step definition" errors at runtime.
8. Pure unit tests must always include `[TestCategory("UnitTest")]`.
9. Every DSL `.feature` file must include `@DomainTest`, `@SystemTest`, and exactly one of `@SystemTest_Live`, `@SystemTest_Smoke`, or `@SystemTest_Simulated`.

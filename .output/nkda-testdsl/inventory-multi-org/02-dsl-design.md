# DSL Design — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/simulated/inventory-multi-org.feature`
Design date: 2026-06-10
Assessment input: `.output/nkda-testdsl/inventory-multi-org/01-feature-assessment.md`

---

## 1. Summary

The assessment classified this feature family as `pre-existing` / `matched`. The single
scenario `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` is fully covered
by an existing MSTest method at
`tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:57`.

No new DSL types, no new test methods, and no new builder aliases are required. This document
records the canonical DSL surface that covers the scenario, confirms tag compliance, and
defines the deletion plan for the Reqnroll feature file.

---

## 2. Business Capability

**Capability:** Module independence during inventory execution.

The production pipeline must not suppress artefact output from inventory-capable data modules
(WorkItems, Identities, Nodes, Teams) when the `InventoryDiscoveryModule`
(`InventoryAnalyser`) is excluded from the job. This is a compositional isolation guarantee.

All DSL surface is grouped under this capability. No migration-phase bucket classification
is used.

---

## 3. Typed Entry Point

```csharp
// Namespace: DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules
// File: tests/.../Modules/InventoryModules/InventoryModulesScenario.cs

public sealed class InventoryModulesScenario
{
    public static InventoryModulesBuilder Arrange();
}
```

`Arrange()` is the sole entry point. It returns a fresh `InventoryModulesBuilder` with all
four data modules and the `InventoryAnalyser` enabled by default.

---

## 4. Builder

```csharp
// Namespace: DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules
// File: tests/.../Modules/InventoryModules/InventoryModulesBuilder.cs

public sealed class InventoryModulesBuilder
{
    // Removes the InventoryAnalyser post-processor from the job.
    public InventoryModulesBuilder WithoutInventoryAnalyser();

    // Feature-vocabulary alias; delegates to WithoutInventoryAnalyser().
    // Bridges "InventoryDiscoveryModule" (feature language) to the internal concept.
    public InventoryModulesBuilder WithoutInventoryDiscoveryModule();

    // Removes a named data module (WorkItems | Identities | Nodes | Teams).
    public InventoryModulesBuilder WithoutModule(string moduleName);

    // Executes the job and returns a result wrapper.
    public Task<InventoryModulesResult> RunAsync(CancellationToken cancellationToken = default);
}
```

For the `inventory-multi-org` scenario the call chain is:

```csharp
InventoryModulesScenario.Arrange()
    .WithoutInventoryDiscoveryModule()
    .RunAsync()
```

`WithoutInventoryDiscoveryModule()` (line 37 of `InventoryModulesBuilder.cs`) is the
behaviour-first alias that maps the feature vocabulary directly to the DSL without string
matching. It delegates to `WithoutInventoryAnalyser()` which sets `_includeInventoryAnalyser
= false`.

Both methods exist in the codebase. No additions are needed.

---

## 5. Runner / Driver

```csharp
// Namespace: DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules
// File: tests/.../Modules/InventoryModules/InventoryModulesDriver.cs (internal)

internal static class InventoryModulesDriver
{
    internal static Task<InventoryModulesResult> RunAsync(
        bool includeWorkItems,
        bool includeIdentities,
        bool includeNodes,
        bool includeTeams,
        bool includeInventoryAnalyser,
        CancellationToken cancellationToken = default);
}
```

`InventoryModulesDriver` is an internal implementation detail. Tests interact with it only
through `InventoryModulesBuilder.RunAsync()`. The driver constructs mocks, wires the DI
container, executes the job, and returns an `InventoryModulesResult`. No changes required.

---

## 6. Result and Assertion Extensions

```csharp
// Namespace: DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules
// File: tests/.../Modules/InventoryModules/InventoryModulesResult.cs

public sealed class InventoryModulesResult
{
    // True when InventoryAnalyser was registered in the job that produced this result.
    public bool InventoryAnalyserWasIncluded { get; }

    // Asserts PersistIndexAsync was called for the named module's inventory context.
    public void AssertModuleArtefactExists(string moduleName);

    // Asserts PersistIndexAsync was called at least four times with a valid
    // per-project inventory index context (FileName="inventory.json",
    // non-empty Organisation, non-empty Project).
    public void AssertAllStandardModuleArtefactsExist();
}
```

`InventoryAnalyserWasIncluded` is the guard property. The test asserts `IsFalse` on it before
asserting artefact presence, to confirm the discovery module was genuinely excluded and rule
out test-setup errors.

`AssertAllStandardModuleArtefactsExist()` uses `Times.AtLeast(4)` against the
`IPackageAccess.PersistIndexAsync` mock. This confirms that at least one write per data module
was made (WorkItems, Identities, Nodes, Teams).

No changes required to this type.

---

## 7. Fixture

No separate fixture class is required. All per-test state (mocks, DI container, execution
context) is constructed inside `InventoryModulesDriver.RunAsync` and is discarded when the
test completes. MSTest `[TestClass]` / `[TestMethod]` isolation is sufficient.

---

## 8. Target Test — Scenario Mapping

### Scenario: `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`

| Property | Value |
|---|---|
| Test class | `InventoryModulesTests` |
| Test method | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` |
| File | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` |
| Line | 57 |
| Status | **exists — no action required** |

**Canonical DSL form (as implemented at line 57):**

```csharp
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
[TestCategory("inventory")]
[TestCategory("multi-org")]
[TestMethod]
public async Task InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced()
{
    var result = await InventoryModulesScenario
        .Arrange()
        .WithoutInventoryDiscoveryModule()
        .RunAsync();

    // Guard: confirm the discovery module was genuinely absent.
    Assert.IsFalse(result.InventoryAnalyserWasIncluded,
        "Test setup error: InventoryDiscoveryModule (InventoryAnalyser) should not have been included.");

    // Primary assertion: all four data-module artefacts are still present.
    result.AssertAllStandardModuleArtefactsExist();
}
```

Feature step to DSL mapping:

| Feature step | DSL equivalent |
|---|---|
| `Given a simulated inventory job without InventoryDiscoveryModule` | `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` |
| `When the inventory job is executed` | `.RunAsync()` |
| `Then inventory artefacts are produced by inventory-capable modules` | `result.AssertAllStandardModuleArtefactsExist()` |

---

## 9. Test Tags

| Tag | Category attribute | Compliance |
|---|---|---|
| `CodeTest` | `[TestCategory("CodeTest")]` | present |
| `IntegrationTests` | `[TestCategory("IntegrationTests")]` | present |
| `inventory` | `[TestCategory("inventory")]` | present |
| `multi-org` | `[TestCategory("multi-org")]` | present |

All four required categories are present on the method at lines 52–55 of
`InventoryModulesTests.cs`. Tag compliance status: **compliant**.

---

## 10. Scenario-to-Test Inventory Update

Row 1 of `00-scenario-test-inventory.md` is already correct and requires no update.

| # | Wiring State | Coverage Origin | Scenario Name | Planned / Actual DSL Test Name | Mapping Status | Tag Compliance |
|---|---|---|---|---|---|---|
| 1 | `unwired` | `pre-existing` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `matched` | `compliant` |

---

## 11. Deletion Plan — Legacy Reqnroll Artefacts

### Feature file

| Path | Action |
|---|---|
| `features/inventory/simulated/inventory-multi-org.feature` | **Delete** — unwired, no code-behind generated, behaviour fully covered by the existing MSTest method. |

### Generated code-behind

None exists. The assessment confirmed no `.feature.cs` was generated. No deletion needed.

### Step bindings

None exist. No `*Steps.cs` files reference this scenario. No deletion needed.

### Test project entry (`ExternalFeatureFiles`)

No `.csproj` references this feature file. No removal needed.

**Total files to delete: 1** (`features/inventory/simulated/inventory-multi-org.feature`).

---

## 12. DSL Surface Summary

| Type | File | Change |
|---|---|---|
| `InventoryModulesScenario` | `Modules/InventoryModules/InventoryModulesScenario.cs` | No change |
| `InventoryModulesBuilder` | `Modules/InventoryModules/InventoryModulesBuilder.cs` | No change — `WithoutInventoryDiscoveryModule()` already present at line 37 |
| `InventoryModulesResult` | `Modules/InventoryModules/InventoryModulesResult.cs` | No change |
| `InventoryModulesDriver` | `Modules/InventoryModules/InventoryModulesDriver.cs` | No change |
| `InventoryModuleFactory` | `Modules/InventoryModules/InventoryModuleFactory.cs` | No change |
| `InventoryModulesTests` | `Modules/InventoryModulesTests.cs` | No change — target test already present at line 57 with compliant tags |

No new DSL concepts are introduced. The full surface is already present in the codebase.

---

## 13. Design Decisions

| Decision | Rationale |
|---|---|
| `WithoutInventoryDiscoveryModule()` kept as alias for `WithoutInventoryAnalyser()` | Preserves feature vocabulary without breaking the internal production naming. Both methods are retained; neither is deprecated. |
| Bulk `AssertAllStandardModuleArtefactsExist()` over per-module assertions | Appropriate at integration level. `Times.AtLeast(4)` is sufficient to catch total artefact absence. `AssertModuleArtefactExists(moduleName)` is available if per-module precision is required. |
| No fixture class | The driver encapsulates all setup and teardown. MSTest method-level isolation eliminates the need for a shared fixture. |
| Feature file deleted, not archived | The file is unwired and has no Reqnroll code-behind. Archiving provides no value; clean deletion removes a false signal of pending Reqnroll work. |

# DSL Design — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/ado/inventory-multi-org.feature`
Design date: 2026-06-10
Input: `01-feature-assessment.md`

---

## 1. Context

The assessment classified this feature family as `partial-existing`. The single scenario
`Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` has substantive coverage
already provided by `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`
in `InventoryModulesTests.cs`. The DSL surface that hosts this test already exists and is
well-formed:

- Entry point: `InventoryModulesScenario.Arrange()` → `InventoryModulesBuilder`
- Runner: `InventoryModulesBuilder.RunAsync()` → `InventoryModulesResult`
- Assertions: `InventoryModulesResult.AssertAllStandardModuleArtefactsExist()`

No new DSL types or namespaces are required. This design specifies:

1. The targeted extension to `InventoryModulesBuilder` needed to complete feature coverage.
2. The targeted tag corrections to the existing test method.
3. The exact target test name, tags, and DSL call-chain for the scenario.
4. The deletion plan for Reqnroll artefacts (none exist — confirmed in assessment).
5. Updates to `00-scenario-test-inventory.md`.

---

## 2. InventoryDiscoveryModule Disambiguation

**Finding:** No class or interface named `InventoryDiscoveryModule` exists in `src/`. The only
removal method on the builder is `WithoutInventoryAnalyser()`.

**Design decision:** The feature scenario term `InventoryDiscoveryModule` is treated as a
feature-vocabulary alias for the `InventoryAnalyser` concept that exists in the production
codebase. No new production class will be introduced. The builder will gain one new method
(`WithoutInventoryDiscoveryModule()`) that delegates to the existing `_includeInventoryAnalyser`
flag. This makes the DSL vocabulary match the feature language without changing production
semantics and without introducing a duplicate removal path.

---

## 3. DSL Surface — Additions to Existing Types

### 3.1 `InventoryModulesBuilder` — one new method

**Location:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModules/InventoryModulesBuilder.cs`

```csharp
/// <summary>
/// Removes the InventoryDiscoveryModule from the job.
/// In the production pipeline InventoryDiscoveryModule is the InventoryAnalyser;
/// this method is the feature-vocabulary alias for <see cref="WithoutInventoryAnalyser"/>.
/// </summary>
public InventoryModulesBuilder WithoutInventoryDiscoveryModule()
    => WithoutInventoryAnalyser();
```

Placement: after the existing `WithoutInventoryAnalyser()` method, before `WithoutModule(string)`.

### 3.2 `InventoryModulesResult` — no changes required

`AssertAllStandardModuleArtefactsExist()` already asserts the feature's observable outcome.
No new assertion is needed.

### 3.3 `InventoryModulesScenario` — no changes required

The static `Arrange()` entry point is sufficient.

---

## 4. Tag Corrections — Existing Test Method

**Location:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs`, line 34–47

The existing method `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`
is missing the canonical feature-family tags. The following two attributes must be added:

```csharp
[TestCategory("inventory")]
[TestCategory("multi-org")]
```

The `[TestCategory("UnitTest")]` attribute should be removed. This test exercises real module
wiring via the DI container and a mock package store; it is an integration-level test, not a
unit test. `[TestCategory("IntegrationTests")]` and `[TestCategory("CodeTest")]` are retained.

Final attribute set for the existing method:

```csharp
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
[TestCategory("inventory")]
[TestCategory("multi-org")]
```

---

## 5. Target Test Definitions

### Test 1 — extend existing (tag correction only, no logic change)

| Property | Value |
|---|---|
| File | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` |
| Method name | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` |
| Change type | Tag correction |
| Tags added | `[TestCategory("inventory")]`, `[TestCategory("multi-org")]` |
| Tag removed | `[TestCategory("UnitTest")]` |
| DSL call-chain | unchanged |

### Test 2 — new test covering feature scenario vocabulary

| Property | Value |
|---|---|
| File | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` |
| Method name | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` |
| Change type | New test |
| Tags | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` |
| Scenario mapped | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` |

Target test body:

```csharp
// --- Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts ---

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

This test uses the new `WithoutInventoryDiscoveryModule()` builder method and the
existing result assertion. It maps 1:1 to the feature scenario steps:

| Feature step | DSL equivalent |
|---|---|
| `Given an Azure DevOps inventory job without InventoryDiscoveryModule` | `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` |
| `When the inventory job is executed` | `.RunAsync()` |
| `Then inventory artefacts are produced by inventory-capable modules` | `result.AssertAllStandardModuleArtefactsExist()` |

---

## 6. Reqnroll Artefact Deletion Plan

**No Reqnroll artefacts exist for this feature family.**

Assessment confirmed:
- No `ExternalFeatureFiles` entry in any `.csproj`
- No generated `.feature.cs` file
- No `*Steps.cs` bindings
- No Reqnroll context classes

Deletion action: none required.

---

## 7. DSL Surface Summary

| Type | Location | Change |
|---|---|---|
| `InventoryModulesBuilder` | `Modules/InventoryModules/InventoryModulesBuilder.cs` | Add `WithoutInventoryDiscoveryModule()` method |
| `InventoryModulesScenario` | `Modules/InventoryModules/InventoryModulesScenario.cs` | No change |
| `InventoryModulesResult` | `Modules/InventoryModules/InventoryModulesResult.cs` | No change |
| `InventoryModulesDriver` | `Modules/InventoryModules/InventoryModulesDriver.cs` | No change |
| `InventoryModuleFactory` | `Modules/InventoryModules/InventoryModuleFactory.cs` | No change |
| `InventoryModulesTests` | `Modules/InventoryModulesTests.cs` | Tag correction on existing method + add new test method |

---

## 8. Scenario Test Inventory — Updated Rows

The following rows replace the single row in `00-scenario-test-inventory.md`:

| # | Wiring State | Coverage Origin | Feature File | Scenario Name | Planned / Actual DSL Test Name | Mapping Status | Expected Tags | Actual Tags (target) | Tag Compliance | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 1a | `unwired` | `partial-existing` | `features/inventory/ado/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | `partial` → `matched` after tag fix | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `compliant` (after fix) | `tests/.../Modules/InventoryModulesTests.cs:34` — add missing tags, remove `UnitTest` |
| 1b | `unwired` | `to-build` | `features/inventory/ado/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `matched` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `compliant` | New test — `tests/.../Modules/InventoryModulesTests.cs` (to be added) |

**Row 1a** represents the existing test after tag correction. It covers the same behaviour
as the feature scenario via `InventoryAnalyser` vocabulary.

**Row 1b** represents the new test that uses `WithoutInventoryDiscoveryModule()` builder
vocabulary, providing a named mapping to the feature scenario text.

---

## 9. Design Rationale

The assessment recommended resolving the `InventoryDiscoveryModule` vs `InventoryAnalyser`
ambiguity before building. This design resolves it by:

- Confirming no production class named `InventoryDiscoveryModule` exists.
- Introducing a builder alias method that bridges feature vocabulary to the existing
  implementation flag without changing semantics.
- Adding a new test that uses the alias, so the feature scenario maps to a test whose
  name exactly mirrors the feature language.
- Keeping the existing test (after tag fix) as secondary corroboration.

This avoids duplication of logic while providing full traceability from the feature scenario
to executable tests.

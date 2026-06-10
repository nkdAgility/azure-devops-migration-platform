# Feature Assessment — inventory-multi-org

Feature file: `features/inventory/ado/inventory-multi-org.feature`
Assessment date: 2026-06-10

---

## 1. Scope

This assessment covers the single-scenario feature family `inventory-multi-org`, which exercises the constraint that removing the `InventoryDiscoveryModule` does not suppress artefact production from the remaining inventory-capable modules. The feature carries tags `@inventory @ado @multi-org`.

---

## 2. Wiring Classification

**Verdict: `unwired`**

| Evidence type | Finding |
|---|---|
| `ExternalFeatureFiles` entry in any `.csproj` | None found. Searched all `*.csproj` files under `tests/`. No reference to `inventory-multi-org.feature`. |
| Generated `.feature.cs` | None found in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Features/` (directory is empty). |
| `*Steps.cs` bindings | None found. No `.cs` file in any test project references `InventoryDiscoveryModule`, `multi-org`, or step text that matches this feature. |

The feature file exists on disk but is not listed in any test project's `ExternalFeatureFiles`, no `.feature.cs` has been generated, and no Reqnroll step bindings exist. The family has no executing baseline.

---

## 3. Pre-Existing Coverage Map

### Scenario: `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`

**Coverage origin: `partial-existing`**

Two related tests exist in the code-first MSTest corpus:

**Test A — closest match:**
- File: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:35`
- Method: `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`
- Tags: `[TestCategory("CodeTest")]`, `[TestCategory("IntegrationTests")]`, `[TestCategory("UnitTest")]`
- Assertion: confirms all four standard module artefacts (`WorkItems`, `Identities`, `Nodes`, `Teams`) are produced when `InventoryAnalyser` is excluded.
- Gap: The feature scenario names `InventoryDiscoveryModule`, not `InventoryAnalyser`. These may or may not be the same concept. Additionally the test is not tagged `inventory` or `multi-org`, making it invisible under feature-family tag filters.

**Test B — multi-org dispatch:**
- File: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerInventoryTests.cs:32–142`
- Methods: `InventoryDispatch_WithTwoSourceEndpoints_RecordsWorkItemInventoryTwice`, `InventoryDispatch_WithTwoSourceEndpoints_LogsOrgCountAndInvokesInventoryTwice`, `InventoryDispatch_WithTwoSourceEndpoints_EmitsPerOrgProgressWithCumulativeMetrics`, `InventoryDispatch_WhenSecondOrgFails_LogsWarningAndContinues`
- These tests cover multi-org dispatch behaviour broadly but do not specifically assert "artefacts are produced by inventory-capable modules when InventoryDiscoveryModule is absent."

**Decision:** Do not plan a duplicate. Plan to extend Test A to clarify whether `InventoryDiscoveryModule` ≡ `InventoryAnalyser`, add the missing `inventory` and `multi-org` test category tags, and if they are distinct, add a new test for the `InventoryDiscoveryModule`-specific removal case.

---

## 4. Behaviour Inventory

| # | Observable Behaviour | Source in Feature |
|---|---|---|
| B1 | When an inventory job runs without the `InventoryDiscoveryModule`, inventory artefacts are still produced by the modules that have inventory capability. | Scenario steps: Given/When/Then |

The feature asserts resilience/independence: removal of one module category (discovery) does not degrade output from the remaining inventory-capable modules.

---

## 5. Step Implementation Map

No `*Steps.cs` bindings exist for any step in this feature. The three step texts are:

| Step | Binding file | Method | Status |
|---|---|---|---|
| `Given an Azure DevOps inventory job without InventoryDiscoveryModule` | — | — | Missing |
| `When the inventory job is executed` | — | — | Missing |
| `Then inventory artefacts are produced by inventory-capable modules` | — | — | Missing |

Because the family is `unwired` and no bindings exist, all steps are intent-derived. The closest implementation reference is `InventoryModulesBuilder.WithoutInventoryAnalyser()` in `InventoryModulesBuilder.cs` and the execution harness in `InventoryModulesDriver` (not directly read, but called from the builder).

---

## 6. Context State Map

No Reqnroll context classes are associated with this feature. The equivalent code-first DSL uses:

| State concept | DSL equivalent | Location |
|---|---|---|
| "inventory job without InventoryDiscoveryModule" | `InventoryModulesScenario.Arrange().WithoutInventoryAnalyser()` | `InventoryModulesBuilder.cs` |
| "inventory job is executed" | `.RunAsync()` | `InventoryModulesBuilder.cs:53` |
| "inventory artefacts are produced" | `result.AssertAllStandardModuleArtefactsExist()` | `InventoryModulesResult.cs:51` |

---

## 7. Assertion Quality

**Existing Test A assertion:** `AssertAllStandardModuleArtefactsExist()` verifies `PersistIndexAsync` is called at least four times with a valid `PackageIndexContext`. This is a substantive, non-vacuous assertion — it observes package writes, not just absence of exceptions.

**Gap:** The assertion does not name individual modules; it uses `Times.AtLeast(4)`. If one of the four modules is silently dropped but another writes twice, the assertion would still pass. This is a minor weakness but acceptable for an integration-level test.

**Feature scenario intent:** "produced by inventory-capable modules" is satisfied by the existing assertion shape.

---

## 8. Proposed DSL Concepts

| Concept | Type | Notes |
|---|---|---|
| `InventoryJobWithout(string moduleName)` | Arrange builder method | Generalises `WithoutInventoryAnalyser()` to accept any module name, including `InventoryDiscoveryModule` if that is a distinct class |
| `InventoryArtefactsProducedByAllCapableModules()` | Assert method | Already exists as `AssertAllStandardModuleArtefactsExist()`; may need renaming to align with DSL vocabulary |
| `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | Tag | Missing from existing tests; needed for feature-family filter compliance |

---

## 9. Missing-Step Intent Backlog

| Item | Intent | Recommended Action |
|---|---|---|
| Clarify `InventoryDiscoveryModule` vs `InventoryAnalyser` | Determine whether the feature names a distinct production class | Search `src/` for a class named `InventoryDiscoveryModule`; if none exists the term is likely synonymous with `InventoryAnalyser` |
| Add missing tags to Test A | `[TestCategory("inventory")]`, `[TestCategory("multi-org")]` should be added to `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | Edit `InventoryModulesTests.cs:35` |
| Optionally new test if concepts differ | If `InventoryDiscoveryModule` is distinct, add `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` using the same builder pattern | New test in `InventoryModulesTests.cs` |

---

## 10. Migration Recommendation

**Classification:** `partial-existing` — do not build a net-new test until the `InventoryDiscoveryModule` vs `InventoryAnalyser` ambiguity is resolved.

**Recommended path:**

1. Search `src/` for a class or interface named `InventoryDiscoveryModule`. If it does not exist, confirm the feature term is synonymous with `InventoryAnalyser`.
2. Add `[TestCategory("inventory")]` and `[TestCategory("multi-org")]` to the existing test `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`.
3. If `InventoryDiscoveryModule` is a distinct concept, extend `InventoryModulesBuilder` with a `WithoutInventoryDiscoveryModule()` method and add a corresponding test method.
4. Do not migrate this feature to Reqnroll — the DSL-first test pattern is already in place and the Reqnroll framework is being retired.

**Risk:** Low. The artefact-production behaviour under module absence is already covered by an existing substantive test. The primary gap is tag compliance and naming clarity.

---

## 11. Scenario-to-Test Inventory Snapshot

See `00-scenario-test-inventory.md` for the full row-per-scenario table.

Summary:

| Scenario | Coverage Origin | Mapping Status |
|---|---|---|
| `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `partial-existing` | `partial` |

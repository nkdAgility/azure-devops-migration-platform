# Feature Assessment — inventory-multi-org (tfs)

## 1. Scope

- **Feature family:** `inventory-multi-org` (TFS connector variant)
- **Feature file:** `features/inventory/tfs/inventory-multi-org.feature`
- **Tags on feature:** `@inventory @tfs @multi-org`
- **Scenario count:** 1

---

## 2. Wiring Classification

**Verdict: `unwired`**

Evidence:
- No `ExternalFeatureFiles` entry references `features/inventory/tfs/inventory-multi-org.feature` in any `.csproj` in the repository (searched all `tests/**/*.csproj` and `tests/Directory.Build.props`).
- No `*.feature.cs` generated file exists for this feature.
- No `*Steps.cs` bindings exist that reference the step text from this feature file. A search for `InventoryDiscoveryModule`, `multi.*org`, `MultiOrg`, and related terms in `tests/**/*.cs` found only code-first MSTest tests — no Reqnroll step bindings.

**No executing baseline exists for this feature.**

---

## 3. Pre-Existing Coverage Map

### Scenario 1 — `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`

**Coverage origin: `pre-existing`**

An equivalent code-first MSTest test already exists at:

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:57`
  - Method: `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced`
  - Tags: `[TestCategory("CodeTest")]`, `[TestCategory("IntegrationTests")]`, `[TestCategory("inventory")]`, `[TestCategory("multi-org")]`

The test:
1. Arranges an inventory job via `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()`.
2. Guards that `InventoryAnalyserWasIncluded == false`.
3. Asserts `AssertAllStandardModuleArtefactsExist()` — verifying `PersistIndexAsync` was called at least 4 times for `inventory.json` entries.

This directly covers the feature scenario intent: running the inventory job without the `InventoryDiscoveryModule` still produces artefacts from all inventory-capable modules.

**Mapping status: `matched`**

No new test needs to be planned for this scenario.

**Note:** A near-duplicate test also exists at line 35 (`InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`) which uses the `WithoutInventoryAnalyser()` alias. Both share the same `[TestCategory("multi-org")]` tag and assert identical behaviour. This is not a gap — the two tests document that the two vocabulary aliases (feature-file vocabulary vs. production code vocabulary) both route to the same underlying behaviour. No new test is warranted.

---

## 4. Behaviour Inventory

| # | Scenario | Observable Behaviour |
|---|---|---|
| 1 | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | When the `InventoryDiscoveryModule` is absent from a TFS inventory job, all remaining inventory-capable modules still write their per-project artefacts (`inventory.json`) into the migration package. |

---

## 5. Step Implementation Map

| Step text | Step type | Implementation status | Notes |
|---|---|---|---|
| `a Team Foundation Server inventory job without InventoryDiscoveryModule` | Given | No Reqnroll step binding exists | Intent covered by `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` in the pre-existing code-first test |
| `the inventory job is executed` | When | No Reqnroll step binding exists | Intent covered by `.RunAsync()` in the pre-existing code-first test |
| `inventory artefacts are produced by inventory-capable modules` | Then | No Reqnroll step binding exists | Intent covered by `result.AssertAllStandardModuleArtefactsExist()` in the pre-existing code-first test |

**All three steps are unbound.** The feature is `unwired` with no `*Steps.cs` file. The behaviour is, however, fully covered by the pre-existing code-first test.

---

## 6. Context State Map

The pre-existing test infrastructure (code-first DSL) encapsulates state as follows:

| State element | Carrier | Notes |
|---|---|---|
| Module inclusion flags | `InventoryModulesBuilder` fields (`_includeWorkItems`, `_includeIdentities`, `_includeNodes`, `_includeTeams`, `_includeInventoryAnalyser`) | Configured via fluent builder methods |
| Mock package access | `Mock<IPackageAccess>` (captured in `InventoryModulesResult`) | Verifies `PersistIndexAsync` calls |
| Analyser inclusion guard | `InventoryModulesResult.InventoryAnalyserWasIncluded` | Boolean exposed for test-setup guard assertion |

---

## 7. Assertion Quality

The pre-existing test uses:

- A **guard assertion** (`Assert.IsFalse(result.InventoryAnalyserWasIncluded, ...)`) confirming the test setup precondition.
- A **behavioural assertion** (`AssertAllStandardModuleArtefactsExist`) that verifies `PersistIndexAsync` was called `Times.AtLeast(4)` with a non-null inventory index context.

**Quality rating: sound.** The assertion is non-vacuous — it verifies actual package writes, not just that no exception was thrown. The guard prevents false positives from setup failures.

Minor gap: the assertion counts total calls (≥ 4) rather than asserting one call per named module (WorkItems, Identities, Nodes, Teams). If a single module wrote 4 times the assertion would still pass. This is a known trade-off documented in the `InventoryModulesResult` XML comment. It does not warrant a new test, but could be a follow-up to strengthen the existing test.

---

## 8. Proposed DSL Concepts

No new DSL concepts are required. The scenario is covered by the pre-existing code-first test. If the test DSL library (`DevOpsMigrationPlatform.Testing.Dsl`) is extended in the future, the following concepts would be natural representations:

| Concept | Type | Notes |
|---|---|---|
| `InventoryJob.WithoutDiscoveryModule()` | Builder method | Already exists as `WithoutInventoryDiscoveryModule()` |
| `InventoryResult.AssertAllModuleArtefactsExist()` | Assertion | Already exists as `AssertAllStandardModuleArtefactsExist()` |

---

## 9. Missing-Step Intent Backlog

No backlog items. The single scenario is fully covered by a pre-existing test.

---

## 10. Migration Recommendation

**Action: retire — do not convert.**

The sole scenario in `features/inventory/tfs/inventory-multi-org.feature` is `pre-existing` (matched to `InventoryModulesTests.cs:57`). No new test should be built. The feature file should be retired (it has no `ExternalFeatureFiles` entry and no step bindings, so retirement requires only deleting the `.feature` file).

Before retiring, confirm that:
1. The feature tags (`@inventory @tfs @multi-org`) are already represented on the pre-existing test via `[TestCategory("inventory")]` and `[TestCategory("multi-org")]`. The `@tfs` tag has **no equivalent** `[TestCategory("tfs")]` on the matched test — the pre-existing test runs against the simulated connector, not a real TFS connector. This is a follow-up gap: the matched test should either gain `[TestCategory("tfs")]` if the simulated connector is considered TFS-equivalent, or a separate TFS integration test should be created if connector-specific coverage is required.
2. The near-duplicate scenario `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` (using the analyser alias, line 35) can be reviewed for consolidation.

**Retirement is safe pending the `@tfs` tag gap review.**

---

## 11. Scenario-to-Test Inventory Snapshot

See `00-scenario-test-inventory.md` for the full inventory table.

| Scenario | Wiring | Coverage | Mapping Status | Evidence |
|---|---|---|---|---|
| `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `unwired` | `pre-existing` | `matched` | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:57` |

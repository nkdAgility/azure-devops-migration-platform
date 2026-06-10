# Feature Assessment — inventory-multi-org

Feature file: `features/inventory/simulated/inventory-multi-org.feature`
Assessment date: 2026-06-10

---

## 1. Scope

This assessment covers the single-scenario feature family `inventory-multi-org`. The feature
exercises the constraint that removing the `InventoryDiscoveryModule` from an inventory job
does not suppress artefact production from the remaining inventory-capable modules. The feature
carries tags `@inventory @simulated @multi-org`.

---

## 2. Wiring Classification

**Verdict: `unwired`**

| Evidence type | Finding |
|---|---|
| `ExternalFeatureFiles` entry in any `.csproj` | None found. All `*.csproj` files under `tests/` were searched. No reference to `inventory-multi-org.feature`. |
| Generated `.feature.cs` adjacent to the feature | None found. |
| `*Steps.cs` bindings matching step text | None found. No `*Steps.cs` file references step text for this scenario. |

The feature file exists on disk but is not listed in any test project's `ExternalFeatureFiles`
item group. No Reqnroll code-behind (`.feature.cs`) has been generated and no step bindings
exist. The family has no executing Reqnroll baseline.

---

## 3. Pre-Existing Coverage Map

### Scenario: `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`

**Coverage origin: `pre-existing`**

A direct equivalent exists in the code-first MSTest corpus:

| Property | Value |
|---|---|
| File | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` |
| Line | 57 |
| Method | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` |
| Tags | `[TestCategory("CodeTest")]`, `[TestCategory("IntegrationTests")]`, `[TestCategory("inventory")]`, `[TestCategory("multi-org")]` |
| Assertion | `result.AssertAllStandardModuleArtefactsExist()` — verifies `PersistIndexAsync` is called at least four times with a valid `PackageIndexContext`, confirming all four standard modules (WorkItems, Identities, Nodes, Teams) produce inventory artefacts. |

A second related test also exists:

| Property | Value |
|---|---|
| File | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` |
| Line | 35 |
| Method | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` |
| Tags | `[TestCategory("CodeTest")]`, `[TestCategory("IntegrationTests")]`, `[TestCategory("inventory")]`, `[TestCategory("multi-org")]` |
| Notes | Uses the `WithoutInventoryAnalyser()` builder method directly; functionally identical to Row 1 above since `WithoutInventoryDiscoveryModule()` delegates to `WithoutInventoryAnalyser()`. |

**Decision:** No new test is required. The scenario maps to `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` which uses the feature vocabulary exactly. Mapping status: `matched`.

---

## 4. Behaviour Inventory

| # | Observable Behaviour | Source |
|---|---|---|
| B1 | An inventory job that runs without the `InventoryDiscoveryModule` still produces inventory artefacts from every inventory-capable data module. | Feature scenario steps (Given/When/Then) |

The feature asserts module independence: the presence or absence of the discovery/analyser
module does not degrade artefact output from the remaining modules (WorkItems, Identities,
Nodes, Teams).

---

## 5. Step Implementation Map

No `*Steps.cs` bindings exist. The three step texts and their code-first DSL equivalents are:

| Step | Binding | Method | Code-first DSL equivalent |
|---|---|---|---|
| `Given a simulated inventory job without InventoryDiscoveryModule` | None | None (missing) | `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` — `InventoryModulesBuilder.cs:37` |
| `When the inventory job is executed` | None | None (missing) | `.RunAsync()` — `InventoryModulesBuilder.cs:60` |
| `Then inventory artefacts are produced by inventory-capable modules` | None | None (missing) | `result.AssertAllStandardModuleArtefactsExist()` — `InventoryModulesResult.cs:51` |

The builder alias `WithoutInventoryDiscoveryModule()` at `InventoryModulesBuilder.cs:37`
delegates directly to `WithoutInventoryAnalyser()`, confirming the feature vocabulary
maps precisely to an existing builder path.

---

## 6. Context State Map

No Reqnroll context classes are associated with this family. The code-first DSL uses:

| State concept | DSL class / method | File |
|---|---|---|
| Job arranged without InventoryDiscoveryModule | `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` | `InventoryModulesBuilder.cs` |
| Job execution | `.RunAsync()` | `InventoryModulesBuilder.cs:60` |
| Artefact presence assertion | `result.AssertAllStandardModuleArtefactsExist()` | `InventoryModulesResult.cs:51` |
| Guard assertion (module genuinely absent) | `Assert.IsFalse(result.InventoryAnalyserWasIncluded, ...)` | `InventoryModulesTests.cs:65` |

The guard assertion at line 65 explicitly confirms the discovery module was absent before
asserting artefact presence, making this a well-structured integration test.

---

## 7. Assertion Quality

**Primary assertion** (`AssertAllStandardModuleArtefactsExist`): substantive. Verifies
`PersistIndexAsync` is called at least four times with a `PackageIndexContext` where
`FileName == "inventory.json"` and `Organisation` and `Project` are non-empty. This observes
real package writes rather than mere absence of exceptions.

**Guard assertion** (`Assert.IsFalse(result.InventoryAnalyserWasIncluded, ...)`): confirms
test setup validity before the primary assertion runs. This prevents false passes where a
misconfigured test never excluded the module.

**Minor weakness:** `Times.AtLeast(4)` does not pinpoint which specific module wrote which
artefact. A scenario where one module writes multiple times could mask a missing module write.
This is an acceptable trade-off at integration level.

**Overall quality:** Good. Non-vacuous, guarded, integration-level.

---

## 8. Proposed DSL Concepts

No new DSL concepts are required. All vocabulary is already present:

| Concept | Status | Location |
|---|---|---|
| `WithoutInventoryDiscoveryModule()` builder alias | Exists | `InventoryModulesBuilder.cs:37` |
| `AssertAllStandardModuleArtefactsExist()` | Exists | `InventoryModulesResult.cs:51` |
| `InventoryAnalyserWasIncluded` guard property | Exists | `InventoryModulesResult.cs:21` |
| `[TestCategory("inventory")]`, `[TestCategory("multi-org")]` tags | Present on matched test | `InventoryModulesTests.cs:53–54` |

---

## 9. Missing-Step Intent Backlog

No outstanding intent gaps. The scenario is fully covered by the existing test. The following
clarifications are recorded for completeness:

| Item | Status |
|---|---|
| `InventoryDiscoveryModule` vs `InventoryAnalyser` vocabulary alignment | Resolved — `InventoryModulesBuilder.cs:37` documents the alias relationship explicitly. |
| `@simulated` feature tag vs `@ado` naming discrepancy from earlier assessment versions | Corrected — actual feature file carries `@simulated`, not `@ado`. |

---

## 10. Migration Recommendation

**Classification:** `pre-existing` — no migration action required.

The scenario is fully covered by:
`tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:57`
(`InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced`)

All four required `TestCategory` tags are present. The assertion is substantive. The feature
file does not need to be wired to a Reqnroll test project; the Reqnroll framework is being
retired in favour of this code-first DSL pattern.

**Risk:** None. Behaviour is covered, tagged correctly, and guarded against test-setup errors.

---

## 11. Scenario-to-Test Inventory Snapshot

See `00-scenario-test-inventory.md` for the full row-per-scenario table.

Summary:

| Scenario | Coverage Origin | Mapping Status |
|---|---|---|
| `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `pre-existing` | `matched` |

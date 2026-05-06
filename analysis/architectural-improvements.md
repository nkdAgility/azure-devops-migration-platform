# Architectural Improvements — Deepening Opportunities

**Date:** May 4, 2026  
**Scope:** Full codebase exploration (src/DevOpsMigrationPlatform.*)  
**Objective:** Identify shallow modules, boilerplate patterns, and tightly-coupled abstractions that impede AI navigability and maintenance

---

## Executive Summary

Systematic exploration of the Azure DevOps Migration Platform codebase has identified **12 significant friction points** that create understanding friction and impede maintenance. The platform exhibits a well-intentioned modular structure (Module → Orchestrator → Service pattern), but shallow connector dispatchers, boilerplate-heavy module wrappers, and scattered responsibility create noise and obscure business logic.

This document presents **10 high-impact deepening candidates** — refactors that will:
- Consolidate shallow abstractions (eliminate 10+ identical Composite dispatcher classes)
- Reduce boilerplate (6 identical ~130-LOC module wrappers)
- Improve testability (isolate business logic from infrastructure)
- Enhance AI navigability (fewer files to search; clearer intent)

---

## 🔴 Critical Friction Points

### 1. Composite Dispatcher Pattern Proliferation — **MOST SEVERE**

**Status:** Shallow, pass-through, untested in isolation

**Scope:** 
- Infrastructure.Agent/Connectors/
- Infrastructure.Agent/Export/
- Infrastructure.Agent/Import/
- Infrastructure.Agent/Tools/NodeTranslation/

**Issue:**
- **10+ nearly-identical `Composite*` classes**: CompositeIdentitySource, CompositeTeamSource, CompositeTeamTarget, CompositeWorkItemRevisionSourceFactory, CompositeWorkItemImportTargetFactory, CompositeWorkItemResolutionStrategyFactory, CompositeNodeCreator, CompositeClassificationTreeReader, and more
- All follow identical pattern: accept `IEnumerable<Keyed*>` registrations → build lookup dictionary → `Resolve()` by endpoint discriminator → dispatch to concrete implementation
- No business logic; pure mechanical dispatch
- Each is ~40–80 LOC of identical boilerplate

**Real Friction:**
- Tracing a bug that manifests as "wrong implementation called" requires navigating 10 files that all look identical
- When one composite dispatcher behaves subtly differently (e.g., async vs. sync resolution), the difference is buried in similar-looking code
- Adding a new dispatched service requires copy-pasting the entire pattern from an existing example

**Why It Matters:**
- **AI navigability:** When a connector-specific method fails, the dispatcher layer adds a layer of indirection that obscures whether the bug is in registration, dispatch logic, or the connector itself
- **Testing:** Dispatchers are almost never tested directly; they're proven by integration tests of the concrete implementation. A dispatcher bug could silently pass through.

---

### 2. Shallow Module Wrapper Layer — **SEVERE**

**Status:** Thin, boilerplate-heavy, untested as isolated units

**Scope:** Infrastructure.Agent/Modules — all 6 module implementations:
- IdentitiesModule
- NodesModule
- TeamsModule
- WorkItemsModule
- InventoryModule
- DependencyDiscoveryModule

**Issue:**
- Every module: ~100–150 LOC following identical structure:
  1. Constructor: 8–12 parameters (logger, options, orchestrator, optional services); null-check and store
  2. `SupportsX` properties: hardcoded booleans
  3. Inventory/Export/Prepare/Import/Validate methods: guard check (enabled? service != null?) → emit progress → delegate **entire operation** to orchestrator
- No module-level business logic; purely dispatcher/wrapper
- Example from IdentitiesModule:
  ```csharp
  public async Task ExportAsync(ExportContext context, CancellationToken ct)
  {
      _logger.LogInformation("Exporting identities...");
      _progressSink?.Report(...);
      await _orchestrator.ExportAsync(context, ct);
  }
  ```

**Real Friction:**
- To understand what a module does, you **must read the orchestrator**. The module layer provides no value
- Tests of modules are vacuous: mock the orchestrator, method returns. No real logic tested.
- If a bug occurs in the module layer (e.g., wrong parameter passed to orchestrator), it's invisible because tests don't exercise the pass-through logic

**Why It Matters:**
- **Locality:** Business logic is not where the IModule contract suggests it should be. The module layer is a lie — it claims to implement the contract, but the contract is implemented in the orchestrator.
- **Testability:** Hard to write a meaningful unit test for a module; you're testing the orchestrator through a mock wrapper.

---

### 3. WorkItems Module Asymmetry — Export vs. Import Orchestrators

**Status:** Tightly-coupled, asymmetric design pattern

**Scope:** 
- WorkItemsModule
- WorkItemExportOrchestrator
- WorkItemImportOrchestrator

**Issue:**
- **Other modules:** One orchestrator per module (e.g., IIdentitiesOrchestrator handles export + import + validation)
- **WorkItems:** TWO separate orchestrators (WorkItemExportOrchestrator, WorkItemImportOrchestrator) bound into a single IModule
- WorkItemsModule constructor is massive; includes separate import-specific factories and tools. #if !NET481 guards add another layer of complexity
- Export path: calls one orchestrator; Import path: calls the other

**Real Friction:**
- WorkItemsModule is the "fat module" — understanding it requires mapping a large constructor parameter list to two separate orchestrators
- When import fails but export succeeds, it's unclear whether the bug is in the module, the export orchestrator, or the import orchestrator

**Why It Matters:**
- **Asymmetry signals debt:** Why do WorkItems need two orchestrators when Identities, Nodes, Teams use one? Suggests unfinished refactor or phase-specific complexity not yet generalized
- **Harder to reason about:** A developer reading the module sees import parameters but may not use export and vice versa

---

### 4. Tightly-Coupled RevisionFolderProcessor Constructor Hell

**Status:** High-cardinality dependencies, unclear usage per-phase

**Scope:** Infrastructure.Agent/Import/RevisionFolderProcessor.cs

**Issue:**
- Constructor: **12+ parameters**
  ```csharp
  public RevisionFolderProcessor(
      IWorkItemImportTarget target,
      IIdMapStore idMapStore,
      ICheckpointingService checkpointing,
      IIdentityLookupTool? identityLookupTool,
      IArtefactStore artefactStore,
      ILogger<RevisionFolderProcessor> logger,
      IMigrationMetrics? metrics,
      string? jobId,
      IFieldTransformTool? fieldTransformTool,
      INodeTranslationTool? nodeStructureTool,
      ProjectMapping? nodeStructureContext,
      NodeTranslationOptions? nodeStructureOptions)
  ```
- Implements 4-stage import loop (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments)
- Some parameters conditionally used in each stage (e.g., fieldTransformTool used only in AppliedFields stage)
- #if !NET481 guards suggest TFS agent has different path

**Real Friction:**
- Understanding parameter usage requires reading all 4 stage methods
- Optional parameters mask which services are always required vs. sometimes used
- If a service parameter is injected but unused in a code path, a mock test will never fail; bug surfaces in production

**Why It Matters:**
- **Cognitive load:** 12 parameters is at the threshold of human working memory. Adding one more breaks comprehension
- **Untestable isolation:** Each stage depends on a subset of parameters. Can't easily unit-test Stage B without Stage A setup.

---

### 5. Pass-Through No-Op Identity Service

**Status:** Purpose-obscuring, unchecked default behavior

**Scope:** Infrastructure.Agent/Import/PassThroughIdentityMappingService.cs

**Issue:**
- Entire implementation:
  ```csharp
  public sealed class PassThroughIdentityMappingService : IIdentityMappingService
  {
      public string Resolve(string sourceIdentity) => sourceIdentity;
      public void LoadMappingOverrides(string? mappingJson) { }
  }
  ```
- Comment: "Used during import when no identity mapping file is configured. Full identity resolution is added in US4/T031."
- Returns source identity unchanged; no warning, no logging

**Real Friction:**
- Silent default behavior: if identity mapping is misconfigured, the system won't warn. Identities are migrated unchanged.
- Placeholder comment suggests incomplete feature; unclear what the final behavior should be

**Why It Matters:**
- **Silent correctness:** A misconfigured identity mapping silently succeeds but produces wrong results. No observable signal.

---

### 6. Discovery/Analysis Module Pattern Duplication

**Status:** Duplication, inconsistent responsibility split, hard to generalize

**Scope:** 
- InventoryModule
- DependencyDiscoveryModule

**Issue:**
- InventoryModule: resolves connectorType in the module → looks up keyed InventoryService → passes to IInventoryOrchestrator
- DependencyDiscoveryModule: resolves organisations from context in the module → builds JobPolicies object → creates service → passes to IDependencyOrchestrator
- Inconsistent responsibility split: one leaves orchestrator to look up service; the other builds service + policies in module

**Real Friction:**
- Which pattern is correct? When adding a third discovery module, which should you follow?
- If they diverge, future contributors will guess and create yet another pattern

**Why It Matters:**
- **Generalization:** Hard to extract a reusable "discovery module template" from two inconsistent examples
- **Maintenance:** Bug in one may not be obvious in the other if the pattern isn't uniform

---

### 7. Factory Registration Boilerplate — Unmaintainable Extension Methods

**Status:** Repetitive, mechanical, no validation or collision detection

**Scope:** Infrastructure.Agent/Connectors/FactoryRegistrationExtensions.cs

**Issue:**
- 10+ methods: AddImportTargetFactory<T>, AddRevisionSourceFactory<T>, AddNodeCreator<T>, AddIdentitySource<T>, AddTeamSource<T>, AddTeamTarget<T>, AddClassificationTreeReader<T>, AddResolutionStrategyFactory<T1, T2>, ...
- All follow identical copy-paste pattern
- No validation of typeKey; no collision detection; no compile-time safety

**Real Friction:**
- Registering a new keyed service requires finding an example, copy-pasting, and manually updating the registration. Easy to get wrong (e.g., forget to register the Composite dispatcher)
- Different resolution strategies: some use IServiceProvider lazy resolution, others eager — inconsistency not documented

**Why It Matters:**
- **Extensibility:** Adding a tenth dispatcher service requires C&P from the ninth and guessing the pattern
- **Runtime errors:** No compile-time validation; wrong registration discovered only when the service is needed

---

### 8. Options Configuration — No Validation at DI Time

**Status:** Pass-through, runtime-resolved, untested in isolation

**Scope:** Module Options classes (IdentitiesModuleOptions, NodesModuleOptions, TeamsModuleOptions, WorkItemsModuleOptions, etc.)

**Issue:**
- Each module has its own Options class; all follow same pattern: sealed init-only properties, implement IModuleOptions, SectionName constant
- Modules inject `IOptions<T>` → resolve .Value → pass to orchestrator
- **No validation at DI registration time**; only used at runtime
- If a config option is misspelled, it silently reads default value; no error until module runs

**Real Friction:**
- Configuration bugs discovered late (mid-export)
- If a module depends on a critical option but uses a wrong default, the bug won't surface until that option path is taken

**Why It Matters:**
- **Fail-fast principle violated:** Config validation should happen at startup, not during job execution

---

### 9. Internal Sealed Orchestrators — Untestable, Tightly-Coupled to Storage

**Status:** Untestable in isolation, coupled to multiple abstractions

**Scope:** 
- IdentitiesOrchestrator.cs
- NodesOrchestrator.cs
- TeamsOrchestrator.cs
- DependencyOrchestrator.cs

**Issue:**
- All marked `internal sealed` — cannot be instantiated from test projects without InternalsVisibleTo
- Tightly coupled to IArtefactStore, IStateStore, IProgressSink, IMigrationMetrics
- Responsibility spans: checkpoint writing, progress emission, metrics recording, business logic
- Unit tests must mock all five abstractions

**Real Friction:**
- If a bug occurs in cursor-writing or progress-emission logic, it's buried inside the orchestrator
- Cursor bugs not caught by module tests (which mock orchestrator); only caught by integration tests

**Why It Matters:**
- **Testability:** Orchestrators implement the critical business logic but are nearly impossible to unit-test in isolation
- **Late bug discovery:** Integration tests catch bugs that unit tests should have caught

---

### 10. Identity Resolution Scattered Across Multiple Services

**Status:** Unclear responsibility, hard to trace failures

**Scope:** 
- IIdentityLookupTool
- PassThroughIdentityMappingService
- IIdentitySource
- RevisionFolderProcessor

**Issue:**
- **Three separate services, same responsibility:**
  - IIdentityLookupTool: resolves identity by UPN/display name against target (prepare phase)
  - PassThroughIdentityMappingService: returns source identity unchanged (import phase, if no mapping)
  - IIdentitySource: enumerates source identities (export phase)
  - RevisionFolderProcessor: applies identity lookup to field values (import phase)
- When identity resolution fails, unclear which service is to blame

**Real Friction:**
- Identity mapping is critical for correctness; distributed implementation makes bugs hard to localize
- Each service has its own rules for what "resolution" means

**Why It Matters:**
- **Traceability:** A wrong identity in the output could originate from any of 4 services; requires tracing through multiple modules

---

### 11. Options Lifetime Inconsistency — IOptions<T> vs. IOptionsMonitor<T>

**Status:** Leaky abstraction, runtime coupling, non-deterministic behavior

**Scope:** Service constructors across modules and tools

**Issue:**
- Some modules inject `IOptions<T>` (immutable snapshot at DI time)
- Some inject `IOptionsMonitor<T>` (live, re-reads config if changed)
- NodesOrchestrator injects IOptionsMonitor<NodeTranslationOptions>
- No documented reason why one vs. the other

**Real Friction:**
- If a module is instantiated scoped but options are registered singleton, behavior is non-deterministic
- No pattern to follow when adding a new service

**Why It Matters:**
- **Determinism:** Configuration behavior should be predictable; current pattern allows surprises

---

### 12. Team Orchestrators — Specialized Per-Item Pattern Not Generalized

**Status:** Tightly-coupled, specialized, hard to generalize

**Scope:** 
- TeamExportOrchestrator.cs
- TeamImportOrchestrator.cs
- TeamsOrchestrator.cs

**Issue:**
- TeamsOrchestrator enumerates teams → delegates each to TeamExportOrchestrator.ExportTeamAsync(...)
- Per-team orchestrator fetches settings, iterations, members, capacity, area paths (6+ operations)
- **Per-item orchestration pattern NOT used by other modules** (Identities, Nodes, WorkItems don't have IdentitiesPerIdentityOrchestrator, etc.)
- When a team operation fails, unclear whether bug is in enumeration loop or per-team orchestrator

**Real Friction:**
- Teams became special-cased; unclear why and when to generalize

**Why It Matters:**
- **Inconsistency signals incomplete refactor:** If another module needs per-item orchestration, should it follow teams or define a new pattern?

---

## Friction Point Summary Table

| Friction Type | Count | Severity | Impact |
|---|---|---|---|
| **Shallow dispatcher pattern** | 10+ Composite* classes | 🔴 Critical | AI navigation, testing, extension |
| **Module wrapper boilerplate** | 6 modules | 🔴 Critical | Locality, testability, understanding |
| **WorkItems asymmetry** | 1 special case | 🟠 High | Asymmetry signals debt |
| **RevisionFolderProcessor coupling** | 1 processor | 🟠 High | Cognitive load, testability |
| **Pass-through services** | 1 service | 🟠 High | Silent correctness, untested default |
| **Discovery module duplication** | 2 modules | 🟠 High | Generalization, consistency |
| **Factory registration boilerplate** | 10+ methods | 🟠 High | Extensibility, maintainability |
| **Options validation** | All modules | 🟠 High | Fail-fast principle violated |
| **Internal orchestrators** | 4 classes | 🟡 Medium | Testability, late bug discovery |
| **Identity resolution scatter** | 4 services | 🟡 Medium | Traceability, bug localization |
| **Options lifetime inconsistency** | Scattered | 🟡 Medium | Non-determinism, pattern clarity |
| **Team orchestrator specialization** | 2 classes | 🟡 Medium | Generalization, consistency |

---

## 🏗️ Deepening Opportunities

### Candidate 1: Collapse Composite Dispatcher Boilerplate 🔴 **CRITICAL**

**Files:**
- Infrastructure.Agent/Connectors/Composite*.cs (10+ implementations)
- Infrastructure.Agent/Connectors/FactoryRegistrationExtensions.cs (registration boilerplate)

**Problem:**
The platform has 10+ nearly-identical "Composite" dispatcher classes:
- CompositeIdentitySource
- CompositeTeamSource
- CompositeTeamTarget
- CompositeWorkItemRevisionSourceFactory
- CompositeWorkItemImportTargetFactory
- CompositeNodeCreator
- CompositeClassificationTreeReader
- ... and more

Each follows the same pattern: `Dictionary<string, Keyed*>` lookup → dispatcher by endpoint key. **Zero business logic; 100% mechanical boilerplate.** Each ~40–80 LOC, nearly identical.

**Solution:**
Create a single generic `CompositeServiceDispatcher<TInterface>` base class or use source generators to eliminate the need for hand-written dispatchers entirely.

**Benefits:**
- **Leverage:** All dispatcher bugs fixed in one place instead of ten
- **Locality:** Extension points collapse from 10 files to 1 or 0
- **Testability:** Generic dispatcher can be unit-tested once
- **AI navigation:** Tracing through a bug no longer requires visiting 10 identical files

**Note on net481 Compatibility:** This candidate requires a hybrid approach for .NET 4.8.1 support (TFS Migration Agent):
- Option A: Generic dispatcher base class with conditional compilation for keyed service discovery
- Option B: Source generators to auto-generate dispatcher boilerplate — **requires SDK-style `.csproj` projects**; the generator project must target `netstandard2.0`; minimum toolchain: `Microsoft.CodeAnalysis.CSharp >= 3.8.0` and Visual Studio 2019 v16.8+ (or equivalent MSBuild with Roslyn source-generator support). Verify build environment before choosing this option.

---

### Candidate 2: Eliminate Shallow Module Wrapper Layer 🔴 **CRITICAL**

**Files:**
- Infrastructure.Agent/Modules/IdentitiesModule.cs (~140 LOC)
- Infrastructure.Agent/Modules/NodesModule.cs (~140 LOC)
- Infrastructure.Agent/Modules/TeamsModule.cs (~160 LOC)
- Infrastructure.Agent/Modules/WorkItemsModule.cs (~180 LOC)
- Infrastructure.Agent/Modules/InventoryModule.cs (~130 LOC)
- Infrastructure.Agent/Modules/DependencyDiscoveryModule.cs (~130 LOC)

**Problem:**
Every module is **essentially empty**. Typical flow:
1. Constructor: 8–12 parameters; null-checks
2. Property: `SupportsExport => true` (hardcoded boolean)
3. Method: `ExportAsync(context, ct)` → guard check → emit progress → **delegate entire operation to orchestrator**

The module layer is a pass-through. To understand what a module does, you must skip the module entirely and read the orchestrator.

**Solution:**
Preserve the formal `IModule` phase contract (`SupportsInventory`, `SupportsExport`, `SupportsPrepare`, `SupportsImport`, `SupportsValidate`) and reduce wrapper boilerplate by introducing a shared `ModuleBase` or composition helper that handles common orchestrator wiring. Alternatively, elevate reusable orchestrator pieces into the base class so the module layer remains thin but still exposes an explicit, navigable contract. Removing the `IModule` interface entirely is not recommended — it would eliminate the extensibility contract and break capability-flag routing.

**Benefits:**
- **Locality:** Module is the place where module logic lives
- **Testability:** Module tests actually test the module, not mocked orchestrators
- **Clarity:** Reading an IModule implementation answers: "What does this module do?"
- **Reduced indirection:** 6 ~140-LOC files collapsed into registration logic or thin wrappers

---

### Candidate 3: Generalize Per-Item Orchestration 🟠 **HIGH**

**Files:**
- Infrastructure.Agent/Teams/TeamsOrchestrator.cs
- Infrastructure.Agent/Teams/TeamExportOrchestrator.cs
- Infrastructure.Agent/Teams/TeamImportOrchestrator.cs
- Infrastructure.Agent/Modules/TeamsModule.cs

**Problem:**
Teams uses a **per-item orchestration pattern**:
- TeamsOrchestrator enumerates all teams
- For each team, delegates to TeamExportOrchestrator.ExportTeamAsync(...)
- Each call fetches team settings, iterations, members, capacity, area paths

**Other modules do NOT use this pattern.** When a module needs item-level orchestration, should it follow Teams or invent a new pattern?

**Solution:**
Extract a **generic per-item orchestration pattern** — an interface like `IItemOrchestrator<TItem>` with methods like `ProcessAsync(TItem, context, ct)`.

**Benefits:**
- **Leverage:** Future modules can inherit the pattern without guessing
- **Locality:** Per-item logic is grouped in one place
- **Consistency:** All modules that need it use the same abstraction
- **Testability:** Per-item orchestrators can be unit-tested with mock items

---

### Candidate 4: Extract RevisionFolderProcessor Context 🟠 **HIGH**

**Files:**
- Infrastructure.Agent/Import/RevisionFolderProcessor.cs

**Problem:**
RevisionFolderProcessor constructor: **12+ parameters**, some optional, used conditionally. The processor has 4 stages, each using a **different subset of parameters**. Optional parameters mask which services are always required vs. stage-specific.

**Solution:**
Group parameters into **domain value objects**:
- `RevisionProcessorTools` — IArtefactStore, IStateStore, ILogger (always needed)
- `IdentityResolutionContext` — identityLookupTool, idMapStore (field mapping stage)
- `FieldTransformationContext` — fieldTransformTool, nodeStructureTool, etc. (fields stage)
- `AttachmentUploadContext` — target, checkpointing, metrics (attachment stage)

Constructor becomes: `public RevisionFolderProcessor(RevisionProcessorTools tools, ...Context? ...Context)`

**Benefits:**
- **Cognitive load:** 4 parameters instead of 12
- **Testability:** Test Field transformation without mocking identity/attachment context
- **Locality:** Stage-specific dependencies grouped with the code that uses them
- **Clarity:** Constructor immediately shows the four concerns

---

### Candidate 5: Centralize Identity Resolution 🟠 **HIGH**

**Files:**
- Infrastructure.Agent/Identity/IdentityLookupTool.cs
- Infrastructure.Agent/Import/PassThroughIdentityMappingService.cs
- Infrastructure.Agent/Import/RevisionFolderProcessor.cs
- Abstractions.Agent/Services/IIdentitySource.cs

**Problem:**
Identity resolution is scattered across four services with overlapping semantics:

| Service | Responsibility | Used When |
|---|---|---|
| IIdentityLookupTool | Resolve identity by UPN/displayName against target | Prepare phase |
| PassThroughIdentityMappingService | Return source identity unchanged | Import phase, if no mapping configured |
| IIdentitySource | Enumerate source identities | Export phase |
| RevisionFolderProcessor | Apply identity lookup to field values during revision import | Import phase |

When an identity fails to resolve, unclear which service is responsible.

**Solution:**
Create a single **`IIdentityResolutionPipeline`** abstraction that encapsulates all identity operations:

```csharp
public interface IIdentityResolutionPipeline
{
    IAsyncEnumerable<Identity> EnumerateSourceAsync(CancellationToken ct);  // Export
    Identity LookupTargetAsync(Identity sourceIdentity, CancellationToken ct);  // Prepare
    string ResolveFieldValue(string sourceIdentity);  // Import
    void LoadMappingOverrides(string? mappingJson);
}
```

**Benefits:**
- **Traceability:** A bug in identity resolution has one file to investigate
- **Locality:** All identity logic grouped in one place
- **Testability:** Single abstraction to mock; behavior is deterministic
- **Clarity:** Contract clearly states the resolution phases

---

### Candidate 6: Unify Module-Orchestrator Asymmetry 🟠 **HIGH**

**Files:**
- Infrastructure.Agent/Modules/WorkItemsModule.cs
- Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs
- Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs
- Infrastructure.Agent/Modules/IdentitiesModule.cs (contrasting pattern)

**Problem:**
WorkItems is unique: it has **two separate orchestrators** (Export + Import) bound into one IModule. All other modules have one orchestrator per module.

Asymmetry signals **debt**: Why do WorkItems need two orchestrators when Identities use one?

**Solution:**
Investigate why WorkItems requires two orchestrators. Options:
1. Extract `IWorkItemExportModule` and `IWorkItemImportModule` as separate IModule implementations
2. Unify into a single `WorkItemOrchestrator` that handles all phases
3. Document in an ADR why two orchestrators are required and establish the pattern

**Benefits:**
- **Clarity:** Module structure matches other modules
- **Extensibility:** If another module needs a second orchestrator, there's a documented pattern
- **Locality:** Module file size and constructor complexity reduced

---

### Candidate 7: Eliminate Factory Registration Boilerplate 🟡 **MEDIUM**

**Files:**
- Infrastructure.Agent/Connectors/FactoryRegistrationExtensions.cs (10+ extension methods)

**Problem:**
Every connector-specific factory registration requires a hand-written extension method. This is ~20 LOC repeated 10+ times. No validation of `typeKey`; no collision detection; easy to get wrong.

**Solution:**
Use **source generators** to auto-generate all 10 extension methods from a single template. Or use **keyed service discovery** via reflection + DI conventions (for .NET 10+; would require conditional compilation for net481).

**Benefits:**
- **Maintainability:** Adding a new connector requires registering one class; boilerplate auto-generated
- **Compile-time safety:** Source gen produces type-safe registration
- **Consistency:** All registrations follow same pattern

---

### Candidate 8: Add Options Validation at DI Time 🟡 **MEDIUM**

**Files:**
- All module options classes (IdentitiesModuleOptions, NodesModuleOptions, etc.)
- MigrationPlatformHost.cs (DI setup)

**Problem:**
Module options are validated **at runtime** when first used, not at DI registration. If a config option is misspelled, it silently reads a default value; bug surfaces mid-export.

**Solution:**
Add **options validators** registered at DI setup time using `IValidateOptions<T>` pattern:

```csharp
public class IdentitiesModuleOptionsValidator : IValidateOptions<IdentitiesModuleOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentitiesModuleOptions options)
    {
        if (options.BatchSize <= 0)
            return ValidateOptionsResult.Fail("BatchSize must be > 0");
        return ValidateOptionsResult.Success;
    }
}
```

**Benefits:**
- **Fail-fast:** Config bugs discovered when the app starts, not mid-migration
- **Observability:** Clear error message in logs at startup
- **Testability:** Config validation tested independently

---

### Candidate 9: Clarify Options Lifetime Policy 🟡 **MEDIUM**

**Files:**
- Service constructors across Infrastructure.Agent/* (scattered)

**Problem:**
Some services inject `IOptions<T>` (immutable snapshot); others inject `IOptionsMonitor<T>` (live config). No documented reason; inconsistency makes new services guess.

**Solution:**
Establish a single documented policy:
- **Use `IOptions<T>` by default** (immutable snapshot; deterministic behavior)
- **Use `IOptionsMonitor<T>` only if** the service needs to re-read config mid-execution

Document in `/docs/configuration-reference.md` or a new `/docs/options-lifetime-policy.md`.

**Benefits:**
- **Determinism:** Behavior is predictable
- **Pattern clarity:** New contributors know which to use
- **Testability:** Deterministic options make tests reproducible

---

### Candidate 10: Resolve Discovery Module Duplication 🟡 **MEDIUM**

**Files:**
- Infrastructure.Agent/Modules/InventoryModule.cs
- Infrastructure.Agent/Modules/DependencyDiscoveryModule.cs

**Problem:**
Two discovery modules exist with **inconsistent responsibility split**:

| Module | Pattern |
|---|---|
| InventoryModule | Resolve connector type in module → look up keyed InventoryService → pass to IInventoryOrchestrator |
| DependencyDiscoveryModule | Resolve organizations in module → build JobPolicies → create service → pass to IDependencyOrchestrator |

When adding a third discovery module, which pattern to follow?

**Solution:**
Define a single **Discovery Module Template** and refactor both modules to follow it:
1. Module resolves connector type
2. Module looks up keyed discovery service
3. Module delegates to orchestrator with service injected
4. Orchestrator handles enumeration/checkpointing/progress

**Benefits:**
- **Generalization:** Template for future discovery modules
- **Consistency:** Both modules behave identically
- **Maintainability:** Bug in one is immediately visible in the other

---

## Next Steps

These deepening opportunities are presented for architectural consideration and prioritization. Each candidate:
- Addresses a specific friction point or group of related issues
- Improves testability, maintainability, or AI navigability
- Can be pursued independently or as part of a coordinated refactoring initiative

**Recommended sequence for maximum impact:**
1. **Candidate 1** — Collapse Composite Dispatcher (eliminates 10+ identical files)
2. **Candidate 2** — Eliminate Shallow Module Wrapper (clarifies module responsibility)
3. **Candidate 5** — Centralize Identity Resolution (critical domain concept)

Alternatively, focus on candidates that support your current development priorities or address the highest-friction areas in your maintenance workflow.

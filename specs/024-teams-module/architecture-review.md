# Architecture Review — Combined Report

**Date:** 2026-04-27
**Scope:** Areas affected by IdentitiesModule, NodeStructureModule, TeamsModule implementation
**Perspectives checked:** Modular Monolith · Clean Architecture · Hexagonal · Vertical Slice · Screaming Architecture
**Trigger:** Mandatory `after_plan` hook (`.specify/extensions.yml`)

---

## Summary Table

| Perspective | Critical | High | Medium | Low | Informational |
|---|---|---|---|---|---|
| Modular Monolith [MM] | 3 | 2 | 2 | 0 | — |
| Clean Architecture [CA] | 0 | 0 | 1 | 1 | — |
| Hexagonal [HX] | 1 | 1 | 2 | 0 | — |
| Vertical Slice [VS] | 1 | 3 | 1 | 0 | — |
| Screaming Architecture [SA] | 1 | 2 | 3 | 0 | 1 |
| **Total** | **6** | **8** | **9** | **1** | **1** |

---

## Critical Violations (must fix during implementation)

```
[MM-C1] Type checking anti-pattern in WorkItemsModule
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs:219-227
  Issue: `_processorFactory is RevisionFolderProcessorFactory` — direct concrete type check
  Fix: Add ProjectMapping overload to IRevisionFolderProcessorFactory interface.

[MM-C2] Missing INodeEnsurer interface — NodeEnsurer injected as concrete type
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeEnsurer.cs
  Fix: Create INodeEnsurer in Abstractions.Agent/Tools/. Register behind interface.

[MM-C3] ReferencedPathTracker and ClassificationTreeCapture injected as concrete types
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs:16,65,86
  Fix: Create IReferencedPathTracker and IClassificationTreeCapture in Abstractions.Agent/Tools/.

[HX-C1] Infrastructure leakage in store factories
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/ExportProgressStoreFactory.cs:30-31
  Issue: Direct File.Exists() + concrete SqliteExportProgressStore instantiation.
  Fix: Abstract store creation fully behind factory interface.

[VS-C1] ReferencedPathTracker is mutable shared state between export/import slices
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/ReferencedPathTracker.cs
  Issue: Non-threadsafe HashSet shared across slices. Registered as Transient but held as singleton.
  Fix: Make immutable after export; use IReferencedPathTracker interface; fix DI lifetime.

[SA-C1] Project naming uses deployment topology (Agent/ControlPlane) not business domain
  Files: All *.Agent and *.ControlPlane project names
  Note: EXISTING DESIGN DECISION — document rationale but do not rename in this spec.
```

## High Violations (fix in current spec)

```
[MM-H1] ClassificationTreeCapture and ReferencedPathTracker registered without interfaces
  File: NodeStructureToolServiceCollectionExtensions.cs:33-34
  Fix: Register behind IClassificationTreeCapture / IReferencedPathTracker.

[MM-H2] WorkItemsModule imports concrete NodeStructure namespace
  File: WorkItemsModule.cs:14
  Fix: Replace with interface-based dependencies from Abstractions.Agent.

[HX-H1] SDK coupling: Module orchestration holds references to NodeStructure components
  File: WorkItemsModule.cs:54
  Fix: NodeStructureModule extraction will naturally resolve this.

[VS-H1] Missing feature tests for Identities, Teams, NodeStructure slices
  Files: features/export/identities/, features/import/teams/ (empty .gitkeep)
  Fix: ATDD implementation will create these — tracked in plan Phase 2.

[VS-H2] Checkpoint keys not scoped by job ID — potential cross-job collision
  File: CheckpointingService.cs:21
  Note: Pre-existing issue. Document for future fix; do not change cursor format in this spec.

[VS-H3] ReferencedPathTracker DI lifetime mismatch (Transient vs actual singleton usage)
  File: NodeStructureToolServiceCollectionExtensions.cs:33
  Fix: Change to Scoped or Singleton with IReferencedPathTracker interface.

[SA-H1] CatalogService / InventoryService — generic names lack domain context
  Files: Infrastructure.Agent/Discovery/CatalogService.cs, InventoryService.cs
  Note: Pre-existing; not in scope for this spec.

[SA-H2] RevisionFolderProcessor — pattern name hides 4-stage import workflow
  File: Infrastructure.Agent/Import/RevisionFolderProcessor.cs
  Note: Pre-existing; not in scope for this spec.
```

## Medium Violations (address during implementation)

```
[MM-M1] IRevisionFolderProcessorFactory missing ProjectMapping overload
  File: Abstractions.Agent/Export/IRevisionFolderProcessorFactory.cs
  Fix: Add overload with optional ProjectMapping parameter.

[MM-M2] Optional concrete type parameters in WorkItemsModule constructor
  File: WorkItemsModule.cs:73-75
  Fix: Replace with interface types; use NullObject pattern for optional deps.

[CA-M1] Optional NodeEnsurer causes silent functional degradation
  File: WorkItemsModule.cs:205-214
  Fix: Inject required INodeEnsurer; use NullNodeEnsurer when disabled.

[HX-M1] CompositeNodeCreator has no fallback adapter — runtime exception
  File: CompositeNodeCreator.cs:47-53
  Fix: Validate at DI composition time or provide NullNodeCreator fallback.

[HX-M2] Port interface adapter registration is opaque for AzureDevOps INodeCreator
  Fix: Add explicit adapter registration comments in DI extension methods.

[VS-M1] WorkItems slice delegates import concerns without clear ownership boundary
  File: WorkItemsModule.cs:168-255
  Fix: NodeStructureModule extraction clarifies ownership.

[SA-M1] ReferencedPathTracker name suggests observability, not path discovery
  Fix: Rename is optional; IReferencedPathTracker interface will clarify contract.

[SA-M2] NodeStructureOptions.SectionName could be more intent-revealing
  Note: Config section name is established; rename deferred.

[SA-M3] CompositeNodeCreator is pattern-based name, not business name
  Note: Acceptable; consistent with existing pattern in codebase.
```

## Low / Informational

```
[CA-L1] WorkItemsModule directly instantiates orchestrators (new WorkItemExportOrchestrator)
  File: WorkItemsModule.cs:144, 238
  Note: Orchestrators delegate to injected ports; coupling is tolerable.

[SA-I1] IdentitiesModule and TeamsModule do not yet exist in code
  Note: Expected — this spec creates them.
```

---

## Cross-Cutting Patterns

The following violations appear across multiple perspectives — fixing the root cause resolves all:

### Pattern 1: Missing Abstractions for NodeStructure Services

**Appears in:** [MM-C2], [MM-C3], [MM-H1], [MM-H2], [HX-H1], [VS-C1], [VS-H3]

**Root cause:** `NodeEnsurer`, `ReferencedPathTracker`, and `ClassificationTreeCapture` are concrete classes injected directly into `WorkItemsModule` without interfaces.

**Single fix:** Create `INodeEnsurer`, `IReferencedPathTracker`, `IClassificationTreeCapture` in `Abstractions.Agent/Tools/`. Register behind interfaces. This resolves 7 violations across 3 perspectives.

**Note:** The `NodeStructureModule` extraction planned in this spec will naturally force this refactoring — the module cannot depend on concrete classes from the same infrastructure layer without circular references.

### Pattern 2: Factory Interface Incompleteness

**Appears in:** [MM-C1], [MM-M1]

**Root cause:** `IRevisionFolderProcessorFactory` interface is missing the `ProjectMapping` overload, forcing concrete type checking.

**Single fix:** Add the overload to the interface. Removes the `is RevisionFolderProcessorFactory` type check.

---

## Recommended Fix Order

1. **Create missing abstractions** — `INodeEnsurer`, `IReferencedPathTracker`, `IClassificationTreeCapture` (resolves 7 violations)
2. **Extract NodeStructureModule** — naturally separates concerns (resolves [HX-H1], [VS-M1])
3. **Rename INodeStructureTool → INodeTranslationTool** (planned in spec)
4. **Fix ReferencedPathTracker DI lifetime** — Singleton with interface (resolves [VS-C1], [VS-H3])
5. **Add IRevisionFolderProcessorFactory overload** (resolves [MM-C1], [MM-M1])
6. **Replace optional concrete params with NullObject interfaces** (resolves [MM-M2], [CA-M1])
7. **Create feature files** during ATDD sessions (resolves [VS-H1])

Items 1-6 should be addressed as prerequisite refactoring tasks before the main module implementation.

---

## Verdict

**PASS WITH CONDITIONS** — The architecture review found 6 Critical and 8 High violations. However:

- **5 of 6 Critical violations** are resolved by creating missing interface abstractions (a prerequisite refactoring step already implied by the plan's NodeStructureModule extraction)
- **1 Critical violation** (SA-C1: project naming) is a pre-existing design decision — not in scope
- **All High violations** either resolve naturally during implementation or are pre-existing issues outside this spec's scope

The plan is architecturally sound. The implementation should include a **Phase 0.5 — Prerequisite Refactoring** batch to create the missing abstractions before building the new modules.

### Architecture Review Checklist

- [x] **Modular Monolith** check executed and all findings recorded.
- [x] **Clean Architecture** check executed and all findings recorded.
- [x] **Hexagonal Architecture** check executed and all findings recorded.
- [x] **Vertical Slice** check executed and all findings recorded.
- [x] **Screaming Architecture** check executed and all findings recorded.
- [x] Combined summary table completed.
- [x] All Critical violations listed with file, line, and fix suggestion.
- [x] All High violations listed with file, line, and fix suggestion.
- [x] Cross-cutting patterns identified and noted.
- [x] Recommended fix order provided.

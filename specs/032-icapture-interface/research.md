# Research: ICapture Interface — Unified Capture Contract

**Feature**: `032-icapture-interface`  
**Phase**: 0 — Research  
**Status**: Complete — no NEEDS CLARIFICATION items. Spec is fully specified.

---

## Summary

This is a pure internal refactor. The spec is exhaustive: all design decisions, interface
contracts, class names, file paths, metric names, span names, DI registration patterns, and
edge-case behaviours are explicitly specified. No external research tasks are required.

The following documents the rationale for each technical decision already encoded in the spec,
for future reference.

---

## Decision Log

### D-001: ICapture vs IModule as the Capture Dispatch Type

**Decision**: Introduce `ICapture` as a new, minimal base interface; `IModule` extends it.

**Rationale**: `IModule` carries 6 phase properties (`SupportsExport`, `SupportsImport`, etc.)
and 5 phase methods that are irrelevant to the `JobPlanExecutor`'s `TaskKind.Capture` dispatch
path. Using `IModule` as the dispatch type forces the executor to receive the full module surface
area when it needs only `Name` and `CaptureAsync`. `ICapture` is the minimal contract that
satisfies the executor's needs and enables non-module capture handlers (e.g. `DependencyCapture`)
to be registered without implementing unused module concerns.

**Alternatives considered**:
1. *Keep `modulesByName: IReadOnlyDictionary<string, IModule>` and add special-case branching
   for non-module capture handlers* — rejected; this is exactly the `IProjectAnalyser` hack
   being removed. Each new capture type would require another branch.
2. *Replace `IModule` with `ICapture` entirely (flatten interfaces)* — rejected; `IModule` carries
   meaningful export/import/validate phase semantics. `SupportsInventory` filtering on `IModule`
   remains essential for plan building.

---

### D-002: ICapture.Name Routing Convention

**Decision**: `ICapture.Name` maps to `parts[1]` of the task ID when split on `'.'`.
(e.g. `capture.workitems.{org}.{project}` → `Name = "workitems"`).

**Rationale**: The existing `GetModuleName` helper in `JobPlanExecutor` already implements
this split-on-dot-take-index-1 pattern. `ICapture.Name` adopts the same convention rather
than introducing a separate naming scheme, making the routing rule uniform across all task kinds.

**Alternatives considered**:
1. *Use full task-type prefix (e.g. `Name = "capture.workitems"`)* — rejected; inconsistent with
   existing module naming (`WorkItemsModule.Name = "WorkItems"`).

---

### D-003: DependencyCapture Placement

**Decision**: `DependencyCapture` lives in `DevOpsMigrationPlatform.Infrastructure.Agent/Capture/`.

**Rationale**: `DependencyCapture` is not a module (no export/import phases); placing it under
`Modules/` would be misleading. `Capture/` is a new, purpose-named subfolder that clearly
communicates intent (Screaming Architecture principle). It is parallel to `Analysis/` which
holds `DependencyAnalyser`, keeping discovery-phase classes co-located by type.

**Alternatives considered**:
1. *Place under `Analysis/`* — rejected; `Analysis/` is for fan-in analysers, not per-project
   capture operations. `DependencyAnalyser` is an `IOrganisationsAnalyser`, not an `ICapture`.

---

### D-004: SimulatedDependencyDiscoveryServiceFactory Implementation Strategy

**Decision**: Wrap the already-registered keyed `SimulatedWorkItemLinkAnalysisService` (keyed
`"Simulated"`) inside a new `SimulatedDependencyDiscoveryServiceFactory` that implements
`IDependencyDiscoveryServiceFactory`. Both `Create` and `CreateForProject` delegate to the
same underlying `SimulatedWorkItemLinkAnalysisService` (the simulated service returns empty
or deterministic link results regardless of scope).

**Rationale**: `AddSimulatedDependencyAnalysis` already registers
`SimulatedWorkItemLinkAnalysisService` as a keyed singleton. The factory wrapper is the
minimal addition needed to satisfy `IDependencyDiscoveryServiceFactory` without duplicating
the simulated link logic.

**Alternatives considered**:
1. *Create a new `SimulatedDependencyDiscoveryService` from scratch* — rejected; unnecessary
   duplication of the simulated link analysis service already in place.

---

### D-005: IProjectAnalyser Deletion

**Decision**: Delete `IProjectAnalyser` entirely rather than deprecate it.

**Rationale**: `IProjectAnalyser` was a temporary workaround introduced to allow
`DependencyAnalyser` to handle per-project capture without an `ICapture` abstraction.
After `ICapture` is introduced and `DependencyCapture` is created, `IProjectAnalyser` has
zero callers. Keeping it would violate ISP and confuse future contributors.

**Alternatives considered**:
1. *Mark `[Obsolete]`* — rejected; the type is internal to the solution and has no external
   consumers. Deletion is cleaner than a zombie interface.

---

### D-006: JobAgentWorker captureHandlersByName Assembly

**Decision**: `JobAgentWorker` builds `captureHandlersByName` by:
1. Taking all `IModule` instances where `SupportsInventory = true` → cast to `ICapture`
2. Union with all `ICapture`-only registrations resolved via
   `serviceProvider.GetServices<ICapture>()`, excluding those already added from step 1
   (to prevent double-registration of modules that implement both `IModule` and `ICapture`)

**Rationale**: This mirrors the existing `modulesByName` construction pattern but widens
the source to include pure `ICapture` instances. The exclusion step prevents `WorkItemsModule`
(which is both `IModule : ICapture`) from appearing twice if it is registered as both.

**Alternatives considered**:
1. *Register all ICapture-only types as `IModule`* — rejected; forces `DependencyCapture` to
   implement irrelevant phase methods and contradicts FR-013.
2. *Use a separate keyed registration (e.g. keyed `"capture"`)* — rejected; adds DI ceremony
   with no practical benefit for the current scale.

---

### D-007: TfsMigrationAgent Scope

**Decision**: The TFS agent does NOT register `DependencyCapture` and emits no
`capture.dependencies.*` tasks for `source.type = TeamFoundationServer`.

**Rationale**: The TFS Object Model (`WorkItemStore`) does not expose the same cross-project
dependency analysis API surface that `IDependencyDiscoveryService` abstracts. A
TFS-native implementation is explicitly deferred (not in this spec scope). The TFS plan
builder is guarded to emit no `capture.dependencies.*` tasks. If such a task is erroneously
present, the executor logs a structured `Warning` and skips it gracefully.

**Evidence**: The TFS connector does not have an `IDependencyDiscoveryServiceFactory`
implementation. Creating one requires significant TFS OM API investigation and is a
separate work item.

---

## No Open Questions

All implementation decisions are fully specified. Proceed to Phase 1 design.

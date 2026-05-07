# Data Model: ICapture Interface — Unified Capture Contract

**Feature**: `032-icapture-interface`  
**Phase**: 1 — Design  
**Status**: Complete

---

## Entities

### 1. `ICapture` (New Interface)

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Modules`  
**File**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/ICapture.cs`

```csharp
public interface ICapture
{
    /// <summary>
    /// Unique handler name. Maps to the second dot-separated segment of the
    /// capture task ID: e.g. "workitems" for "capture.workitems.{org}.{project}".
    /// Must be unique across all ICapture registrations in the DI container.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Captures data for a single org+project pair into IArtefactStore artefacts.
    /// </summary>
    Task CaptureAsync(InventoryContext context, CancellationToken ct);
}
```

**Constraints**:
- `Name` values must be unique across all `ICapture` registrations (enforced at DI assembly time via `ToDictionary` which throws on collision).
- `Name` is case-insensitive in the dispatch dictionary (`StringComparer.OrdinalIgnoreCase`).
- `CaptureAsync` MUST NOT throw unhandled exceptions for expected error conditions — log and propagate via task return value.

---

### 2. `IModule` (Updated — extends ICapture)

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Modules`  
**File**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs`

**Changes**:
- Inherits `ICapture` (adds `Name` and `CaptureAsync`)
- Removes `InventoryAsync(InventoryContext context, CancellationToken ct)` declaration
- All other members (`DependsOn`, `SupportsExport`, `SupportsInventory`, `SupportsPrepare`, `SupportsImport`, `SupportsValidate`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`) — **unchanged**

**Post-change interface surface**:
```
ICapture (base):
  string Name
  Task CaptureAsync(InventoryContext, CancellationToken)

IModule (extends ICapture):
  IReadOnlyList<ModuleDependency> DependsOn
  bool SupportsExport
  bool SupportsInventory        ← controls plan-builder task emission; stays on IModule
  bool SupportsPrepare
  bool SupportsImport
  bool SupportsValidate
  Task ExportAsync(ExportContext, CancellationToken)
  Task PrepareAsync(PrepareContext, CancellationToken)
  Task ImportAsync(ImportContext, CancellationToken)
  Task ValidateAsync(ValidationContext, CancellationToken)
```

---

### 3. `IProjectAnalyser` (Deleted)

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IProjectAnalyser.cs`

**Action**: Delete file. Remove all references:
- `DependencyAnalyser : IOrganisationsAnalyser, IProjectAnalyser` → becomes `DependencyAnalyser : IOrganisationsAnalyser`
- `DependencyAnalyser.CaptureProjectAsync(...)` method body → deleted
- `JobPlanExecutor` `IProjectAnalyser` branch in `TaskKind.Capture` handling → deleted; replaced by unified `captureHandlersByName` lookup

---

### 4. `ModuleBase` (Updated)

**Assembly**: `DevOpsMigrationPlatform.Infrastructure.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Infrastructure.Agent.Modules`  
**File**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs`

**Changes**:
- `InventoryAsync` → `CaptureAsync`; same no-op default body (logs warning and returns `Task.CompletedTask`)

```csharp
public virtual Task CaptureAsync(InventoryContext context, CancellationToken ct)
{
    _logger.LogWarning("Capture phase is not supported by module {Module}.", Name);
    return Task.CompletedTask;
}
```

---

### 5. Concrete Modules (Updated — method rename only)

All four modules below rename `InventoryAsync` → `CaptureAsync` and add `override`:

| Class | File |
|-------|------|
| `WorkItemsModule` | `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` |
| `IdentitiesModule` | `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs` |
| `NodesModule` | `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` |
| `TeamsModule` | `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs` |

**No logic changes**. Only the method name changes. Method body, parameters, return type — all identical.

---

### 6. `DependencyCapture` (New)

**Assembly**: `DevOpsMigrationPlatform.Infrastructure.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Infrastructure.Agent.Capture`  
**File**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs`

**Implements**: `ICapture` only (not `IModule`)

**Constructor dependencies**:
| Dependency | Type | Lifetime |
|------------|------|---------|
| `dependencyFactory` | `IDependencyDiscoveryServiceFactory` | Injected |
| `orchestrator` | `IDependencyOrchestrator` | Injected |
| `logger` | `ILogger<DependencyCapture>` | Injected |
| `metrics` | `IPlatformMetrics?` | Optional |
| `progressSink` | `IProgressSink?` | Optional |

**Properties**:
- `Name` → `"dependencies"` (matches second segment of `capture.dependencies.{org}.{project}`)

**`CaptureAsync` behaviour**:
1. Increment `in_flight` metric; open `dependency.capture` activity span (tags: `job.id`, `org.url`, `project.name`, `capture.handler="dependencies"`)
2. Log `Information`: "Capture started" with `{JobId}`, `{Org}`, `{Project}`
3. Emit `ProgressSink` event: `Stage = "Capturing"`
4. Open child span `dependency.capture.create_service`; call `IDependencyDiscoveryServiceFactory.CreateForProject`
5. Open child span `dependency.capture.execute`; call `IDependencyOrchestrator.CaptureProjectAsync` — this writes `discovery/{org}/{project}/dependencies.csv`
6. Close spans
7. Decrement `in_flight`; record `count` (1 project) and `duration_ms`
8. Log `Information`: "Capture completed" with `{DurationMs}`, `{OutputPath}`
9. Emit `ProgressSink` event: `Stage = "Captured"`, `Metrics = { Discovery = { Dependencies = { ... } } }`
10. On exception: record `errors` metric; log `Error`; emit `Stage = "Failed"`; re-throw (caller handles)

**Output artefact**: `discovery/{orgSlug}/{projectSlug}/dependencies.csv`
- Written by `IDependencyOrchestrator.CaptureProjectAsync` (existing behaviour, carried over from `DependencyAnalyser.CaptureProjectAsync`)
- Overwritten on resume (existing behaviour per FR-007)

---

### 7. `SimulatedDependencyDiscoveryServiceFactory` (New)

**Assembly**: `DevOpsMigrationPlatform.Infrastructure.Simulated`  
**Namespace**: `DevOpsMigrationPlatform.Infrastructure.Simulated.DependencyDiscovery`  
**File**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DependencyDiscovery/SimulatedDependencyDiscoveryServiceFactory.cs`

**Implements**: `IDependencyDiscoveryServiceFactory`

**Constructor dependencies**:
| Dependency | Type | Resolution |
|------------|------|------------|
| `linkAnalysisService` | `IWorkItemLinkAnalysisService` | Resolved from keyed singleton `"Simulated"` |

**Methods**:
- `Create(organisations, policies)` → wraps `linkAnalysisService` in a scoped `IDependencyDiscoveryService`
- `CreateForProject(allOrganisations, orgUrl, projectName, policies)` → same but scoped to single org+project

**Behaviour**: Delegates entirely to the already-registered `SimulatedWorkItemLinkAnalysisService`
which returns deterministic empty or configured link results. No external connectivity required.

**Registration**: Added to `AddSimulatedDependencyAnalysis` in `SimulatedServiceCollectionExtensions`:
```csharp
services.AddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>();
```

---

### 8. `IJobPlanExecutor` (Updated)

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs`

**Signature change** (see `contracts/IJobPlanExecutor.md` for full diff):

```diff
- IReadOnlyDictionary<string, IModule> modulesByName,
+ IReadOnlyDictionary<string, ICapture> captureHandlersByName,
```

`ExecuteExportPhaseAsync` and `ExecuteImportPhaseAsync` still use `IReadOnlyDictionary<string, IModule> modulesByName` — these are export/import phase methods that genuinely need module-level properties.

---

### 9. `JobPlanExecutor` (Updated)

**Assembly**: `DevOpsMigrationPlatform.Infrastructure.Agent`  
**File**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs`

**Key changes**:
1. `ExecuteTasksAsync` parameter: `modulesByName: IReadOnlyDictionary<string, IModule>` → `captureHandlersByName: IReadOnlyDictionary<string, ICapture>`
2. `TaskKind.Capture` branch: replaces current `IProjectAnalyser`-branching logic with single `captureHandlersByName` lookup:
   ```
   handlerName = GetModuleName(task.Id, captureHandlersByName.Keys)
   if !captureHandlersByName.TryGetValue(handlerName, out var captureHandler)
       log error; return
   captureHandler.CaptureAsync(scopedCtx, ct)
   ```
3. Remove `IProjectAnalyser` import and all references
4. `InventoryAsync` call sites (in `TaskKind.Capture` handling) → `CaptureAsync`

---

### 10. `JobAgentWorker` (Updated)

**Assembly**: `DevOpsMigrationPlatform.MigrationAgent`  
**File**: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`

**OnDiscoveryJobAsync changes**:
```diff
- var depModuleMap = modulesToRun.ToDictionary(m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
+ var captureHandlersByName = BuildCaptureHandlers(modulesToRun, jobScope.ServiceProvider);
  await _planExecutor.ExecuteTasksAsync(
-     discoveryPlan, modulesByName: depModuleMap, ...
+     discoveryPlan, captureHandlersByName: captureHandlersByName, ...
```

**OnMigrationJobAsync changes** (inventory job path):
```diff
- var inventoryModuleMap = jobModules.ToDictionary(m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
+ var captureHandlersByName = BuildCaptureHandlers(jobModules, jobScope.ServiceProvider);
  await _planExecutor.ExecuteTasksAsync(
-     executionPlan, inventoryModuleMap, ...
+     executionPlan, captureHandlersByName, ...
```

**New helper method**:
```csharp
private static IReadOnlyDictionary<string, ICapture> BuildCaptureHandlers(
    IEnumerable<IModule> modules,
    IServiceProvider serviceProvider)
{
    var result = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase);

    // Step 1: modules with SupportsInventory=true (cast to ICapture via IModule : ICapture)
    foreach (var module in modules.Where(m => m.SupportsInventory))
        result[module.Name] = module;

    // Step 2: pure ICapture registrations (not already added as a module)
    foreach (var capture in serviceProvider.GetServices<ICapture>())
        if (!result.ContainsKey(capture.Name))
            result[capture.Name] = capture;

    return result;
}
```

---

## State Transitions

The `ICapture.CaptureAsync` call does not manage task-level state directly. The `JobPlanExecutor`
wraps each task in `Running` → `Completed` / `Failed` transitions exactly as today. No new
task state values are introduced.

## Validation Rules

| Rule | Where Enforced |
|------|---------------|
| `ICapture.Name` must be unique | `BuildCaptureHandlers` — `ToDictionary` throws `ArgumentException` on collision |
| `ICapture.Name` must match second dot-segment of task ID | Convention enforced by `GetModuleName` in `JobPlanExecutor` |
| `SupportsInventory` controls module inclusion in capture handlers | `BuildCaptureHandlers` filter |
| `ICapture`-only types NOT registered as `IModule` | DI registration convention; `BuildCaptureHandlers` de-duplicates by name |

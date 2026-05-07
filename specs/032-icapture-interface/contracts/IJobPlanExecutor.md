# Interface Contracts: IJobPlanExecutor — Updated Signature

**Feature**: `032-icapture-interface`  
**Phase**: 1 — Design  
**Contract file**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs`

---

## Signature Diff — `ExecuteTasksAsync`

```diff
  Task<bool> ExecuteTasksAsync(
      JobTaskList plan,
-     IReadOnlyDictionary<string, IModule> modulesByName,
+     IReadOnlyDictionary<string, ICapture> captureHandlersByName,
      IReadOnlyDictionary<string, IAnalyser> analysersByName,
      InventoryContext? baseInventoryContext,
      ExportContext? baseExportContext,
      ImportContext? importContext,
      IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
      IStateStore stateStore,
      CancellationToken ct);
```

---

## Unchanged Signatures

`ExecuteExportPhaseAsync` and `ExecuteImportPhaseAsync` continue to use
`IReadOnlyDictionary<string, IModule> modulesByName` because export and import phases
require access to module-level properties (`SupportsExport`, `SupportsImport`) and phase methods
(`ExportAsync`, `ImportAsync`). These methods operate on `IModule`, not `ICapture`.

```csharp
Task<bool> ExecuteExportPhaseAsync(
    JobTaskList plan,
    IReadOnlyDictionary<string, IModule> modulesByName,   // unchanged
    ExportContext exportContext,
    IStateStore stateStore,
    CancellationToken ct);

Task<bool> ExecuteImportPhaseAsync(
    JobTaskList plan,
    IReadOnlyDictionary<string, IModule> modulesByName,   // unchanged
    ImportContext importContext,
    IStateStore stateStore,
    CancellationToken ct);
```

---

## Call-site Migration Guide

All callers of `ExecuteTasksAsync` must update the second positional argument:

**Before**:
```csharp
var modulesByName = modules.ToDictionary(m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
await _planExecutor.ExecuteTasksAsync(plan, modulesByName, analysers, ...);
```

**After**:
```csharp
var captureHandlersByName = BuildCaptureHandlers(modules, serviceProvider);
await _planExecutor.ExecuteTasksAsync(plan, captureHandlersByName, analysers, ...);
```

Where `BuildCaptureHandlers` is the helper in `JobAgentWorker` (see `data-model.md` §10).

---

## Known Call-sites

| File | Method | Action Required |
|------|--------|----------------|
| `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` | `OnDiscoveryJobAsync` | Replace `depModuleMap` with `captureHandlersByName` |
| `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` | `OnMigrationJobAsync` (inventory path) | Replace `inventoryModuleMap` with `captureHandlersByName` |
| All test files calling `ExecuteTasksAsync` | Various | Update second argument type; see FR-010 |

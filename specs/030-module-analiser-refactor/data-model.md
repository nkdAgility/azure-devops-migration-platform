# Data Model: Module IModule Phase Consolidation and IAnalyser Introduction

## New / Modified Abstractions

### `IModule` — Extended Interface

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/IModule.cs` (modify existing)

```
IModule
  Name: string                            (existing)
  DependsOn: IReadOnlyList<ModuleDependency>  (existing)
  SupportsInventory: bool                 (NEW — default false via ModuleBase)
  SupportsExport: bool                    (existing)
  SupportsPrepare: bool                   (NEW — default false via ModuleBase)
  SupportsImport: bool                    (existing)
  InventoryAsync(InventoryContext, CancellationToken): Task    (NEW)
  ExportAsync(ExportContext, CancellationToken): Task          (existing)
  PrepareAsync(PrepareContext, CancellationToken): Task        (NEW)
  ImportAsync(ImportContext, CancellationToken): Task          (existing)
  ValidateAsync(ValidationContext, CancellationToken): Task    (existing)
```

---

### `IAnalyser` — New Interface

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Analysis/IAnalyser.cs` (new file)

```
IAnalyser
  Name: string
  DependsOn: IReadOnlyList<ModuleDependency>
  AnalyseAsync(AnalyseContext, CancellationToken): Task
```

**Contract invariants:**
- MUST NOT write to any source or target system — only `IArtefactStore` reads/writes.
- MUST produce at least one non-empty artefact. Zero-output `AnalyseAsync` MUST emit a structured Warning log.
- MUST declare all data dependencies via `DependsOn` (the plan executor enforces ordering).
- Registered in DI alongside `IModule`. Both participate in `JobTaskList`.

---

### `InventoryContext` — New Context Record

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/InventoryContext.cs` (new file)

```
InventoryContext (record, init-only)
  Job: Job
  ArtefactStore: IArtefactStore
  StateStore: IStateStore
  ProgressSink: IProgressSink?          (optional)
  SourceEndpoint: OrganisationEndpoint  (scoped to one org per call)
  Projects: IReadOnlyList<string>?      (null = all projects)
```

---

### `PrepareContext` — New Context Record

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/PrepareContext.cs` (new file)

```
PrepareContext (record, init-only)
  Job: Job
  ArtefactStore: IArtefactStore        (read package; write prepare-report.json)
  StateStore: IStateStore
  ProgressSink: IProgressSink?
  TargetEndpoint: ITargetEndpointInfo  (query target for resolution)
```

---

### `AnalyseContext` — New Context Record

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Analysis/AnalyseContext.cs` (new file)

```
AnalyseContext (record, init-only)
  Job: Job
  ArtefactStore: IArtefactStore        (read inventory/export artefacts; write analysis artefacts)
  StateStore: IStateStore
  ProgressSink: IProgressSink?
  // No source or target endpoint — analysers are artefact-only
```

---

### `DependencyPhase` — Extended Enum

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/DependencyPhase.cs` (modify existing)

```
DependencyPhase (enum)
  Inventory = 0    (NEW)
  Export    = 1    (existing)
  Import    = 2    (existing)
  Both      = 3    (existing — Export + Import)
  Prepare   = 4    (NEW)
  Analyse   = 5    (NEW — IAnalyser.AnalyseAsync)
```

---

### `ModuleDependency` — Updated Record

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/ModuleDependency.cs` (modify existing)

Changes:
- `ModuleName` getter: strip `"Module"` suffix (existing) AND `"Analyser"` suffix (new).  
  E.g., `DependencyAnalyser` → `"Dependencies"`.
- New computed property: `AppliesToInventory => Phase is DependencyPhase.Inventory`
- New computed property: `AppliesToPrepare => Phase is DependencyPhase.Prepare`
- New computed property: `AppliesToAnalyse => Phase is DependencyPhase.Analyse`

---

### `PrepareReport` — New Artefact Record

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Modules/PrepareReport.cs` (new file)

```
PrepareReport (record, serialised to {Module}/prepare-report.json)
  ModuleName: string
  ResolvedCount: int
  UnresolvedCount: int
  UnresolvedItems: IReadOnlyList<UnresolvedItem>
  GeneratedAt: DateTimeOffset

UnresolvedItem (record)
  Key: string             (e.g. identity UPN, node path, team name)
  Reason: string          (why it could not be resolved)
  Severity: PrepareIssueSeverity

PrepareIssueSeverity (enum)
  Warning = 0
  Blocking = 1
```

---

## Modified Infrastructure Types

### `IInventoryOrchestrator` — Updated Signature

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**File**: `Discovery/IInventoryOrchestrator.cs` (modify existing)

Change: Replace `ExportContext context` parameter with `InventoryContext context`. Remove `IReadOnlyList<ScopedOrganisationEndpoint> organisations` parameter — multi-org loop lives in the caller (`JobAgentWorker`); each invocation is single-org.

```
IInventoryOrchestrator
  RunAsync(
    moduleName: string,
    eventStream: IAsyncEnumerable<InventoryProgressEvent>,
    context: InventoryContext,            ← was ExportContext
    checkpointIntervalSeconds: int = 300,
    ct: CancellationToken = default
  ): Task
```

---

### `JobTaskList` — Extended Phase Labels

**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent` (or ControlPlane)  
**File**: wherever `JobTask.Phase` string values are defined

Phase string values extended with:
- `"inventory"` — produced by `IModule.InventoryAsync`
- `"prepare"` — produced by `IModule.PrepareAsync`
- `"analyse"` — produced by `IAnalyser.AnalyseAsync`

---

## New Implementation Types

### `DependencyAnalyser` — Replaces `DependencyDiscoveryModule`

**Assembly**: `DevOpsMigrationPlatform.Infrastructure.Agent`  
**File**: `Analysis/DependencyAnalyser.cs` (new file)

```
DependencyAnalyser : IAnalyser
  Name => "Dependencies"
  DependsOn => [ ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory) ]
  AnalyseAsync(AnalyseContext, CancellationToken): Task
    → delegates to IDependencyOrchestrator (adapted to AnalyseContext)
    → reads ArtefactStore → "inventory.json"
    → writes ArtefactStore → "analysis/dependencies.csv"
    → writes ArtefactStore → "analysis/dependencies.mmd"
```

---

## Eliminated Types

| Type | Assembly | Reason |
|---|---|---|
| `InventoryModule` | Infrastructure.Agent | Logic absorbed into `WorkItemsModule.InventoryAsync` |
| `InventoryDiscoveryModule` | Infrastructure.Agent | Multi-org loop moved to `JobAgentWorker` |
| `DependencyDiscoveryModule` | Infrastructure.Agent | Replaced by `DependencyAnalyser : IAnalyser` |

---

## Module Phase Support Matrix (Post-Refactor)

| Module | SupportsInventory | SupportsExport | SupportsPrepare | SupportsImport |
|---|---|---|---|---|
| `WorkItemsModule` | true | true | true | true |
| `IdentitiesModule` | true | true | true | true |
| `NodesModule` | true | true | true | true |
| `TeamsModule` | true | true | true | true |

---

## JobKind → Phase Dispatch Table

| JobKind | Phases executed (in order) |
|---|---|
| `Inventory` | `inventory` (all IModules where SupportsInventory) → `analyse` (IAnalysers whose DependsOn satisfied) |
| `Export` | `inventory` → `export` |
| `Prepare` | `analyse` (IAnalysers required by any SupportsPrepare module's DependsOn) → `prepare` (all IModules where SupportsPrepare) |
| `Import` | `import` |
| `Migrate` | `inventory` → `export` → `prepare` → `import` → `validate` |
| `Dependencies` | `inventory` (WorkItems only) → `analyse` (DependencyAnalyser only) |

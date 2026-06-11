# Agent Execution Model

Authoritative model for the Module-down execution hierarchy.
For the Job/Task layer above this, see `job-lifecycle.md` and `task-execution-contract.md`.

---

## The Hierarchy

```
Module
  └── Orchestrator
        └── Extension  (IModuleExtension)
              ├── Adapter     (*Adapter)
              ├── Tool        (*Tool)
              └── PackageAccess
```

**One type, both directions — at every layer.**
Modules have `ExportAsync` + `ImportAsync`. Extensions have `ExportAsync` + `ImportAsync`.
Adapters have read + write methods. There are no export-only or import-only types at any layer.

---

## Layer: Module

**Naming**: `{Domain}Module` — e.g. `TeamsModule`, `WorkItemsModule`  
**Interface**: `IModule : ICapture`  
**Lives in**: `Infrastructure.Agent/Modules/`

### Owns
- Configuration and endpoint resolution
- Phase entrypoint (`ExportAsync`, `ImportAsync`, `PrepareAsync`, `ValidateAsync`, `CaptureAsync`)
- Extension discovery: resolves `IEnumerable<IModuleExtension>` from DI, filters `IsEnabled`, filters `SupportsExport`/`SupportsImport`, sorts by `Order`, passes `IReadOnlyList<IModuleExtension>` to orchestrator
- Delegation to `I{Domain}Orchestrator`

### Must not own
- Entity loops, stage sequencing, checkpoint logic
- Capability logic or adapter SDK calls
- Knowledge of which extensions are registered

### Phase shape
```csharp
public interface IModule : ICapture
{
    Task CaptureAsync(InventoryContext ctx, CancellationToken ct);
    Task ExportAsync(ExportContext ctx, CancellationToken ct);
    Task PrepareAsync(PrepareContext ctx, CancellationToken ct);
    Task ImportAsync(ImportContext ctx, CancellationToken ct);
    Task ValidateAsync(ValidateContext ctx, CancellationToken ct);
}
```

### Configuration
- Every module has an `Enabled` property.
- Configuration path: `MigrationPlatform.Modules.{ModuleName}`.
- Bound via `IOptions<T>`.

### Isolation
- A module failure must not crash other modules.
- Modules must not call other modules or hold references to other module instances.
- Service registration via DI extension methods in the module assembly.
- Filesystem access only through `IPackageAccess`.

---

## Layer: Orchestrator

**Naming**: `{Domain}Orchestrator` — e.g. `TeamsOrchestrator`, `NodesOrchestrator`  
**Interface**: `I{Domain}Orchestrator`  
**Lives in**: `Infrastructure.Agent/Modules/` or `Infrastructure.Agent/{Domain}/`

### Owns
- The per-entity loop (foreach team, foreach work item, etc.)
- Stage gates and phase boundaries
- Checkpointing and cursor-based resume
- Metrics emission (`IMigrationMetrics`)
- Progress events (`IProgressSink`)
- Receiving `IReadOnlyList<IModuleExtension>` from the module; invoking each extension per entity

### Must not own
- Capability logic (what to export/import)
- Adapter SDK mechanics
- Knowledge of which extensions are registered or what they do

### Module → Orchestrator seam
The module filters and sorts extensions; the orchestrator iterates entities and invokes them:
```
Module:
  1. resolve IEnumerable<IModuleExtension> from DI
  2. filter IsEnabled(options)
  3. filter SupportsExport | SupportsImport
  4. sort by Order
  5. pass IReadOnlyList<IModuleExtension> → Orchestrator

Orchestrator:
  foreach entity:
    build ExtensionContext
    foreach extension → ExportAsync / ImportAsync
```

### Invariants
- One orchestrator per concern — do not split by phase (no `{Domain}ExportOrchestrator` + `{Domain}ImportOrchestrator`).
- Phase methods remain symmetric (`ExportAsync`, `ImportAsync`) unless contract change governance is satisfied.
- Compile-time phase guards (`#if`) on orchestrator abstraction contracts are forbidden.
- Adapter support differences are handled in adapters and capability flags, not by splitting orchestrator shapes.

---

## Layer: Extension

**Naming**: `{Capability}{Domain}Extension` — e.g. `BoardConfigTeamExtension`  
**Interface**: `IModuleExtension` (cross-cutting); module-specific sub-interface e.g. `ITeamExtension`  
**Lives in**: `Infrastructure.Agent/{Domain}/Extensions/`

### Owns
- One cohesive capability's export + import logic
- Wrapping the adapter(s) and tool(s) needed for that capability
- `IsEnabled(options)` — pure function of options, no I/O
- `ExportAsync(ExtensionContext, ct)` and `ImportAsync(ExtensionContext, ct)`

### Must not own
- Other capabilities (one extension = one concern)
- Loop control or checkpoint logic
- Knowledge of other extensions

### IModuleExtension contract
```csharp
public interface IModuleExtension
{
    string Module { get; }       // e.g. "Teams"
    string Name { get; }         // e.g. "BoardConfig" — unique within module
    int Order { get; }           // lower runs first
    bool SupportsExport { get; }
    bool SupportsImport { get; }
}
```

### Module-specific extension contracts
Module-specific extension interfaces extend `IModuleExtension` and add:
- `IsEnabled(TModuleExtensionsOptions)` — enablement check
- `ExportAsync(TExtensionContext, ct)`
- `ImportAsync(TExtensionContext, ct)`

Export and import are capabilities **on one extension** — not separate types.

### ExtensionContext
A sealed record passed per entity per invocation. Contains:
- Entity identity (e.g. `TeamDefinition`, `Slug`)
- `IPackageAccess` — read (import) or write (export) the package
- Module options
- `IProgressSink?`

Extensions must not cache state between entity invocations.

### ConnectorCapability guard
Extensions check `IConnectorCapabilityProvider.Has(ConnectorCapability.X)` before calling their adapter.
TFS registers `ConnectorCapability.None` explicitly. Capability absence → return `Skipped`; never throw, never null-guard the adapter.

---

## Layer: Adapter

**Naming**: `{Connector}{Domain}Adapter` — e.g. `AzureDevOpsBoardAdapter`, `SimulatedBoardAdapter`  
**Interface**: `I{Domain}Adapter` — e.g. `ITeamBoardAdapter`  
**Lives in**: `Infrastructure.{Connector}/{Domain}/`

### Owns
- Connector SDK mechanics for one concern
- Both read (export) and write (import) methods in one type
- No orchestration, no sequencing, no transformation logic

### Must not own
- Phase policy (when to call, skip/fail decisions)
- Transformation logic (that belongs in tools)
- Knowledge of other adapters or concerns

### Naming of methods
- Read methods (export): `Get{Thing}Async`
- Write methods (import): `Update{Thing}Async`, `Create{Thing}Async`
- Merge-mode reads (current live state): `GetCurrent{Thing}Async`

### TFS pattern
TFS connectors that do not support a concern do NOT register the adapter. The capability flag (`ConnectorCapability.None`) is what the extension checks. No null-guard on the adapter injection.

### Implementors per connector
Each capability has three adapter implementations:
- `AzureDevOps{Domain}Adapter` — uses ADO REST SDK
- `Simulated{Domain}Adapter` — deterministic canned data (export); captures writes in-memory (import)
- `Tfs{Domain}Adapter` — where supported; omitted where `ConnectorCapability` is `None`

---

## Layer: Tool

**Naming**: `I{Concern}Tool` / `{Concern}Tool` — e.g. `INodeTranslationTool`, `IIdentityTranslationTool`  
**Lives in**: `Abstractions.Agent/Tools/` (interface); `Infrastructure.Agent/Tools/` (implementation)

### Owns
- Pure stateless transformation logic
- No I/O, no network calls, no filesystem access
- Shared logic consumed by multiple extensions across multiple modules

### Must not own
- Phase knowledge
- State between invocations
- Orchestration decisions

### Injected into
Extensions that need shared transformation logic. Modules and orchestrators do not call tools directly — they delegate to extensions which wrap the tools they need.

### Examples
- `INodeTranslationTool` — translates iteration/area paths from source project naming to target
- `IIdentityTranslationTool` — maps source identity descriptors to target; synchronous, reads Prepare-phase cache

---

## Layer: PackageAccess

**Interface**: `IPackageAccess`  
**Lives in**: `Abstractions.Agent/`

The package is the boundary between export and import. It is the source of truth.

- Extensions write to the package during export via `IPackageAccess`
- Extensions read from the package during import via `IPackageAccess`
- No layer accesses the filesystem directly — all package I/O goes through this abstraction
- Passed to extensions via `ExtensionContext`

---

## The Three Seams

```
Module → Orchestrator      Policy seam
                           Module owns: what extensions run, in what order
                           Orchestrator owns: when per entity, checkpoint, metrics

Extension → Adapter        Connector seam
                           Extension owns: capability logic, phase decisions
                           Adapter owns: SDK mechanics, both directions

Extension → Tool           Logic seam
                           Extension owns: orchestrating the transformation
                           Tool owns: the pure transformation itself
```

---

## Naming Conventions

| Layer | Interface pattern | Implementation pattern | Location |
|---|---|---|---|
| Module | `IModule` (fixed) | `{Domain}Module` | `Infrastructure.Agent/Modules/` |
| Orchestrator | `I{Domain}Orchestrator` | `{Domain}Orchestrator` | `Infrastructure.Agent/Modules/` or `/{Domain}/` |
| Extension (cross-cutting) | `IModuleExtension` | — | `Abstractions.Agent/` |
| Extension (module-specific) | `I{Domain}Extension` | `{Capability}{Domain}Extension` | `Infrastructure.Agent/{Domain}/Extensions/` |
| Adapter (interface) | `I{Domain}Adapter` | — | `Abstractions.Agent/{Domain}/` |
| Adapter (impl) | — | `{Connector}{Domain}Adapter` | `Infrastructure.{Connector}/{Domain}/` |
| Tool (interface) | `I{Concern}Tool` | — | `Abstractions.Agent/Tools/` |
| Tool (impl) | — | `{Concern}Tool` | `Infrastructure.Agent/Tools/` |

---

## Telemetry Obligations

Every layer from Module down must satisfy four telemetry obligations:

- **O-1** `ActivitySource.StartActivity` — span per operation
- **O-2** `IMigrationMetrics` — count, duration, errors, in-flight
- **O-3** `ILogger` structured events — started / completed / skipped / error
- **O-4** `IProgressSink.EmitAsync` — progress events at start and completion

Extensions own O-1 through O-4 for their capability. Orchestrators own O-1 through O-4 for the entity loop.

---

## Test Expectations

- Every export extension must have a Simulated scenario asserting the package artefact exists with non-empty content.
- Every import extension must have a Simulated scenario asserting the adapter received the expected write calls.
- Zero-item Simulated adapters are forbidden.
- Simulated adapters capture write calls in-memory for assertion — they do not assert internally.

---

## Related

- `.agents/10-contracts/specs/execution-contract.md` — canonical interface surface and rules
- `.agents/10-contracts/specs/package-boundary-contract.md`
- `.agents/10-contracts/specs/package-persistence-contract.md`
- `.agents/10-contracts/specs/field-transform-contract.md`
- `.agents/20-guardrails/core/architecture-boundaries.md`
- `.agents/20-guardrails/core/capability-ethos-rules.md`
- `.agents/30-context/domains/job-lifecycle.md` — Job/Task layer above this
- `docs/module-development-guide.md` — full implementation guide

# Agent Execution Model

**Audience:** Contributors.
**Purpose:** Authoritative reference for the Module-down execution hierarchy — Module → Orchestrator → Extension → Adapter → Tool — including layer ownership, seams, naming conventions, and a worked example.

For the Job/Task layer above this, see [`.agents/30-context/domains/job-lifecycle.md`](../.agents/30-context/domains/job-lifecycle.md) and [`.agents/10-contracts/specs/task-execution-contract.md`](../.agents/10-contracts/specs/task-execution-contract.md). The compressed agent-facing summary of this model lives in [`.agents/30-context/architecture/execution-model.md`](../.agents/30-context/architecture/execution-model.md).

---

## The Hierarchy

```
Module
  │   builds the list of Extensions (IModuleExtension): resolve from DI,
  │   filter IsEnabled + SupportsExport/SupportsImport, sort by Order
  │
  │   ── passes IReadOnlyList<IModuleExtension> ──▶  Orchestrator
  │                                                    │  does the orchestration:
  │                                                    │  foreach entity → invoke each Extension
  ▼                                                    ▼
(the Module owns the extension list;            Extension  (IModuleExtension)
 the Orchestrator runs it — it does               ├── Adapter     (*Adapter)
 not own or contain the extensions)               ├── Tool        (*Tool)
                                                   └── PackageAccess
```

The relationship is **ownership + handoff**, not containment: the **Module** creates and
owns the extension list and passes it to the **Orchestrator**, which orchestrates (entity
loop, checkpointing, metrics) and invokes each extension per entity. The orchestrator never
owns extensions, and extension/capability logic never lives inside an orchestrator.

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
- Extension list-building: resolves `IEnumerable<IModuleExtension>` from DI, applies the **default / mandatory / optional** tiers (mandatory forced enabled — disabling one is a fail-closed config error), filters `SupportsExport`/`SupportsImport`, sorts by `Order`, passes `IReadOnlyList<IModuleExtension>` to orchestrator
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
  2. apply tiers: default (auto-include) + mandatory (force enabled; disabling = fail-closed error)
                 + optional (include when extension.IsEnabled)
  3. filter SupportsExport | SupportsImport
  4. sort by Order
  5. pass IReadOnlyList<IModuleExtension> → Orchestrator

Orchestrator:
  foreach entity:
    build ExtensionContext
    foreach extension → ExportAsync / ImportAsync
```

> **Teams Module — simplified model (no mandatory tier yet)**: The current `TeamsModule`
> passes `IEnumerable<IModuleExtension>` (all registered extensions) through to
> `TeamsOrchestrator`. The orchestrator filters (by `Module == "Teams"`, `IsEnabled`,
> and `SupportsExport`/`SupportsImport`) and sorts by `Order`. The three-tier concept
> (default / mandatory / optional) is a future extension point; no Teams extensions are
> currently mandatory. This is a deliberate simplified variant — not a deviation from the spec.

### Invariants
- One orchestrator per concern — do not split by phase (no `{Domain}ExportOrchestrator` + `{Domain}ImportOrchestrator`).
- Phase methods remain symmetric (`ExportAsync`, `ImportAsync`) unless contract change governance is satisfied.
- Compile-time phase guards (`#if`) on orchestrator abstraction contracts are forbidden.
- Adapter support differences are handled in adapters and capability flags, not by splitting orchestrator shapes.

---

## Layer: Extension

**Naming**: `{Capability}{Domain}Extension` — e.g. `BoardConfigTeamExtension`
**Interface**: `IModuleExtension` — the single, cross-cutting extension contract. There is **no** `I{Domain}Extension` sub-interface.
**Lives in**: `Infrastructure.Agent/{Domain}/Extensions/`

An extension is thin, module-neutral, and interchangeable with other extensions. It extends a
module's per-entity behaviour and may call tools. It is **instantiated with its own custom config**
(its own `IOptions<T>`) — contrast the Tool layer, which is a run-wide singleton with one central config.

### Owns
- One cohesive capability's export + import logic
- Wrapping the adapter(s) and calling the tool(s) needed for that capability
- Parameterless `IsEnabled` — answered from its **own** `IOptions<T>`, pure, no I/O
- `ExportAsync(IExtensionContext, ct)` and `ImportAsync(IExtensionContext, ct)`

### Must not own
- Other capabilities (one extension = one concern)
- Loop control or checkpoint logic
- Knowledge of other extensions
- A shared, module-wide options god-object (each extension owns its own distinct config)

### IModuleExtension contract
```csharp
public interface IModuleExtension
{
    string Module { get; }       // owning module name, e.g. "Teams"
    string Name { get; }         // e.g. "BoardConfig" — unique within module
    int Order { get; }           // lower runs first
    bool SupportsExport { get; }
    bool SupportsImport { get; }
    bool IsEnabled { get; }      // parameterless — reads its OWN IOptions<T>

    Task ExportAsync(IExtensionContext context, CancellationToken ct);
    Task ImportAsync(IExtensionContext context, CancellationToken ct);
}
```

All extensions implement `IModuleExtension` **directly**. Export and import are capabilities
**on one extension** — not separate types, and not a domain-specific sub-interface.

### Per-extension configuration
Each extension owns its own, distinct `IOptions<T>`:
- A **mandatory** extension has no `Enabled` knob and returns `IsEnabled => true`.
- An **optional** extension exposes `Enabled` (plus any extension-specific settings) and returns it.

Adding an extension never requires editing a central config class. An extension may be bound to
more than one module in the same form; whether it is **default**, **mandatory**, or **optional** is a
property of the module→extension **binding**, decided when the module builds its list — not of the
extension type.

### IExtensionContext
A module-neutral, sealed record passed per entity per invocation. The base contract:
- `Organisation`, `ProjectName`, `EntityId`
- `TargetEntityId` — null during export; set by the orchestrator before import invocation
- `Package` (`IPackageAccess`) — read (import) or write (export) the package

The host module supplies a concrete record implementing `IExtensionContext` (carrying domain data,
e.g. the team definition and slug); extensions cast to the concrete type they require. Extensions
must not cache state between entity invocations.

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

A Tool and an Orchestrator are the same idea — a worker — differing in breadth: an orchestrator
coordinates many things within its sphere; a tool encapsulates one piece of functionality made
available everywhere. A Tool is a **singleton with one central config for the entire run**, declared
once at `MigrationPlatform.Tools.*`. One instance, one config, shared by every consumer.

### Owns
- Pure stateless transformation / lookup logic
- No I/O, no network calls, no filesystem access
- A single capability, provided as a service to many consumers

### Must not own
- Phase knowledge
- State between invocations
- Orchestration decisions
- Per-consumer config (config is run-wide and central)

### Injected into
Consumed directly via DI by whoever needs it — an orchestrator or an extension. A tool is **not**
wrapped per-module and is **not** an entry in any module's extension list; it is a separate category
from extensions (tool = provides a service; extension = extends behaviour). Modules, being thin, do
not call tools directly — they delegate to their orchestrator.

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
| Extension (contract) | `IModuleExtension` (fixed — no `I{Domain}Extension`) | `{Capability}{Domain}Extension` | contract in `Abstractions.Agent/`; impl in `Infrastructure.Agent/{Domain}/Extensions/` |
| Extension context | `IExtensionContext` | `{Domain}ExtensionContext` | `Abstractions.Agent/` |
| Adapter (interface) | `I{Domain}Adapter` | — | `Abstractions.Agent/{Domain}/` |
| Adapter (impl) | — | `{Connector}{Domain}Adapter` | `Infrastructure.{Connector}/{Domain}/` |
| Tool (interface) | `I{Concern}Tool` | — | `Abstractions.Agent/Tools/` |
| Tool (impl) | — | `{Concern}Tool` | `Infrastructure.Agent/Tools/` |

---

## Worked Example: BoardConfigTeamExtension

`BoardConfigTeamExtension` is the canonical worked example for the Extension layer.

| Aspect | Value |
|---|---|
| Module | `"Teams"` |
| Name | `"BoardConfig"` |
| Order | `100` |
| Config | `IOptions<BoardConfigExtensionOptions>` (own config; not nested in TeamsModuleOptions) |
| Adapter | `ITeamBoardAdapter` — both export reads and import writes in one type |
| Capability gate | `_capProvider.Has(ConnectorCapability.BoardConfig)` — skip if absent, never throw |
| Export path | `ExportAsync` → reads boards/swimlanes/card rules/backlogs/taskboard columns from adapter → serialises to `Teams/{slug}/board-config.json` |
| Import path | `ImportAsync` (O-1/O-2 shell) → `ImportCoreAsync` (logic) — reads package artefact, applies `ImportMode` (Replace/Merge/Skip), writes to target via adapter |
| FR-013 filter | Invalid state mappings (referencing absent target states) are filtered before `UpdateBoardColumnsAsync`, with per-column log warnings |
| Telemetry | O-1 Activity span; O-2 six `IPlatformMetrics` instruments (count/duration/errors/in-flight/skipped); O-3 `ILogger`; O-4 `IProgressSink` events |
| Test coverage | `BoardConfigTeamExtensionTests` (DomainTests) + `SimulatedBoardAdapterExportTests` / `SimulatedBoardAdapterImportTests` (SystemTest_Simulated) + `AzureDevOpsBoardAdapterTests` (IntegrationTests) |

Key design decisions:
- `ImportAsync` is split into a public `ImportAsync` (Activity span + InFlight metrics) and private `ImportCoreAsync` (logic + skip/error/count/duration). This avoids nesting a try/catch inside the Activity inside early-return guards.
- The adapter is registered in the AzureDevOps connector project; TFS registers `ConnectorCapability.None` and no `ITeamBoardAdapter` — the extension never null-guards the adapter.
- `BuildValidStatesMap` reads the target board's current columns (not a dedicated API) to determine valid states. WITs absent from the map pass through; only present WITs with absent states are filtered.

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

## Related Documents

- [`module-development-guide.md`](module-development-guide.md) — full implementation guide
- [`architecture.md`](architecture.md) — whole-platform architecture
- [`.agents/10-contracts/specs/execution-contract.md`](../.agents/10-contracts/specs/execution-contract.md) — canonical interface surface and rules
- [`.agents/10-contracts/specs/package-boundary-contract.md`](../.agents/10-contracts/specs/package-boundary-contract.md)
- [`.agents/10-contracts/specs/package-persistence-contract.md`](../.agents/10-contracts/specs/package-persistence-contract.md)
- [`.agents/10-contracts/specs/field-transform-contract.md`](../.agents/10-contracts/specs/field-transform-contract.md)
- [`.agents/20-guardrails/core/architecture-boundaries.md`](../.agents/20-guardrails/core/architecture-boundaries.md)
- [`.agents/20-guardrails/core/capability-ethos-rules.md`](../.agents/20-guardrails/core/capability-ethos-rules.md)

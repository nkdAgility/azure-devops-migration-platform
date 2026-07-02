# Agent Execution Model (Summary)

Compressed summary of the Module-down execution hierarchy. Full reference:
[`docs/execution-model.md`](../../../docs/execution-model.md).
Canonical interface surface and rules: `.agents/10-contracts/specs/execution-contract.md`.
Job/Task layer above this: `.agents/30-context/domains/job-lifecycle.md`.

## Hierarchy

`Module → Orchestrator → Extension → (Adapter | Tool | PackageAccess)`

- **Module** (`{Domain}Module : IModule : ICapture`) — phase entrypoints (`CaptureAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`), config/endpoint resolution, builds the extension list from DI (default/mandatory/optional tiers, filter `SupportsExport`/`SupportsImport`, sort by `Order`), delegates to `I{Domain}Orchestrator`. No entity loops, no capability logic.
- **Orchestrator** (`{Domain}Orchestrator`) — per-entity loop, stage gates, checkpointing/cursor resume, metrics + progress. Receives the extension list; never owns extensions. One orchestrator per concern — never split by phase; no `#if` phase guards on abstraction contracts.
- **Extension** (`{Capability}{Domain}Extension : IModuleExtension` — direct, no `I{Domain}Extension` sub-interface) — one capability's export + import logic, own `IOptions<T>`, parameterless pure `IsEnabled`. Checks `IConnectorCapabilityProvider.Has(...)` before adapter calls: absence → `Skipped`, never throw, never null-guard.
- **Adapter** (`{Connector}{Domain}Adapter : I{Domain}Adapter`) — connector SDK mechanics for one concern, read + write in one type. Three implementors per capability: AzureDevOps, Simulated, Tfs (omitted where capability is `None`).
- **Tool** (`{Concern}Tool : I{Concern}Tool`) — pure stateless transformation, run-wide singleton, central config at `MigrationPlatform.Tools.*`. Not an extension; injected via DI wherever needed.
- **PackageAccess** (`IPackageAccess`) — sole package I/O boundary; no layer touches the filesystem directly.

## Invariants

- One type, both directions, at every layer — no export-only/import-only types.
- Three seams: Module→Orchestrator (policy), Extension→Adapter (connector), Extension→Tool (logic).
- Telemetry obligations O-1..O-4 at every layer from Module down.
- Every export extension: Simulated test asserting non-empty package artefact. Every import extension: Simulated test asserting adapter received writes. Zero-item Simulated adapters forbidden.

Worked example (`BoardConfigTeamExtension`), naming-convention table, and layer
ownership detail: see the full reference in `docs/execution-model.md`.

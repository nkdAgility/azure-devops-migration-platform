# Execution Contract

Canonical contract surface and rules for the Module-down execution hierarchy.
For the full model with layer descriptions, naming conventions, and seam diagrams,
see `.agents/30-context/architecture/execution-model.md`.

---

## Canonical Interface Surface

### Module layer

| Interface | Purpose |
|---|---|
| `IModule : ICapture` | All modules; exposes `CaptureAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync` |
| `ICapture` | Pure inventory handlers; `Name` + `CaptureAsync` only |
| `IAnalyser` | Post-inventory analysis; writes artefacts to package |

### Orchestrator layer

| Interface | Module |
|---|---|
| `ITeamsOrchestrator` | Teams |
| `INodesOrchestrator` | Nodes |
| `IIdentitiesOrchestrator` | Identities |
| `IWorkItemsImportOrchestrator` | WorkItems (import) |
| `IWorkItemsNodeReadinessOrchestrator` | WorkItems (node readiness) |
| `IInventoryOrchestrator` | Inventory |
| `IDependencyOrchestrator` | Dependencies |

### Extension layer

| Interface | Scope |
|---|---|
| `IModuleExtension` | Cross-cutting marker; `Module`, `Name`, `Order`, `SupportsExport`, `SupportsImport` |
| `ITeamExtension : IModuleExtension` | Teams per-entity extension; `IsEnabled`, `ExportAsync(TeamExtensionContext, ct)`, `ImportAsync(TeamExtensionContext, ct)` |

### Adapter layer

| Interface | Connector concern |
|---|---|
| `ITeamBoardAdapter` | Teams board config (columns, swimlanes, card rules, backlogs, taskboard) |
| `IIdentityAdapter` | Identity live-query during Prepare phase |

### Tool layer

| Interface | Purpose |
|---|---|
| `INodeTranslationTool` | Translates iteration/area paths between source and target project namings |
| `IIdentityTranslationTool` | Maps source identity descriptors to target; synchronous; reads Prepare-phase cache |

### ConnectorCapability

| Type | Purpose |
|---|---|
| `ConnectorCapability` | `[Flags]` enum: `None=0`, `BoardConfig=1`, `Taskboard=2`, `Backlogs=4` |
| `IConnectorCapabilityProvider` | `Has(ConnectorCapability)` — registered by every connector, including TFS with `None` |

---

## Rules

### Universal — apply at every layer

1. **One type, both directions.** No layer has separate export-only or import-only types. Modules, orchestrators, extensions, and adapters all carry both directions in a single contract.
2. **Symmetric phase shape.** `ExportAsync` and `ImportAsync` are always present as a pair. Compile-time guards (`#if`) that remove phase methods from abstraction contracts are forbidden.
3. **Telemetry at every layer.** O-1 spans, O-2 metrics, O-3 structured logging, O-4 progress events are required at module and extension level. See `.agents/20-guardrails/domains/observability-requirements.md`.

### Module rules

4. Modules are thin wrappers — no entity loops, no checkpoint logic.
5. Modules resolve `IEnumerable<IModuleExtension>` from DI, filter by `IsEnabled` + `SupportsExport`/`SupportsImport`, sort by `Order`, and pass `IReadOnlyList` to the orchestrator.
6. Module configuration lives under `MigrationPlatform.Modules.{ModuleName}` and is bound via `IOptions<T>`.
7. A module failure must not crash other modules.
8. Modules must not access the filesystem directly — all package I/O through `IPackageAccess`.

### Orchestrator rules

9. One orchestrator per concern — do not split by phase.
10. Orchestrators own the entity loop, checkpointing, metrics, and progress events. They must not own capability logic or adapter SDK calls.
11. Introducing an alternate orchestration entrypoint for an existing concern is forbidden without contract change governance approval.
12. WorkItems import must sequence: startup policy → node readiness → deterministic revision dispatch, with runtime-visible stage markers.
13. Resolution strategy selection is fail-closed: unsupported explicit strategy values must error; `NullResolutionStrategy` is valid only when the connector intentionally uses idmap-only resolution.

### Extension rules

14. One extension = one capability. Extensions must not implement logic for multiple concerns.
15. Extensions check `IConnectorCapabilityProvider.Has(...)` before calling their adapter. Capability absence → return Skipped; never null-guard the adapter injection.
16. `IsEnabled` must be a pure function of options — no I/O, no state.
17. Extensions must not cache state between entity invocations.
18. `OperationCanceledException` must propagate — extensions must not swallow cancellation.
19. Extension `Name` must be unique within its module.

### Adapter rules

20. Adapters carry both read (export) and write (import) methods in one type.
21. Adapters own SDK mechanics only — no orchestration, no sequencing, no transformation logic.
22. TFS connectors that do not support a concern omit the adapter registration. The capability flag is the guard — not a null check in the extension.
23. `Get*` methods must not throw for an entity with no data — return empty sequence or empty list.
24. `Update*` methods must throw a domain exception if the target entity does not exist (extension handles this with a structured warning).

### Tool rules

25. Tools are stateless and pure — no I/O, no network calls, no filesystem access.
26. Tools are injected into extensions, not called directly by modules or orchestrators.
27. Tools must not hold state between invocations.

### PackageAccess rules

28. All package reads and writes go through `IPackageAccess` — no direct filesystem access anywhere in the hierarchy.
29. The package is the boundary between export and import — it is the source of truth.

---

## Violation Conditions

Any of the following constitutes an architectural violation requiring correction before merge:

- A layer has separate export-only and import-only types (e.g. `ITeamBoardSource` + `ITeamBoardTarget`)
- A module contains an entity loop or checkpoint logic
- An orchestrator is split by phase (e.g. `{Domain}ExportOrchestrator` + `{Domain}ImportOrchestrator`)
- An adapter is split by direction
- A tool performs I/O
- A module or orchestrator calls a tool directly (bypassing extensions)
- An extension null-guards its adapter instead of checking `ConnectorCapability`
- An extension swallows `OperationCanceledException`
- A phase method is removed by a compile-time guard on an abstraction contract
- Direct filesystem access outside of `IPackageAccess`

---

## Related

- `.agents/30-context/architecture/execution-model.md` — full model with layer descriptions, naming, seam diagrams
- `.agents/10-contracts/specs/package-boundary-contract.md`
- `.agents/10-contracts/specs/package-persistence-contract.md`
- `.agents/10-contracts/specs/field-transform-contract.md`
- `.agents/10-contracts/specs/checkpoint-phase-tracking-contract.md`
- `.agents/20-guardrails/core/capability-ethos-rules.md`
- `.agents/20-guardrails/core/architecture-boundaries.md`

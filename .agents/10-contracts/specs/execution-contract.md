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
| `IModuleExtension` | The **single, cross-cutting** extension contract. All extensions implement it directly — `Module`, `Name`, `Order`, `SupportsExport`, `SupportsImport`, parameterless `IsEnabled`, `ExportAsync(IExtensionContext, ct)`, `ImportAsync(IExtensionContext, ct)` |
| `IExtensionContext` | Module-neutral per-entity context — `Organisation`, `ProjectName`, `EntityId`, `TargetEntityId`, `Package`. A module supplies a concrete record implementing it; extensions cast to the type they require |

There is **no `I{Domain}Extension`** sub-interface (e.g. no `ITeamExtension`). Extensions are
module-neutral and interchangeable; the same extension, in the same form, may be bound to more
than one module. Module-specific data reaches the extension through the concrete
`IExtensionContext` the host module builds — not through a domain-specific extension type.

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

4. Modules are thin wrappers — no entity loops, no checkpoint logic. A module is the thin, uniform standard front for the orchestrator of the same name; all modules are interchangeable in shape.
5. Modules build the extension list and pass `IReadOnlyList<IModuleExtension>` to the orchestrator. List-building has three tiers:
   - **Default** extensions are included automatically.
   - **Mandatory** extensions MUST be present and enabled for that module; their effective-enabled is forced `true`. An operator attempting to disable a mandatory extension is a **fail-closed configuration error**, never a silent skip.
   - **Optional** extensions are included only when the operator adds them and their own `IsEnabled` is `true`.
   After tiering, the module filters by `SupportsExport`/`SupportsImport` and sorts by `Order`. Mandatory/default status is a property of the **module→extension binding**, not of the extension type — the same extension may be mandatory for one module and optional for another.
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
16. `IsEnabled` is a **parameterless** property. Each extension answers from its **own** `IOptions<T>` — a mandatory extension returns `true`; an optional extension returns its own `Enabled` setting. `IsEnabled` must be pure (no I/O, no state) and must not read any shared, module-level options object.
17. Each extension owns its **own, distinct** `IOptions<T>` — its config is local to the extension and is not nested inside a shared module-wide options god-object. Adding an extension never requires editing a central config class.
18. An extension is module-neutral: it implements `IModuleExtension` directly (no `I{Domain}Extension`) and operates over `IExtensionContext`, casting to the concrete context the host module supplies. The same extension may be bound to multiple modules in the same form.
19. Extensions must not cache state between entity invocations.
20. `OperationCanceledException` must propagate — extensions must not swallow cancellation.
21. Extension `Name` must be unique within its module.

### Adapter rules

22. Adapters carry both read (export) and write (import) methods in one type.
23. Adapters own SDK mechanics only — no orchestration, no sequencing, no transformation logic.
24. TFS connectors that do not support a concern omit the adapter registration. The capability flag is the guard — not a null check in the extension.
25. `Get*` methods must not throw for an entity with no data — return empty sequence or empty list.
26. `Update*` methods must throw a domain exception if the target entity does not exist (extension handles this with a structured warning).

### Tool rules

27. A Tool is a **singleton service with one central config for the entire run**, declared once at the `MigrationPlatform.Tools.*` config root. There is one instance, shared by every consumer — contrast with an extension, which is instantiated with its own custom config. (Tool = singleton + single run-wide config; Extension = own custom config per instance.)
28. A Tool **may perform I/O** (network, identity/cache lookups, package-mediated reads) when that is how it provides its service — the former "tools are pure / no I/O" rule is **rescinded**. What defines a tool is rule 27 (one singleton, one run-wide central config), not purity. A tool may hold run-wide derived state (e.g. a Prepare-phase translation cache) and must be safe for concurrent use by its many consumers; it must not carry per-consumer/per-call mutable state.
29. Tools are a separate category from extensions: a tool **provides a service** consumed directly via DI; an extension **extends behaviour** and may call tools. Tools are not wrapped per-module and are not entries in any module's extension list. `INodeTranslationTool`, `IIdentityTranslationTool`, and field-transform are Tools — not extensions.

### PackageAccess rules

30. All package reads and writes go through `IPackageAccess` — no direct filesystem access anywhere in the hierarchy.
31. The package is the boundary between export and import — it is the source of truth.

---

## Violation Conditions

Any of the following constitutes an architectural violation requiring correction before merge:

- A layer has separate export-only and import-only types (e.g. `ITeamBoardSource` + `ITeamBoardTarget`)
- A module contains an entity loop or checkpoint logic
- An orchestrator is split by phase (e.g. `{Domain}ExportOrchestrator` + `{Domain}ImportOrchestrator`)
- An adapter is split by direction
- A tool carries per-consumer/per-call mutable state, or is unsafe for concurrent use by its consumers
- A tool is registered as anything other than a run-wide singleton, or carries per-consumer config
- A tool is wrapped as an extension or listed in a module's extension list
- A module calls a tool directly — modules are thin and delegate to their orchestrator (orchestrators and extensions may consume tools via DI)
- An extension reads enablement from a shared module-wide options object instead of its own `IOptions<T>`
- An `I{Domain}Extension` sub-interface is introduced instead of implementing `IModuleExtension` directly
- A mandatory extension is silently skipped instead of raising a fail-closed configuration error
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

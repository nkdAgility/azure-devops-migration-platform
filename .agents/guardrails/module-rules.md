# Module Template

Checklist for every new or modified module. All items mandatory unless marked optional.

---

## 1. Configuration Schema

- Options class in `Abstractions/Options/`: `<Module>Options` with `init`-only properties.
- Registered via `IOptions<T>` pattern.
- Validation: `IValidateOptions<T>` — fail fast on invalid config.

## 2. Package Folder Layout

- Module owns a single top-level folder (e.g. `WorkItems/`, `Teams/`, `Identities/`).
- Sub-folder structure documented; immutable once versioned.
- All reads/writes via `IArtefactStore` — never raw filesystem.

## 3. Cursor / Checkpoint

- File: `/{org}/{project}/.migration/{action}.{module}.cursor.json`.
- Fields: `lastProcessed`, `stage`, `updatedAt`.
- Resume: skip folders ≤ cursor lexicographically. Resume incomplete stage.
- First run: cursor absent → start from beginning.

## 4. IModule Implementation — Three-Layer Pattern

All modules follow the mandatory **Module → Orchestrator → Service** pattern:

```
Module (thin wrapper: ~100–130 lines)
  → Orchestrator (business logic: checkpointing, progress, metrics, enumeration, resume)
    → Service / Source / Target (connector-specific SDK/API calls)
```

### Module Layer (thin wrapper)
- Implements `IModule`.
- Properties: `Name`, `DependsOn` (explicit module dependencies), `SupportsInventory`, `SupportsExport`, `SupportsPrepare`, `SupportsImport`.
- Guard checks: is module enabled? are required services registered?
- Resolves config and endpoints, then delegates to the orchestrator.
- Contains **no business logic** — only config resolution and null checks.
- Constructor-injected: `IOptions<T>`, `ILogger<T>`, `ISourceEndpointInfo`, `ITargetEndpointInfo`, orchestrator interface, connector services.

### Orchestrator Layer (business logic)
- Interface declared in `Abstractions.Agent` (e.g. `INodesOrchestrator`, `ITeamsOrchestrator`).
- Implementation is `internal sealed` in `Infrastructure.Agent`.
- Registered as a singleton in DI (stateless between calls — all operation state passed via method parameters).
- Handles: checkpointing (cursor read/write via `ICheckpointingServiceFactory`), progress events (`IProgressSink`), metrics (OTel `ActivitySource` + `IMigrationMetrics`), CSV/JSON writing, enumeration loops, resume logic.
- Receives connector services as method parameters (not constructor-injected), so the same orchestrator can work with any connector.
- Must consume canonical capability seams (tools/services) for concern logic; must not implement parallel engines for concerns already owned by a seam.

### Service Layer (external calls)
- Connector-specific SDK/API calls behind abstraction interfaces (e.g. `ITeamSource`, `IIdentitySource`, `IClassificationTreeCapture`).
- One implementation per connector (AzureDevOps, TFS, Simulated).
- Injected into the module by DI, passed to the orchestrator at call time.
- Extension or policy adapters in this layer must stay thin (application policy/orchestration only) and must not become alternate concern engines.

## 5. Inventory

- `InventoryAsync`: source-side counting/cataloguing phase.
- Write per-module inventory artefacts via `IArtefactStore` (for example `<Module>/inventory.json`).
- Emit start/per-item/completion progress and metrics.

## 6. Validate

- `ValidateAsync`: pre-flight checks. Returns validation result (not exceptions).
- Checks: required config present, source reachable (export), artefacts readable (import), dependencies satisfied.

## 7. Prepare

- `PrepareAsync`: reads package, connects to target, writes `<Module>/prepare-report.json`.
- Idempotent. Does NOT connect to source. Does NOT modify user-edited mapping files.

## 8. Identity Mapping (if applicable)

- Use `IIdentityMappingService` — never resolve identities directly.
- Record unmapped identities in `Identities/unresolved.json`.

## 9. Tests

| Category | Required |
|----------|----------|
| Unit tests for all logic paths | Yes |
| Feature tests (Reqnroll) for key behaviours | Yes |
| SystemTest_Simulated (end-to-end, no network) | Yes |
| Connector coverage: Simulated + AzureDevOps + TFS (where API allows) | Yes |

Export tests MUST assert artefact exists AND content is non-empty.
Import tests MUST assert target received data (count > 0).

## 10. Documentation

- XML doc-comments on public API surface.
- Module listed in `docs/module-development-guide.md`.
- ADR if design decisions non-obvious.

## 11. Connector Coverage

Every module feature MUST be implemented for:
- **Simulated** — deterministic, no network, ≥ 2 items per operation.
- **AzureDevOps** — real SDK calls via `IAzureDevOpsClientFactory`.
- **TFS** — real SDK calls in net481 agent (where API supports).

Stubs, placeholders, or deferral to follow-up PRs = reject.

## 12. Observability (O-1 through O-5)

| ID | Requirement |
|----|-------------|
| O-1 | `ActivitySource.StartActivity` span with meaningful tags on every operation |
| O-2 | `IMigrationMetrics` calls: attempt, completion, error, duration, in-flight |
| O-3 | Structured logging: `Information` on start/end, `Warning` on skips/errors, `Debug` per-item |
| O-4 | `IProgressSink` injected (optional), `ProgressEvent` emitted at start, per-item/batch (≤50), completion |
| O-5 | `WorkItemFetchScope.Progress` and every `IWorkItemDiscoveryService` call wired to `IProgressSink.Emit` — `null` only for documented exceptions |

Module counter added to `MigrationCounters` → MUST have row in `QueueCommand.BuildProgressRenderable`.

## 13. DI Wiring

- Extension method: `AddXxxModuleServices(this IServiceCollection)`.
- Register orchestrator as singleton: `services.AddSingleton<IXxxOrchestrator, XxxOrchestrator>();`
- Register module as transient: `services.AddTransient<IModule, XxxModule>();`
- Registered in host startup (Agent) and test harness.
- No service locator. No static state. No ambient context.

---

## Quick Reject

Reject module if:
- Does not follow Module → Orchestrator → Service pattern.
- Orchestrator interface missing from `Abstractions.Agent`.
- Module contains business logic (checkpointing, enumeration, metrics) instead of delegating to orchestrator.
- Orchestrator instantiated via `new` instead of DI injection.
- Missing any of O-1..O-5.
- Connector stub/placeholder remains.
- Test asserts only "no exception" or `count >= 0`.
- Raw filesystem instead of `IArtefactStore`.
- State outside root `.migration/` or project `/{org}/{project}/.migration/`.
- Missing `DependsOn` declaration.
- `ExportAsync`/`ImportAsync` completes with count=0 without emitting Warning log.

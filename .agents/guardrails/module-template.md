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

- File: `.migration/Checkpoints/<module>.cursor.json`.
- Fields: `lastProcessed`, `stage`, `updatedAt`.
- Resume: skip folders ≤ cursor lexicographically. Resume incomplete stage.
- First run: cursor absent → start from beginning.

## 4. IModule Implementation

- Implements `IModule` (or derived: `IExportModule`, `IImportModule`).
- Properties: `Name`, `DependsOn` (explicit module dependencies).
- Methods: `ValidateAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`.
- Constructor-injected: `IArtefactStore`, `IStateStore`, `IOptions<T>`, `ILogger<T>`, `IMigrationMetrics`, `IProgressSink` (optional), connector interfaces.

## 5. Validate

- `ValidateAsync`: pre-flight checks. Returns validation result (not exceptions).
- Checks: required config present, source reachable (export), artefacts readable (import), dependencies satisfied.

## 6. Prepare

- `PrepareAsync`: reads package, connects to target, writes `<Module>/prepare-report.json`.
- Idempotent. Does NOT connect to source. Does NOT modify user-edited mapping files.

## 7. Identity Mapping (if applicable)

- Use `IIdentityMappingService` — never resolve identities directly.
- Record unmapped identities in `Identities/unresolved.json`.

## 8. Tests

| Category | Required |
|----------|----------|
| Unit tests for all logic paths | Yes |
| Feature tests (Reqnroll) for key behaviours | Yes |
| SystemTest_Simulated (end-to-end, no network) | Yes |
| Connector coverage: Simulated + AzureDevOps + TFS (where API allows) | Yes |

Export tests MUST assert artefact exists AND content is non-empty.
Import tests MUST assert target received data (count > 0).

## 9. Documentation

- XML doc-comments on public API surface.
- Module listed in `docs/modules.md`.
- ADR if design decisions non-obvious.

## 10. Connector Coverage

Every module feature MUST be implemented for:
- **Simulated** — deterministic, no network, ≥ 2 items per operation.
- **AzureDevOps** — real SDK calls via `IAzureDevOpsClientFactory`.
- **TFS** — real SDK calls in net481 agent (where API supports).

Stubs, placeholders, or deferral to follow-up PRs = reject.

## 11. Observability (O-1 through O-4)

| ID | Requirement |
|----|-------------|
| O-1 | `ActivitySource.StartActivity` span with meaningful tags on every operation |
| O-2 | `IMigrationMetrics` calls: attempt, completion, error, duration, in-flight |
| O-3 | Structured logging: `Information` on start/end, `Warning` on skips/errors, `Debug` per-item |
| O-4 | `IProgressSink` injected (optional), `ProgressEvent` emitted at start, per-item/batch (≤50), completion |

Module counter added to `MigrationCounters` → MUST have row in `QueueCommand.BuildProgressRenderable`.

## 12. DI Wiring

- Extension method: `AddXxxModuleServices(this IServiceCollection)`.
- Registered in host startup (Agent) and test harness.
- No service locator. No static state. No ambient context.

---

## Quick Reject

Reject module if:
- Missing any of O-1..O-4.
- Connector stub/placeholder remains.
- Test asserts only "no exception" or `count >= 0`.
- Raw filesystem instead of `IArtefactStore`.
- State outside `.migration/Checkpoints/`.
- Missing `DependsOn` declaration.
- `ExportAsync`/`ImportAsync` completes with count=0 without emitting Warning log.

# System Architecture — Hard Guardrails

Non-negotiable rules distilled from [docs/](../../docs/). These rules override `/docs` in any conflict. Binding entry point: [agents.md](../../agents.md).

## Guardrail Challenge Protocol

If a rule below forces a **clearly worse outcome**: Stop → Cite rule number → Propose amendment → Ask human → Wait for decision. Silent workaround = violation. Blind compliance with harmful rule = negligence.

---

## Absolute Rules

1. **WorkItems layout is canonical.** `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` — no renaming, reordering, or flattening.

2. **Import must be streaming.** One revision folder at a time. Loading all revisions into memory is forbidden.

3. **No global in-memory sort.** Enumeration order = lexicographic folder traversal. In-memory sorting of `EnumerateAsync` results is forbidden.

4. **Cursor-based checkpoints required.** Every module: `.migration/Checkpoints/`. No watermark tables, databases, or in-memory progress tracking.

5. **Attachments beside revision.json.** No global `Attachments/` root. No mandatory blob store.

6. **No source-to-target direct migration.** Source → Package → Target. Always.

7. **Modules only through IArtefactStore and IStateStore.** No direct filesystem access, no source/target API calls outside export/import context, no global shared state.

8. **Identity is cross-cutting.** All modules use `IIdentityMappingService`. `IdentitiesModule` completes before any identity-mapping module.

9. **Config/schema versioning with upgrader.** Breaking changes require version increment + upgrader.

10. **Prepare before Import.** Import checks `.migration/Checkpoints/prepare.complete.json`. Absent → auto-run Prepare. Blocking issue → abort. In `Migrate` mode: Export → Prepare → Import; blocking issues abort after Prepare. See [docs/validation.md](../../docs/validation.md).

11. **ControlPlane must not execute migrations.** Accepts, stores, assigns jobs only. No source/target API calls, no orchestrator logic, no package I/O, no secret unwrapping. `ControlPlaneHost` manages agent lifecycle but contains no job execution logic.

12. **Agents are stateless; all durable state in the package.** All state lives in `.migration/Checkpoints/`, `.migration/Logs/`, revision folders via `IArtefactStore`/`IStateStore`. Crashed agent → new agent resumes from cursor.

13. **IArtefactStore is the only file abstraction.** Module code must not reference `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly. Switching local↔cloud = zero module code changes.

14. **EnumerateAsync must be lexicographic.** Both stores return strict ascending lexicographic order. Out-of-order enumeration breaks streaming import. In-memory sorting forbidden.

15. **Job contract is the unit of work exchange.** CLI → job contract → control plane → agent. TUI is a pure progress viewer — never submits jobs.

16. **CLI must not contain migration logic.** CLI parses args, builds `MigrationJob`, submits to control plane via HTTP. `Standalone` mode → starts `LocalStackHost` in-process. `Hosted` mode → connects to `ControlPlane.BaseUrl`. CLI never calls modules, writes cursors, or accesses `IArtefactStore`. Infrastructure: host builder pattern, `Program.cs` < 50 lines, DI in dedicated host builder class.

17. **Job Engine hostable independently of TUI.** No console/UI dependency. Receives `MigrationJob` + `IProgressSink`; produces package output + cursor state. Runnable in-process, in container, or in test harness.

18. **No UI coupling in Job Engine or modules.** No `Console`, no `System.Console`, no interactive input. All output via `IProgressSink` or `IArtefactStore` (`.migration/Logs/`).

19. **TFS Object Model runs in dedicated net481 agent (`TfsMigrationAgent`).** First-class polling agent using same HTTP lease protocol as MigrationAgent. Dispatches via `IModule` (`TfsJobAgentWorker`). Uses same `IArtefactStore`/`IStateStore`/`IProgressSink`. Windows-only. `AgentLifecycleService` spawns on Windows, skips elsewhere. No compiled reference from .NET 10 projects. See [docs/migration-agent.md](../../docs/migration-agent.md#tfs-migration-agent).

20. **Execution path is always CLI → ControlPlaneHost → MigrationAgent.** Uses EF Core for persistence. No topology bypasses the control plane. Topologies: Local/Server (CLI drives Aspire, localhost, `file:///`), Cloud Self-Hosted (customer Azure via `azd up`), Cloud Managed (same stack, managed). Test isolation via `DEVOPS_MIGRATION_INFRA`: `portable` (bundled PG + filesystem) and `docker` (Docker PG + Azurite). See [docs/control-plane.md](../../docs/control-plane.md), [docs/aspire-integration.md](../../docs/aspire-integration.md).

21. **Mandatory reuse of existing architecture.** Use `WorkItemExportOrchestrator`/`IWorkItemRevisionSource` for work item processing. Use `IArtefactStore.EnumerateAsync()` for traversal. Use `ICheckpointingService`/`IStateStore` for progress. Stream binaries via `IArtefactStore.WriteBinaryAsync()`/`IAttachmentBinarySource`. New abstractions only when: no existing abstraction covers the use case, defined in `Abstractions`/`Abstractions.Agent`/`Abstractions.ControlPlane`, used by ≥2 modules, motivation documented.

22. **No architectural workarounds without explicit user acceptance.** Adapters, shims, deferred markers, lossy conversions forbidden unless human explicitly approves in-session. Agent must present workaround + proper fix, wait for response, record decision in `discrepancies.md`.

23. **Only Migration Agent (and TFS Export Agent) may write to working directory/package.** CLI, TUI, ControlPlane have no write access. Data residency constraint. CLI read-only access for post-job display is permitted. **Exception (feature 025-agent-config-package, approved 2026-04-29):** The CLI MAY write `migration-config.json` to the package root as a pre-submission step before calling the control plane. This is the only package write permitted from the CLI. Rationale: the config must exist in the package before the job is dispatched so the agent can read it; the agent cannot write it before it has received the job.

24. **Module/Tool identifiers derived from class name.** `{Stem}Module`: `Name` = `"{Stem}"`, config = `MigrationPlatform:Modules:{Stem}`, cursor key = `{Stem}`, file = `{Stem}Module.cs`. `{Stem}Tool`: folder = `Tools/{Stem}/`, file = `{Stem}Tool.cs`, DI = `{Stem}ToolServiceCollectionExtensions.cs`, interface = `I{Stem}Tool`, options = `{Stem}Options`, config = `MigrationPlatform:Tools:{Stem}`. Any deviation = instant reject.

25. **⛔ Full observability mandatory on every module and tool.**
    - **O-1 Traces:** `using var activity = ActivitySource.StartActivity(...)` with tags. Source: `WellKnownActivitySourceNames.Migration`.
    - **O-2 Metrics:** `IMigrationMetrics` for attempt, completion, error, duration, in-flight. Constants → `WellKnownMetricNames`.
    - **O-3 Logging:** `Information` start/end; `Warning` skips/errors; `Debug` per-item. Structured params only. Customer data → `DataClassification.Customer` scope.
    - **O-4 ProgressEvent:** `IProgressSink` (optional). Emit at start, per-item/batch ≤50, completion. `Metrics.Migration.{ModuleName}` populated. `BuildProgressRenderable` renders: Identities → Nodes → Teams → WorkItems.
    - **⚠️ `ProgressEvent.Metrics` is null for .NET 10 agents.** CLI reads from `GET /jobs/{id}/telemetry`, NOT from `ProgressEvent.Metrics`.

---

## Reference

Consult [docs/architecture.md](../../docs/architecture.md). Default: preserve package layout, maintain streaming, write state only through defined interfaces.

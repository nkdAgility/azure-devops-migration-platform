# System Architecture — Hard Guardrails

Non-negotiable rules distilled from [docs/](../../../docs/). These rules override `/docs` in any conflict. Binding entry point: [agents.md](../../../agents.md).

## Guardrail Challenge Protocol

If a rule below forces a **clearly worse outcome**: Stop → Cite rule number → Propose amendment → Ask human → Wait for decision. Silent workaround = violation. Blind compliance with harmful rule = negligence.

---

## Absolute Rules

1. **WorkItems layout is canonical.** `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` — no renaming, reordering, or flattening.

2. **Import must be streaming.** One revision folder at a time. Loading all revisions into memory is forbidden.

3. **No global in-memory sort.** Enumeration order = lexicographic folder traversal. In-memory sorting of `EnumerateAsync` results is forbidden.

4. **Cursor-based checkpoints required.** Every module writes project-scoped cursor files under `/{org}/{project}/.migration/`. No watermark tables, databases, or in-memory progress tracking.

5. **Attachments beside revision.json.** No global `Attachments/` root. No mandatory blob store.

6. **No source-to-target direct migration.** Source → Package → Target. Always.

7. **Modules use the package boundary (`IPackageAccess`) for package-facing operations.** No direct filesystem access, no source/target API calls outside export/import context, no global shared state.

8. **Identity is cross-cutting.** All modules use `IIdentityMappingService`. `IdentitiesModule` completes before any identity-mapping module.

9. **Config/schema versioning with upgrader.** Breaking changes require version increment + upgrader.

10. **Phase gates: Inventory before Export, Prepare before Import.** Export requires root `.migration/inventory.complete.json` (or auto-runs Inventory). Import requires root `.migration/prepare.complete.json` (or auto-runs Prepare). Blocking Prepare issues abort Import/Migrate. See [docs/validation.md](../../../docs/validation.md).

11. **ControlPlane must not execute migrations.** Accepts, stores, assigns jobs only. No source/target API calls, no orchestrator logic, no package I/O, no secret unwrapping. `ControlPlaneHost` manages agent lifecycle but contains no job execution logic.

12. **Agents are stateless; all durable state is package-backed.** Use root `.migration/` for package orchestration, `/{org}/{project}/.migration/` for project cursors, and `.migration/runs/<runId>/` for audit evidence only. Resume authority is root/project state, not run logs.

13. **IArtefactStore is the only file abstraction.** Module code must not reference `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly. Switching local↔cloud = zero module code changes.

14. **EnumerateAsync must be lexicographic.** Both stores return strict ascending lexicographic order. Out-of-order enumeration breaks streaming import. In-memory sorting forbidden.

15. **`Job` is the unit of work exchange.** CLI submits `Job` (`ConfigPayload`) to control plane; agent executes all kinds (`Inventory`, `Export`, `Prepare`, `Import`, `Validate`, `Migrate`, `Dependencies`). TUI is view-only and never submits jobs.

16. **CLI must not contain migration logic.** CLI builds/submits `Job` over HTTP and may start local hosts in Standalone mode. It never executes modules, writes cursors, or writes package artefacts.

17. **Job Engine is headless and hostable independently of TUI.** No console/UI dependency. Executes `JobTaskList` (module tasks per phase), with topological ordering inside phases and sequential phase execution.

18. **No UI coupling in Job Engine or modules.** No `Console`, no `System.Console`, no interactive input. All output via `IProgressSink` or `IArtefactStore` (`.migration/runs/<runId>/logs/`).

19. **TFS Object Model runs in dedicated net481 agent (`TfsMigrationAgent`).** First-class polling agent using same HTTP lease protocol as MigrationAgent. Dispatches via `IModule` (`TfsJobAgentWorker`). Uses same `IArtefactStore`/`IStateStore`/`IProgressSink`. Windows-only. `AgentLifecycleService` spawns on Windows, skips elsewhere. No compiled reference from .NET 10 projects. See [docs/agent-hosting.md](../../../docs/agent-hosting.md#tfs-migration-agent).

20. **Execution path is always CLI → ControlPlaneHost → MigrationAgent.** No topology bypasses the control plane. Supported topologies are local/server and cloud variants documented in [docs/control-plane.md](../../../docs/control-plane.md) and [docs/development-setup.md](../../../docs/development-setup.md).

20a. **Project reference boundaries are compiler-enforced (spec 021.2).** Permitted references only:
    - CLI → `Abstractions`, `Infrastructure` (not `Abstractions.Agent`, not `Abstractions.ControlPlane`)
    - ControlPlaneHost → `Abstractions`, `Abstractions.ControlPlane`, `Infrastructure`, `ControlPlane`
    - MigrationAgent → `Abstractions.Agent`, `Infrastructure.Agent`, `Infrastructure`
    - TfsMigrationAgent → `Abstractions.Agent`, `Infrastructure.Agent`, `Infrastructure.TfsObjectModel`
    - `TfsMigrationAgent` must **not** be referenced as a project dependency from any .NET 10 project.
    - No circular references permitted.

21. **Mandatory reuse of existing architecture.** Use `WorkItemExportOrchestrator`/`IWorkItemRevisionSource` for work item processing. Use `IArtefactStore.EnumerateAsync()` for traversal. Use `ICheckpointingService`/`IStateStore` for progress. Stream binaries via `IArtefactStore.WriteBinaryAsync()`/`IAttachmentBinarySource`. New abstractions only when: no existing abstraction covers the use case, defined in `Abstractions`/`Abstractions.Agent`/`Abstractions.ControlPlane`, used by ≥2 modules, motivation documented.

22. **No architectural workarounds without explicit user acceptance.** Adapters, shims, deferred markers, lossy conversions forbidden unless human explicitly approves in-session. Agent must present workaround + proper fix, wait for response, record decision in `discrepancies.md`.

23. **Only Migration Agent (and TFS Export Agent) may write package data.** CLI, TUI, and ControlPlane are read-only for package artefacts. Config travels in `Job.ConfigPayload`; the agent materializes `migration-config.json` after lease acquisition.

24. **Module/Tool/Analyser identifiers derive from class stem.** Keep naming/config/DI/file conventions aligned (`{Stem}Module`, `{Stem}Tool`, `{Stem}Analyser`) and section paths under `MigrationPlatform:*:{Stem}`.

25. **Observability is mandatory on every module and tool.** All O-1..O-5 requirements and channel separation rules are enforced by [observability-requirements.md](../domains/observability-requirements.md). Do not duplicate or bypass that contract.

26. **Capture handler registration and resolution must fail fast.** `BuildCaptureHandlers` must throw `ArgumentException` on any duplicate `ICapture.Name` (including module-backed captures and pure `ICapture` registrations). If a plan references an analyser, capture handler, module, or organisation endpoint that is not registered/resolved for the task, `JobPlanExecutor` must fail the task/job; log-and-skip is forbidden.

27. **`IOptions<T>` is the only runtime config injection pattern (spec 028).** `MigrationOptions` is serialisation-only. Every options class must declare `SectionName`, register via `AddSchemaEntry<T>()`, and keep `migration.schema.json` generated from DI registrations.

28. **Capability seams are canonical and singular.** Each concern has one canonical runtime seam and one public reusable contract surface. Modules, orchestrators, extensions, and analysers must consume that seam rather than introducing parallel runtime entry points or duplicate concern engines. Phase/slice policy belongs in thin adapters. See [capability-ethos-rules.md](../core/capability-ethos-rules.md).

29. **Guard clauses are compatibility-only.** Runtime guard clauses in module/orchestrator/service code are permitted only when required to preserve compatibility between `net481` and modern .NET targets (`net9.0`/`net10.0`). Defensive guards for nullable services, optional enablement toggles, or generic fail-fast checks are prohibited as local code guards and must be expressed through canonical validation surfaces instead. Non-compatibility guards discovered during touched-scope refactors must be removed. Current implementation gaps on `net481` are not a valid reason to add non-compatibility guards.

---

## Reference

Consult [docs/architecture.md](../../../docs/architecture.md). Default: preserve package layout, maintain streaming, write state only through defined interfaces.





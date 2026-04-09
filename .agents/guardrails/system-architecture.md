# System Architecture — Hard Guardrails

These rules are non-negotiable. They are distilled from the full reference set in [docs/](../../docs/). In any conflict between these rules and any documentation in `/docs`, **these rules win**. The docs define architectural intent; the `.agents/guardrails` files enforce it. The binding entry point is [agents.md](../../agents.md).

## Absolute Rules

1. **WorkItems chronological layout is canonical.**
   The folder structure `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` must not be altered. No renaming, reordering, or flattening.

2. **Import must be streaming.**
   Load and process one revision folder at a time. Loading all revisions into memory before processing is forbidden.

3. **No global in-memory sort.**
   Enumeration order is determined by lexicographic folder traversal. Sorting in memory defeats the purpose of the layout and breaks memory safety for large datasets.

4. **Cursor-based checkpoints are required.**
   Every module must maintain a cursor file under `Checkpoints/`. Watermark tables, databases, or in-memory progress tracking are not acceptable substitutes.

5. **Attachments are stored beside revision.json.**
   Attachment files live in the same folder as their `revision.json`. There is no global `Attachments/` root and no mandatory blob store.

6. **No source-to-target direct migration.**
   The system is a package platform. Source data is always written to the package first. Import always reads from the package. Direct source-to-target calls in any module are forbidden.

7. **Modules only through IArtefactStore and IStateStore.**
   Modules must not access the filesystem directly, call source/target APIs outside of the export/import context, or share state through globals.

8. **Identity is a cross-cutting service.**
   No module implements its own identity resolution. All modules use `IIdentityMappingService`. `IdentitiesModule` must complete before any module that maps identities.

9. **Config and schema versioning with upgrader.**
   Breaking changes to config or package schema require a version increment and a corresponding upgrader. There is no backwards compatibility without an upgrader.

10. **Validate before import.**
    In `Both` mode, a validation pass runs after export and before import. Import must not begin on a package that fails validation. Post-flight validation must also run after import. See [docs/validation.md](../../docs/validation.md) for the full check list and configuration.

11. **The `ControlPlane` service must not execute migrations.**
    The `ControlPlane` service library accepts, stores, and assigns jobs. It does not call source or target APIs, run orchestrator logic, read or write the migration package, or unwrap secrets. `ControlPlaneHost` extends the control plane with agent lifecycle management but must not contain job execution logic. Violations break the separation between coordination (`ControlPlane`/`ControlPlaneHost`) and execution (`Agent`).

12. **Agents are stateless; all durable state is in the package.**
    Agents hold no state beyond the current lease. All durable state lives in the package (`Checkpoints/`, `Logs/`, revision folders) via `IArtefactStore` and `IStateStore`. An Agent that crashes is replaced by a new Agent that resumes from the cursor.

13. **IArtefactStore is the only permitted file abstraction.**
    Both `FileSystemArtefactStore` and `AzureBlobArtefactStore` implement `IArtefactStore`. Module code must not reference either implementation directly. Switching from local to cloud mode must require zero module code changes.

14. **EnumerateAsync must be lexicographic.**
    Both artefact store implementations must return results from `EnumerateAsync` in strict lexicographic (ascending) order. Out-of-order enumeration breaks streaming import and chronological replay. In-memory sorting of enumerated paths is forbidden.

15. **The job contract is the unit of work exchange.**
    The CLI converts a local config file into a job contract before submission. The control plane stores and forwards the job contract. The Migration Agent executes it. Nothing else is permitted as an inter-component work handoff. The TUI is a pure progress viewer — it never submits jobs or builds job contracts.

16. **The CLI must not contain migration logic.**
    The CLI parses arguments, builds a `MigrationJob`, and submits it to the control plane via `ControlPlaneClient`. When no `MIGRATION_API_URL` is set, the CLI uses **embedded Aspire `DistributedApplication` APIs** (not the `AppHost` project) to start `ControlPlaneHost`, `MigrationAgent`(s), and PostgreSQL in-process on the local machine before submitting the job. When `MIGRATION_API_URL` is set, the CLI connects directly to the specified remote endpoint. In both cases the CLI communicates with the control plane exclusively via HTTP — it never calls modules, writes cursors, or accesses `IArtefactStore` outside the Job Engine boundary. The `AppHost` project is the `azd up` provisioning target and developer standalone convenience tool — it is not in the CLI execution path.
    
    **CLI Infrastructure Pattern**: CLI infrastructure setup must follow the host builder pattern, with `Program.cs` limited to bootstrapping logic (< 50 lines) and commands managing their hosting lifecycle through dependency injection. All DI container setup, service registration, and infrastructure configuration must be centralized in a dedicated host builder class (e.g., `MigrationPlatformHost`). Commands must inherit from a base class providing `IServiceProvider` and `IHostApplicationLifetime` access, enabling proper separation between bootstrapping, infrastructure setup, and command logic.

17. **The Job Engine must be hostable independently of the TUI.**
    The Job Engine has no dependency on any console, UI framework, or interactive terminal. It receives a `MigrationJob` and an `IProgressSink`; it produces package output and cursor state. It must be runnable in-process (local), in a container (Migration Agent), or in a test harness without modification.

18. **No UI coupling in the Job Engine or modules.**
    The Job Engine and all modules must not write to `Console`, reference `System.Console`, or use any interactive input mechanism. All output goes through `IProgressSink` (progress events) or `IArtefactStore` (logs written to `Logs/`). Any violation makes the engine unrunnable as a Migration Agent.

19. **TFS Object Model runs in an isolated subprocess spawned by the CLI only.**
    The .NET 4.x TFS exporter (`DevOpsMigrationPlatform.CLI.TfsMigration`) is a completely separate binary invoked **directly by the CLI** (`TfsExportCommand` in `CLI.Migration`). It is not routed through ControlPlane or MigrationAgent — TFS OM cannot run in Docker, so this is a CLI-only operation for all topologies. `TfsExportAgent` uses the same `IArtefactStore`, `IStateStore`, and `IProgressSink` abstractions as the .NET 10 `MigrationAgent` — these interfaces are defined in the multi-targeted `DevOpsMigrationPlatform.Abstractions` (`net481;net10.0`). The `StdoutProgressSink` (net481) writes NDJSON progress events to stdout; the CLI reads these via `TfsExporterProcessAdapter` and streams them to the terminal. The .NET 10 host must never hold a compiled reference to the .NET 4 project. The only permitted caller of the subprocess is `ExternalToolRunner` in `DevOpsMigrationPlatform.CLI.Migration` — a generic, TFS-agnostic process bridge. `TfsExporterProcessAdapter` in `CLI.Migration` is the only permitted TFS-aware .NET 10 class; it translates NDJSON stdout lines into progress events for the CLI. See [docs/tfs-exporter.md](../../docs/tfs-exporter.md) for the full protocol specification.

20. **The control plane data store is PostgreSQL in all environments — no substitutions.**
    PostgreSQL (via EF Core + Npgsql) is the only permitted data store for the control plane. It is present across **all** hosting topologies. The execution path is **always** `CLI → ControlPlaneHost → MigrationAgent`. There is no topology in which the CLI executes migration logic directly or bypasses the control plane. From the user's perspective a local run appears seamless (single CLI command), but `ControlPlaneHost`, `MigrationAgent`(s), and PostgreSQL are still started by Aspire and used — they are transparent, not absent.
    - **Local / Dedicated Server** (CLI-driven, single machine): the CLI drives Aspire programmatically to start `ControlPlaneHost` and `MigrationAgent`(s); PostgreSQL runs as an Aspire portable binary resource (no Docker, no installer). `ControlPlaneHost` manages Migration Agent lifecycle. All services bind to localhost. Package storage is `file:///`. This topology is sometimes called "standalone" in conversation — it still uses the full control plane stack.
    - **Cloud Self-Hosted** (customer Azure subscription): the same Azure stack as Managed — Container Apps, PostgreSQL Flexible Server, Blob Storage — provisioned by the customer via `azd up` in their own Azure subscription.
    - **Cloud Managed** (`azd up`, Azure): the same stack runs in Azure Container Apps; PostgreSQL Flexible Server provisioned automatically from the same AppHost declaration.
    No SQLite fallback, no in-memory database, and no alternative provider are permitted for the control plane in any of these topologies — including tests and CI. Test isolation is achieved using the **Local/Server AppHost profile**, which supports two subprofiles selected via `DEVOPS_MIGRATION_INFRA`: `portable` (validates local/server: bundled PG binary + filesystem store, no Docker) and `docker` (validates cloud: Docker PostgreSQL + Azurite). Both subprofiles must pass in every CI pipeline stage. See [docs/control-plane.md](../../docs/control-plane.md) for the table schema and [docs/aspire-integration.md](../../docs/aspire-integration.md) for all AppHost profiles.

Consult [docs/architecture.md](../../docs/architecture.md). If the answer is not there, the safest default is to preserve the package layout, maintain streaming behaviour, and write state only through the defined interfaces.

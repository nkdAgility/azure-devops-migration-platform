# System Architecture — Hard Guardrails

These rules are non-negotiable. They are distilled from the full reference set in [docs/](../../docs/). In any conflict between these rules and any documentation in `/docs`, **these rules win**. The docs define architectural intent; the `ai/guardrails` files enforce it. The binding entry point is [agents.md](../../agents.md).

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

11. **Control plane must not execute migrations.**
    The control plane (ASP.NET Core API) accepts, stores, and assigns jobs. It does not call source or target APIs, run orchestrator logic, read or write the migration package, or unwrap secrets. Violations break the separation between the control plane and Migration Agents.

12. **Migration Agents are stateless; all durable state is in the package.**
    Migration Agents hold no state beyond the current lease. All durable state lives in the package (`Checkpoints/`, `Logs/`, revision folders) via `IArtefactStore` and `IStateStore`. A Migration Agent that crashes is replaced by a new Migration Agent that resumes from the cursor.

13. **IArtefactStore is the only permitted file abstraction.**
    Both `FileSystemArtefactStore` and `AzureBlobArtefactStore` implement `IArtefactStore`. Module code must not reference either implementation directly. Switching from local to cloud mode must require zero module code changes.

14. **EnumerateAsync must be lexicographic.**
    Both artefact store implementations must return results from `EnumerateAsync` in strict lexicographic (ascending) order. Out-of-order enumeration breaks streaming import and chronological replay. In-memory sorting of enumerated paths is forbidden.

15. **The job contract is the unit of work exchange.**
    The TUI converts a local config file into a job contract before submission. The control plane stores and forwards the job contract. The Migration Agent executes it. Nothing else is permitted as an inter-component work handoff.

16. **The TUI must not contain migration logic.**
    The TUI parses arguments, builds a `MigrationJob`, chooses a transport (`LocalJobRunner` or `ControlPlaneClient`), and renders progress. All migration logic — module execution, cursor writes, artefact reads/writes — lives in the Job Engine. A TUI that calls a module directly, writes a cursor, or accesses `IArtefactStore` outside the Job Engine boundary is in violation.

17. **The Job Engine must be hostable independently of the TUI.**
    The Job Engine has no dependency on any console, UI framework, or interactive terminal. It receives a `MigrationJob` and an `IProgressSink`; it produces package output and cursor state. It must be runnable in-process (local), in a container (Migration Agent), or in a test harness without modification.

18. **No UI coupling in the Job Engine or modules.**
    The Job Engine and all modules must not write to `Console`, reference `System.Console`, or use any interactive input mechanism. All output goes through `IProgressSink` (progress events) or `IArtefactStore` (logs written to `Logs/`). Any violation makes the engine unrunnable as a Migration Agent.

19. **TFS Object Model runs in an isolated subprocess only.**
    The .NET 4.x TFS exporter (`DevOpsMigrationPlatform.TfsExporter`) is a completely separate binary. The .NET 10 host communicates with it exclusively via the process bridge protocol: config JSON on stdin, NDJSON progress lines on stdout, unstructured errors on stderr, a cancellation sentinel file, and exit code. The .NET 10 host must never hold a compiled reference to the .NET 4 project, link against any .NET Framework assembly, or invoke TFS OM APIs in any form. The adapter (`TfsExporterProcessAdapter`) is the only permitted caller of the subprocess. See [docs/tfs-exporter.md](../../docs/tfs-exporter.md) for the full protocol specification.

20. **The control plane data store is PostgreSQL in all environments — no substitutions.**
    PostgreSQL (via EF Core + Npgsql) is the only permitted data store for the control plane. It is present in two of the three operational modes:
    - **Standalone mode** (Aspire AppHost, single machine): the full stack — control plane, migration agent, and PostgreSQL — runs on one local machine. PostgreSQL is a portable bundled binary launched as a child process (`AddPortablePostgres`). No Docker, no installer. All services bind to localhost. Package storage is `file:///`.
    - **Self-Hosted mode** (customer Azure subscription): the same Azure stack as Managed — Container Apps, PostgreSQL Flexible Server, Blob Storage — provisioned by the customer via `azd up` in their own Azure subscription.
    - **Managed mode** (`azd up`, Azure): the same stack runs in Azure Container Apps; PostgreSQL Flexible Server provisioned automatically from the same AppHost declaration.
    No SQLite fallback, no in-memory database, and no alternative provider are permitted for the control plane in any of these modes — including tests and CI. Test isolation is achieved using the **Development AppHost profile**, which supports two subprofiles selected via `DEVOPS_MIGRATION_INFRA`: `portable` (validates Standalone: bundled PG binary + filesystem store, no Docker) and `docker` (validates Self-Hosted/Managed: Docker PostgreSQL + Azurite). Both subprofiles must pass in every CI pipeline stage. See [docs/control-plane.md](../../docs/control-plane.md) for the table schema and [docs/aspire-integration.md](../../docs/aspire-integration.md) for all AppHost profiles.

Consult [docs/architecture.md](../../docs/architecture.md). If the answer is not there, the safest default is to preserve the package layout, maintain streaming behaviour, and write state only through the defined interfaces.

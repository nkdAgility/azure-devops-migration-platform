<!--
SYNC IMPACT REPORT
==================
Version change:    1.1.0 → 1.2.0
Bump rationale:    Principle IX added — SOLID Design, Dependency Injection, and IOptions
                   configuration model are now non-negotiable architectural constraints.

Principles modified:
  None

Principles added:
  IX.  SOLID Design & Dependency Injection

Sections modified:
  Technology Stack — DI and IOptions entry added
  Reject Conditions — DI violation conditions added

Templates updated:
  ✅ .specify/templates/plan-template.md — Constitution Check gate IX added
  ✅ .specify/templates/spec-template.md — no changes required
  ✅ .specify/templates/tasks-template.md — no changes required

Deferred TODOs:
  None
-->

# Azure DevOps Migration Platform Constitution

## Core Principles

### I. Package-First Migration (NON-NEGOTIABLE)

The platform MUST follow the pattern **Source → Files → Target** in all modes
(Export, Import, Both). Direct source-to-target migration is categorically
forbidden.

- Export writes only to the on-disk package via `IArtefactStore`.
- Import reads only from the on-disk package via `IArtefactStore`.
- No module may call target APIs from within export logic, or source APIs from
  within import logic.
- The package is the single source of truth and the audit trail.

### II. Streaming Import & Memory Safety (NON-NEGOTIABLE)

Import MUST process exactly one revision folder at a time. Loading all
revisions — or all work items — into memory before processing is forbidden.

- `EnumerateAsync` results MUST be consumed lazily; in-memory sorting of
  enumerated paths is forbidden.
- Both `FileSystemArtefactStore` and `AzureBlobArtefactStore` MUST return
  results in strict lexicographic (ascending) order.
- A module that materialises all revisions into a list or array before
  processing is in violation and MUST be rejected.

### III. Canonical WorkItems Layout (NON-NEGOTIABLE)

The WorkItems folder structure is the streaming import contract. It MUST NOT
be altered, renamed, flattened, or reordered.

- Date folder: `yyyy-MM-dd` (ISO 8601, UTC, zero-padded).
- Revision folder: `<ticks>-<workItemId>-<revisionIndex>` (all three segments,
  hyphen-separated, no padding).
- Each revision folder contains `revision.json` and any attachment files
  beside it. There is no global `Attachments/` root.
- Import order is derived from lexicographic enumeration only.

**Valid example:** `WorkItems/2026-02-25/638760123456789012-12345-17/`

### IV. Cursor-Based Checkpointing & Resumability

Every module MUST maintain a forward-only cursor file under `Checkpoints/`.
Watermark tables, in-memory counters, and database-backed progress tracking
are not acceptable substitutes.

- Cursor file path: `Checkpoints/<modulename>.cursor.json`.
- The `lastProcessed` field holds the relative path of the last completed
  revision folder or equivalent key — never an ID or timestamp alone.
- Valid `stage` values for WorkItems: `CreatedOrUpdated`, `AppliedFields`,
  `AppliedLinks`, `UploadedAttachments`, `Completed`.
- On resume, begin from the stage after `lastProcessed`, not from Stage A.
- `idmap.db` under `Checkpoints/` is the idempotency record for work item ID
  and attachment ID mappings.

### V. Module Isolation via Abstractions

Modules MUST interact with persistent storage only through `IArtefactStore`
and `IStateStore`. No module may access the filesystem directly.

- `IArtefactStore` is the only permitted file abstraction. Module code MUST NOT
  reference `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly.
- Identity resolution is a cross-cutting shared service. No module implements
  its own identity resolution. All modules MUST use `IIdentityMappingService`.
- `IdentitiesModule` MUST complete before any module that maps identities
  (declared via `DependsOn`).
- Switching from local to cloud storage MUST require zero module code changes.

### VI. Separation of Planes

The control plane, Job Engine, TUI, and TFS exporter subprocess are strictly
isolated layers with no permitted cross-layer coupling.

- **Control plane** accepts, stores, and assigns jobs. It MUST NOT execute
  migration logic, call source or target APIs, read or write the package, or
  unwrap secrets.
- **Job Engine** receives a `MigrationJob` and an `IProgressSink`; it produces
  package output and cursor state. It MUST be hostable in-process, in a
  container, or in a test harness without modification. It MUST NOT write to
  `Console` or reference any interactive terminal.
- **TUI** parses arguments, builds a `MigrationJob`, chooses a transport, and
  renders progress. It MUST NOT contain migration logic, call modules directly,
  write cursors, or access `IArtefactStore` outside the Job Engine boundary.
- **TFS Object Model** runs in an isolated .NET 4.8 subprocess only, invoked
  exclusively via `TfsExporterProcessAdapter`. No .NET 10 project may hold a
  compiled reference to the .NET 4.8 project. Communication is stdin/stdout
  NDJSON + exit code only.

### VII. Determinism & Idempotency

Re-running Export with the same inputs MUST produce a package with stable
ordering and a compatible layout. Re-running Import MUST be safe to retry.

- Config and package schema breaking changes require a version increment and a
  corresponding upgrader. There is no backwards compatibility without an
  upgrader.
- `manifest.json` MUST include `packageVersion`, `toolVersion`, `runId`, and
  `configHash`.
- Import idempotency is enforced via `Checkpoints/idmap.db` (ID mappings) and
  cursor state (stage progress). A crashed Migration Agent is replaced by a
  new one that resumes from the cursor without data loss.

### IX. SOLID Design & Dependency Injection (NON-NEGOTIABLE)

All production code MUST follow SOLID design principles and use constructor
injection via `Microsoft.Extensions.DependencyInjection`. Configuration MUST
flow through the `IOptions<T>` model — never raw `IConfiguration` in services.

- Every service, repository, and orchestrator MUST receive all dependencies
  via constructor parameters. Service-locator calls, ambient statics, and
  `new` construction of registered services inside production code are
  forbidden.
- Configuration MUST be consumed as `IOptions<TOptions>` or
  `IOptionsSnapshot<TOptions>`. Direct reads of `IConfiguration`,
  `Environment.GetEnvironmentVariable`, or `appsettings.json` keys inside
  service or module code are forbidden.
- Options classes MUST be `sealed`, use `init`-only properties, and declare a
  `public static string SectionName` constant that matches the JSON section
  key. Validation attributes (`[Required]`, `[Url]`, etc.) MUST be applied
  where applicable.
- Each logical group of services MUST be registered via a dedicated
  `IServiceCollection` extension method (e.g.,
  `AddWorkItemExportServices(this IServiceCollection)`). Inline `services.Add*`
  calls scattered across `Program.cs` are forbidden.
- Interfaces are defined in `DevOpsMigrationPlatform.Abstractions`.
  Implementations in infrastructure or CLI projects MUST depend on abstractions
  only — never on concrete types from sibling infrastructure projects.
- Every class MUST have a single, clear reason to change (Single Responsibility
  Principle). Classes combining orchestration, IO, and business rules MUST be
  split along those boundaries.

### VIII. ATDD-First Development (NON-NEGOTIABLE)

Development follows a two-loop cycle. The **SpecKit outer loop** captures
intent and produces a plan; the **ATDD inner loop** delivers each scenario as
a tested, reviewed increment.

**SpecKit outer loop (feature → tasks):**

1. `/speckit.specify` — produce `spec.md` with prioritised user stories and
   Given/When/Then acceptance scenarios.
2. `/speckit.plan` — produce `plan.md` including the Constitution Check gate.
3. `/speckit.tasks` — produce `tasks.md` with one task per acceptance scenario
   (or coherent group), ordered by dependency.

**ATDD inner loop (one task → one commit):**

1. **Specification** — the accepted scenario from `spec.md` feeds the
   Specification Agent, which produces a Gherkin `.feature` file plus
   architecture notes. Human must approve before proceeding.
2. **Test Generation** — failing Reqnroll `[Binding]` step definitions
   (`*Steps.cs` + `*Context.cs`) under `tests/<Project>.Tests/<Area>/`.
3. **Implementation** — production code that makes the steps pass, plus unit
   tests for every method with branching logic, calculation, or state
   transformation.
4. **Review** — Reviewer Agent produces `Approved` or `Rejected`.

**Rules:**

- No production code before a failing acceptance test exists.
- **One scenario → one session → one commit.** Sessions spanning multiple
  scenarios are forbidden.
- ATDD phases MUST NOT be skipped or reordered.
- Gherkin `.feature` files live under
  `features/<operation>[/<connector>/<module>]/`.
- Test framework: Reqnroll.MSTest + Moq (`MockBehavior.Strict`). No xUnit, no
  NUnit.

## Technology Stack

- **Language:** C# 10+, targeting .NET 9/10 for all new code.
- **Legacy carve-out:** .NET 4.8 is permitted exclusively in
  `DevOpsMigrationPlatform.CLI.TfsExport` for the TFS Object Model exporter.
  No other component may use .NET Framework.
- **Shared abstractions:** `DevOpsMigrationPlatform.Abstractions` targets
  `net481;net10.0` (multi-targeted). Only model records, DTOs, and interface
  definitions may appear there.
- **CLI layer:** Spectre.Console (`Spectre.Console.Cli`). No `System.CommandLine`
  or other argument-parsing library.
- **Dependency Injection:** `Microsoft.Extensions.DependencyInjection` with
  `Microsoft.Extensions.Hosting` for all .NET 9/10 entry points. Constructor
  injection only; no service locator.
- **Configuration:** `Microsoft.Extensions.Options` (`IOptions<T>`,
  `IOptionsSnapshot<T>`). All settings classes are sealed, use `init`-only
  properties, and declare a `SectionName` constant. Bound and validated via
  `services.AddOptions<T>().BindConfiguration(T.SectionName).ValidateDataAnnotations()`.
- **TUI layer:** Terminal.Gui for all interactive terminal rendering. No raw
  ANSI, no `System.Console` inside TUI view classes.
- **Test framework:** Reqnroll.MSTest + Moq. MSTest is the runner; Reqnroll
  provides BDD bindings.
- **Control plane data store:** PostgreSQL via EF Core + Npgsql in all
  environments (Standalone, Self-Hosted, Managed). No SQLite fallback, no
  in-memory database substitute.
- **Observability:** OpenTelemetry (logging, metrics, tracing) via
  `ServiceDefaults`. All services MUST call `builder.AddServiceDefaults()`.
- **Orchestration:** Microsoft Aspire with a single `AppHost` project.
  Two CI subprofiles: `portable` (bundled PG binary + filesystem store) and
  `docker` (Docker PostgreSQL + Azurite). Both MUST pass in every CI stage.

## Reject Conditions

Reject any proposal that:

- Breaks the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/`
  folder structure.
- Loads all revisions or work items into memory before processing.
- Sorts `EnumerateAsync` results in memory.
- Creates a global `Attachments/` root directory.
- Performs direct source-to-target migration without writing to the package.
- Uses any persistence mechanism other than `IArtefactStore` or `IStateStore`
  inside a module.
- Implements per-module identity resolution instead of using
  `IIdentityMappingService`.
- Puts migration execution logic in the control plane.
- References a concrete artefact store implementation inside module code.
- Contains migration logic in the TUI or Job Engine lifecycle outside the
  defined boundary.
- References the .NET 4.8 TFS exporter project directly from any .NET 10
  project.
- Uses a data store other than PostgreSQL for the control plane.
- Creates agent rule files under `/docs` — all agent rules live in
  `.agents/guardrails/`.
- Writes tests using xUnit or NUnit.
- Implements a new module without an accepted Gherkin `.feature` file.
- Uses `new` to construct a registered service inside production or module code
  instead of receiving it via constructor injection.
- Reads configuration via raw `IConfiguration` key access or
  `Environment.GetEnvironmentVariable` inside any service or module — use
  `IOptions<T>` instead.
- Defines an options/settings class that is not `sealed`, has mutable
  properties (setters instead of `init`), or lacks a `SectionName` constant.
- Registers services with inline `services.AddSingleton(...)` calls scattered
  across `Program.cs` instead of a dedicated `Add*Services` extension method.
- Places an interface definition inside an infrastructure or CLI project
  instead of `DevOpsMigrationPlatform.Abstractions`.

## Governance

- `/.agents/guardrails/*.md` files define hard, non-negotiable architectural
  rules and supersede all other documentation and practices.
- `/docs/*.md` files define architectural intent and design rationale.
- `agents.md` at the repository root binds the two and is the mandatory
  starting point for any AI agent or contributor.
- `constitution.md` (this file) supersedes all other development practices.
  When this file and a guardrail conflict, the guardrail wins.
- Amendments to this constitution require:
  1. A version increment following semantic versioning (MAJOR for removals or
     redefinitions of principles; MINOR for new sections or material expansions;
     PATCH for clarifications or wording fixes).
  2. An updated Sync Impact Report prepended as an HTML comment.
  3. Propagation of changes to relevant templates under `.specify/templates/`.
- All pull requests and agent reviews MUST verify compliance against this
  constitution and the guardrails before approving.
- Runtime development guidance: `.agents/guardrails/` and
  `.agents/context/`.

**Version**: 1.2.0 | **Ratified**: 2026-04-02 | **Last Amended**: 2026-04-02

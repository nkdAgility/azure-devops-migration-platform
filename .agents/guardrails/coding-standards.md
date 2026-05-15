# Coding Standards — Azure DevOps Migration Platform

> **For AI agents:** This file is the authoritative enforcement standard. Read it fully before writing any code.
> Code examples illustrating each rule are in [`coding-standards-examples.md`](coding-standards-examples.md).

---

## Core Rule

Code MUST be deterministic, testable, maintainable, and aligned with the defined architecture. No architectural drift is permitted.

---

## Runtime & Structure

- New code: C# 10+, target .NET 9 or .NET 10.
- `Abstractions`, `Abstractions.Agent`, `Infrastructure`, `Infrastructure.Agent` MUST multi-target `net481;net10.0`.
- Types permitted in multi-targeted projects: model records/DTOs, interface definitions, shared utility code with no platform-specific APIs.
- Types MUST NOT be in multi-targeted projects: anything referencing `Microsoft.TeamFoundation.*` (TfsMigrationAgent only); `AzureBlobArtefactStore` (net10.0 only).
- .NET Framework is allowed ONLY in: `DevOpsMigrationPlatform.TfsMigrationAgent` (net481) and `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` (net481).
- TfsMigrationAgent MUST NOT be referenced as a project dependency from any .NET 10 project.
- TfsMigrationAgent is Windows-only; spawned via `AgentLifecycleService` or run independently.
- No other component may use .NET Framework.
- Credentials pass via the job contract only — never via CLI arguments.
- See [docs/agent-hosting.md](../../docs/agent-hosting.md#tfs-migration-agent).

---

## UI Boundaries

- CLI: MUST use `Spectre.Console.Cli`. MUST NOT use `System.CommandLine` or `McMaster.Extensions.CommandLineUtils`.
- TUI: MUST use `Terminal.Gui`. MUST NOT use `System.Console`, raw ANSI, or Spectre.Console rendering primitives inside TUI view classes.
- CLI layer handles input only. TUI layer handles rendering only. No migration logic crosses the boundary.

---

## Architecture Rules

- MUST use dependency injection.
- MUST NOT use service locator patterns.
- MUST NOT use static mutable state.
- MUST NOT perform direct file IO inside modules.
- MUST use `IArtefactStore` for all file writes.
- MUST use `IStateStore` for all resume/checkpoint state.
- MUST isolate modules via interface boundaries.
- MUST preserve one canonical capability seam per concern; do not add parallel runtime entry points.
- MUST centralize concern engine logic behind the canonical seam; do not duplicate it across modules/orchestrators/extensions.
- MUST keep extensions/adapters thin and policy-focused (when/how/skip/fail/checkpoint), not alternate engines.

---

## SOLID — Concrete Rules

- **SRP:** CLI commands MUST delegate business logic to injected services. No business logic inside `Execute`/`ExecuteAsync`.
- **OCP:** New behaviour via new `IExportModule`/`IImportModule` implementations. Never modify existing classes for new types.
- **LSP:** All `IArtefactStore` implementations MUST share identical contracts (return `null` if not found; throw on access errors). Implementations are interchangeable.
- **ISP:** Interfaces MUST be minimal and focused (`IArtefactStore`, `IStateStore`). No fat interfaces mixing unrelated concerns.
- **DIP:** Modules MUST accept `IArtefactStore` via constructor injection. MUST NOT `new` up concrete store types.

See [`coding-standards-examples.md`](coding-standards-examples.md) for C# examples.

---

## Testing

- MUST use MSTest + `Reqnroll.MSTest` (Gherkin `.feature` files for acceptance tests).
- Each module MUST have unit tests. Business logic MUST be testable in isolation.
- Live Azure DevOps tests MUST be `[TestCategory("Integration")]`.
- Replay logic MUST have deterministic tests.
- Write Gherkin in `features/` before implementation begins.

---

## Determinism

- File names MUST be deterministic. Ordering MUST be explicit and stable.
- No non-deterministic GUIDs unless explicitly required. All hashes MUST be reproducible.

---

## Error Handling

- MUST NOT swallow exceptions silently.
- MUST use structured logging. Record module failures explicitly.
- Fail-fast unless `failFast = false` is configured.

---

## Observability — MANDATORY on Every Module and Tool

> **⛔ CRITICAL:** A module that does not satisfy all four requirements below is **not done**.

### O-1: Activity Spans
- Call `ActivitySource.StartActivity("module.operation")` (`using var`) at the start of every significant operation.
- MUST set tags: `module`, `operation`, counts, `error = true` on failure.
- Use `WellKnownActivitySourceNames.Migration` as the source name.

### O-2: Business Metrics
- Inject `IMigrationMetrics` as an **optional** constructor parameter.
- MUST record per operation: attempt counter, completion counter, error counter, duration histogram (ms), in-flight gauge (+1/-1).
- New metric constants → `WellKnownMetricNames`. New signatures → `IMigrationMetrics`. New instruments → `MigrationMetrics`.
- Metric interfaces and concrete classes MUST NOT have `#if !NETFRAMEWORK` guards. Only pipeline-layer types (`SnapshotMetricExporter`, `TelemetryServiceExtensions`, `InMemoryMetricSnapshotStore`) may.
- See `.agents/context/telemetry-model.md`.

### O-3: Structured Logging
- `Information` at start/end of every export/import/validate with counts.
- `Warning` for every skip, unresolvable identity, missing file, or degraded path.
- `Debug` for per-item detail (never `Information` when hundreds of items expected).
- Structured parameters only (`{Count}`, `{Module}`) — no string interpolation in log calls.
- Customer-identifiable data (project names, org URLs, identity strings, attachment paths) MUST use `DataClassification.Customer` scope.
- `AddDataClassificationFilter()` MUST be called on `builder.Logging` in every host exporting logs via OTel.

### O-4: ProgressEvent Emission ⛔ MOST CRITICAL
- Inject `IProgressSink` as an **optional** constructor parameter (`IProgressSink? progressSink = null`).
- MUST emit at **start**: `Stage = "Starting"`, message.
- MUST emit **per item or per batch ≤ 50** during loops with `Metrics.Migration.{ModuleName}` populated.
- MUST emit at **completion**: final counts, `Stage = "Complete"`.
- All emits MUST be null-checked (`_progressSink?.Emit(...)`).
- `MigrationCounters` MUST have a `ModuleCounters?` property for every module that emits counters.
- `QueueCommand.BuildProgressRenderable` MUST render rows in order: **Identities → Nodes → Teams → WorkItems**.
- Adding module counters to `MigrationCounters` without updating `BuildProgressRenderable` = instant reject.
- **`ProgressEvent.Metrics` is always `null` for .NET 10 agents.** CLI MUST read counters from `GET /jobs/{id}/telemetry`, NOT from `ProgressEvent.Metrics`.

See [`coding-standards-examples.md`](coding-standards-examples.md) for O-2 and O-4 code patterns.

---

## Type System

- Encode intent in types. No primitive obsession (no raw `string`/`int` for domain concepts).
- Use `record` types for immutable DTOs. Prefer `sealed` types where inheritance is not intended.
- Type names MUST align with domain language (`WorkItemRevision`, not `WIRev`).
- Domain model types MUST live in `DevOpsMigrationPlatform.Abstractions`. No domain types in CLI or infrastructure projects.

---

## Immutability

- MUST prefer immutable data structures for all DTOs and domain models.
- MUST use `init`-only properties or constructor assignment; no public setters on model types.
- MUST use `record` types where structural equality is required.
- State transitions MUST be explicit. Shared mutable state MUST NOT exist.
- `IOptions<T>` targets MUST be `sealed` with `init`-only properties.

---

## Concurrency and Async Safety

- All I/O-bound operations MUST be async/await end-to-end. No `.Result` or `.Wait()` on `Task`/`ValueTask`.
- `CancellationToken` MUST be accepted and forwarded through the entire call chain.
- `IAsyncEnumerable<T>` MUST be consumed with `await foreach`. No `.ToListAsync()` inside modules.
- Shared mutable resources MUST be protected with `SemaphoreSlim` (async) or `lock` (sync).
- `ConfigureAwait(false)` MUST be used in library and module code.
- Timeouts MUST use `CancellationTokenSource.CancelAfter`. No `Thread.Sleep` or bare `Task.Delay`.

See [`coding-standards-examples.md`](coding-standards-examples.md) for async examples.

---

## Configuration

- Configuration MUST flow through `IOptions<T>` only. `MigrationOptions` is a serialisation-only DTO and MUST NOT be injected into any module, tool, or service.
- Each options class MUST declare `public const string SectionName = "MigrationPlatform:...";` and be registered via `AddSchemaEntry<T>()`.
- The `migration.schema.json` MUST be generated from DI registrations. CI fails if the committed schema differs from the generated output.
- MUST NOT read `IConfiguration` keys directly or call `Environment.GetEnvironmentVariable` inside any service or module.
- Options classes MUST be `sealed`, `init`-only, and declare `public static string SectionName`.
- MUST call `.ValidateDataAnnotations()` during registration.
- No environment-specific branching in code (`if (env == "Production")`). Vary through configuration only.
- Secrets via Key Vault or environment injection only. Never hard-coded.

---

## Versioning

- Breaking changes MUST increment MAJOR version with a corresponding upgrader.
- Deprecated APIs MUST be `[Obsolete("Use X instead. Removed in vN.")]` before removal.
- `manifest.json` MUST embed `packageVersion`, `toolVersion`, `runId`, and `configHash`.
- Interfaces in `DevOpsMigrationPlatform.Abstractions` MUST NOT have methods removed or signatures changed without a version gate.

---

## API and Integration Design

- All integration contracts MUST be explicit — interfaces or schema documents in `DevOpsMigrationPlatform.Abstractions`.
- Retried operations MUST be idempotent, or explicitly documented as non-idempotent.
- Failure modes MUST be defined and predictable. No untyped exceptions crossing layer boundaries.
- All external calls MUST have explicit timeout and cancellation propagation.
- Azure DevOps and TFS SDKs MUST be wrapped behind abstraction interfaces. Never call SDKs directly from domain or module code.

See [`coding-standards-examples.md`](coding-standards-examples.md) for SDK abstraction examples.

---

## Persistence

- All persistent writes MUST go through `IArtefactStore` or `IStateStore`. No direct file/DB access from modules.
- Schema evolution MUST be additive. Destructive changes require a versioned migration.
- Import idempotency MUST use `.migration/Checkpoints/idmap.db` and cursor state — never re-query the target.
- No partial writes. Write to temp path, atomically rename to final path.
- One revision folder = one atomic transactional boundary.

---

## Resilience

- All external API calls MUST use retry with exponential back-off and jitter (Polly or `Microsoft.Extensions.Http.Resilience`).
- Circuit breakers MUST protect calls to systems that can sustain periods of unavailability.
- Modules MUST continue on single-item failure unless `failFast` is configured.
- Timeout budgets MUST be explicit at every integration boundary.
- Degraded mode MUST emit a structured warning event and degraded-mode metric.

---

## Security

- Validate all user input at system boundaries before processing or persistence.
- Credentials and secrets MUST NEVER appear in logs, file names, paths, or in-memory strings beyond immediate use.
- Subprocess credentials MUST pass via stdin JSON only. Never via CLI arguments or environment variables.
- Authentication MUST use managed identity or Key Vault references.
- Dependencies MUST be pinned to exact versions. No `Version="*"` or `Version="Latest"`.
- Sanitise inputs used in file system paths or external commands (path traversal, injection).
- Every known vulnerability MUST be remediated or explicitly tracked with a written rationale. No silent shipping.
- `dotnet list package --vulnerable` MUST run in CI; HIGH/CRITICAL findings MUST block merge.
- OWASP Top 10 MUST be considered for input handling, auth, authorisation, and serialisation.

---

## Build and Dependency Hygiene

- All NuGet versions MUST be in `Directory.Packages.props`. No per-project version attributes.
- Zero warnings. Treat-warnings-as-errors enforced in CI.
- Every change MUST pass `dotnet clean && dotnet build --no-incremental` before complete.
- Every change MUST pass `dotnet test` before complete.
- Every change MUST run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via `.vscode/launch.json` with verified observable output.
- New NuGet dependencies MUST be justified in the PR. Transitive conflicts MUST be resolved in `Directory.Packages.props`.
- `dotnet list package --vulnerable` MUST run after every build in CI.

---

## Performance

- Measure before optimising. No premature optimisation.
- Prefer `Span<T>`, `Memory<T>`, pooled buffers in hot paths.
- `IAsyncEnumerable<T>` and streaming required for all unbounded data sets.
- Bounded eviction policy required for all caches.
- Module durations MUST be recorded as OTel histogram metrics `migration.<module>.duration`.

---

## Cost Awareness

- Provisioning MUST be justified. No over-provisioning by default.
- Scaling MUST have explicit maximum bounds. No unbounded auto-scale without a documented cost ceiling.
- Blob storage patterns MUST minimise redundant list/read operations.

---

## Operational Readiness

- Every deployable component MUST expose liveness + readiness health-check endpoints via `AddServiceDefaults()`.
- Structured logs MUST include `correlationId`, `runId`, and `moduleId` on every log event during a migration run.
- Alerting thresholds MUST be defined for error rate, latency SLOs, and queue depth before release.
- Recovery procedures MUST be in a `RUNBOOK.md` colocated with each deployable service.

---

## Documentation

- ADRs MUST be created in `/docs/adr/` for significant design decisions.
- All `Abstractions` interfaces MUST have XML doc-comments describing contract semantics.
- Feature files in `features/` MUST be kept in sync with implementation.
- `.agents/context/` files MUST be updated when the corresponding implementation changes.
- `README.md` and `/docs/*.md` MUST be updated when public behaviour, config schema, or CLI surface changes.

---

## Naming

- MUST use `AzureDevOps` (full, unabbreviated) everywhere: class names, interface names, file names, variables, comments.
- MUST NOT abbreviate as `ADO`, `Ado`, or any other shorthand.

---

## Prohibited Patterns

- Direct Source → Target migration logic.
- Global attachment stores.
- Loading entire revision sets into memory.
- Hidden resume state outside `.migration/Checkpoints/`.
- Cross-module direct calls.
- `.NET Framework` usage outside `TfsMigrationAgent` / `Infrastructure.TfsObjectModel`.
- `.Result` or `.Wait()` on `Task`/`ValueTask`.
- Ignoring or discarding a `CancellationToken`.
- Hard-coded secrets, connection strings, or credentials.
- Direct SDK calls (Azure DevOps, TFS OM) from domain or module code.
- Retry without exponential back-off.
- Floating NuGet version ranges (`Version="*"`, `Version="Latest"`).
- Primitive types standing in for domain concepts.
- Public mutable setters on domain model or DTO types.
- Environment-specific code branching instead of external configuration.
- Breaking schema/API changes without a version increment and upgrader.
- Unbounded auto-scale without a documented cost ceiling.
- Deployable component without liveness/readiness health-check.
- Domain contract interface outside `DevOpsMigrationPlatform.Abstractions`. _(Exception: infrastructure-internal testability seams with SDK types in signatures are permitted in their infrastructure project if (a) no module/agent/CLI references them and (b) a corresponding SDK-free abstraction exists in `Abstractions`. Host-internal interfaces for testability within a single deployable unit are permitted in the host project if not consumed across project boundaries.)_
- Compiled reference to `DevOpsMigrationPlatform.TfsMigrationAgent` from any .NET 10 project.
- Spawning a TFS subprocess from a .NET 10 component (all TFS jobs go through the control plane lease protocol).
- Passing credentials as CLI arguments to any subprocess.
- Calling source/target APIs from within the control plane.
- Calling source/target APIs from within a Migration Agent outside the orchestrator execution path.
- Referencing `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly in module code.
- Sorting `EnumerateAsync` results in memory.
- Inspecting or proxying source/target credentials in the control plane.
- Writing to `Console`/`System.Console` from the Job Engine or any module.
- Emitting progress as console text instead of `IProgressSink` events.
- Migration execution logic in the TUI.
- `System.Console`, ANSI escapes, or Spectre.Console primitives inside TUI view classes.
- `System.CommandLine`, `McMaster.Extensions.CommandLineUtils`, or any non-Spectre CLI argument library.
- Adding/changing a CLI command without a matching entry in `.vscode/launch.json`.
- Adding/changing a deployable Host without coverage in `build.ps1`.
- CLI-exposed feature without a `[TestCategory("SystemTest")]` test asserting observable output.
- Submitting any change without passing build, passing tests, and a verified scenario run.

---

## Full Connector Coverage (NON-NEGOTIABLE)

Every feature touching source/target interaction MUST be implemented for all three connectors where the API supports it:

1. **Simulated** — deterministic, no external connectivity, used by all test categories.
2. **AzureDevOpsServices** — REST API via .NET 10.
3. **TeamFoundationServer** — TFS Object Model via `TfsMigrationAgent` (net481).

Violations:
- Feature for AzureDevOps with Simulated as `throw new NotImplementedException()`.
- Omitting TFS when the TFS OM API supports the capability.
- New field/link/attachment behaviour in one connector, not propagated to others.
- Interface method only one connector implements; others return `default` or throw.
- Deferring connector implementations to a future task or follow-up PR.

TFS exempt only when the TFS OM API does not expose the capability. The TFS implementation MUST then:
- Emit `ProgressEvent` with `EventKind.Warning` explaining the unsupported capability.
- Log a structured `Warning`.
- NOT throw `NotImplementedException` — gracefully skip.

---

## No `NotImplementedException` Stubs

`throw new NotImplementedException()` (or any equivalent: `throw new NotSupportedException("... not yet implemented")`, `return default;` propagating silently, `TODO: implement` with no same-session issue) is **never permitted in shipped code**.

If a method cannot be fully implemented in the current work unit, **do not create the method**.

**Ephemeral exception:** A stub is permitted only if introduced AND removed within the same session, noted in the session log, and replaced before the task is marked complete. No other exceptions. A PR with a `NotImplementedException` in any reachable code path MUST NOT be merged.

Applies to: all `src/`, all `tests/`, all Simulated implementations.

Instant reject triggers:
- `throw new NotImplementedException()`
- `throw new NotSupportedException("... not yet implemented")`
- `throw new NotSupportedException("... not yet supported")`
- Method body of only `return default;` or `return null;` where that value propagates silently
- `TODO: implement` with no tracked issue or same-session implementation

---

## Pre-Merge Checklist

Reject if any answer is YES (violation) or NO (requirement not met):

| # | Question | Expected |
|---|----------|----------|
| 1 | New state outside `IStateStore`? | NO |
| 2 | Non-deterministic behaviour? | NO |
| 3 | Module isolation violated? | NO |
| 4 | `IArtefactStore` bypassed? | NO |
| 5 | .NET Framework used outside TfsMigrationAgent? | NO |
| 6 | `TfsMigrationAgent` referenced from .NET 10? | NO |
| 7 | TFS subprocess spawned from .NET 10? | NO |
| 8 | Credentials in CLI args to subprocess? | NO |
| 9 | Migration logic added to control plane? | NO |
| 10 | Concrete store referenced inside module? | NO |
| 11 | `EnumerateAsync` results sorted in memory? | NO |
| 12 | `Console.Write` from Job Engine or module? | NO |
| 13 | Migration logic in TUI? | NO |
| 14 | `System.Console`, ANSI, or Spectre.Console in TUI view? | NO |
| 15 | Non-Spectre argument parser in CLI command code? | NO |
| 16 | `.Result` or `.Wait()` on `Task`/`ValueTask`? | NO |
| 17 | `CancellationToken` ignored or discarded? | NO |
| 18 | Hard-coded secret, connection string, or credential? | NO |
| 19 | SDK called directly from domain/module code? | NO |
| 20 | Floating NuGet version range? | NO |
| 21 | Breaking schema/API change without versioned upgrader? | NO |
| 22 | Primitive type used where domain type required? | NO |
| 23 | Public mutable setter on DTO or domain model? | NO |
| 24 | Environment branching in code instead of config? | NO |
| 25 | Domain interface outside `Abstractions` (without documented exception)? | NO |
| 26 | Retry without exponential back-off? | NO |
| 27 | Deployable component without health-check? | NO |
| 28 | `dotnet clean && dotnet build --no-incremental` passes? | YES |
| 29 | `dotnet test` passes? | YES |
| 30 | At least one scenario run with verified output? | YES |
| 31 | `NotImplementedException` or equivalent placeholder in reachable code? | NO |
| 32 | Known vulnerability without fix or tracked rationale? | NO |
| 33 | `dotnet list package --vulnerable` run; all HIGH/CRITICAL resolved? | YES |
| 34 | CLI command added/changed → matching `.vscode/launch.json` entry? | YES |
| 35 | Deployable Host added/changed → exercised by `build.ps1`? | YES |
| 36 | CLI-exposed feature → `[TestCategory("SystemTest")]` test asserting output? | YES |

---

## Final Rule

Modern platform code runs on .NET 9/10. Legacy TFS Object Model is allowed only as an isolated external extraction adapter. No exceptions.

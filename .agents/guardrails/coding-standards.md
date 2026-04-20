# Coding Standards – Azure DevOps Migration Platform

This file defines enforceable coding standards for the migration platform.

All contributors and AI agents must follow these rules.

---

# 🎯 Purpose

Ensure:

- Determinism
- Testability
- Maintainability
- Isolation of legacy concerns
- No architectural drift

---

# 🔒 Language & Runtime

## Primary Runtime

- MUST use C# 10+.
- MUST target .NET 9 or .NET 10.
- All new code MUST be .NET 9/10 unless explicitly exempted.

## Cross-Runtime Code Sharing (Multi-Targeting)

`DevOpsMigrationPlatform.Abstractions` and `DevOpsMigrationPlatform.Infrastructure` MUST target both `net481` and `net10.0`:

```xml
<TargetFrameworks>net481;net10.0</TargetFrameworks>
```

This is the only permitted mechanism for sharing types between the .NET 10 host and the .NET 4.8 subprocess. The same source compiles independently for each runtime — no cross-runtime DLL references exist at runtime.

Types permitted in multi-targeted projects:
- Model records and DTOs (work item revisions, progress events, fields, links, attachments)
- Interface definitions (e.g., `IWorkItemExportService`)
- Shared utility code with no platform-specific APIs

Types that MUST NOT be in multi-targeted projects:
- Any type referencing TFS OM assemblies (`Microsoft.TeamFoundation.*`) — net481 only, `CLI.TfsMigration` project only
- `AzureBlobArtefactStore` — net10.0 only (Azure Blob SDK not available for net481)

## Legacy Runtime (Explicit Carve-Out)

The ONLY allowed .NET Framework usage is:

- `DevOpsMigrationPlatform.CLI.TfsMigration` — the TFS Object Model exporter subprocess
- Built against .NET 4.8
- Using the legacy TFS Object Model (SOAP)

Rules for the carve-out:

- The .NET 4.8 exporter MUST exist in a dedicated project that is **not** part of the .NET 10 solution build.
- It MUST NOT share runtime dependencies with .NET 10 components.
- It MUST be invoked only via `ExternalToolRunner` in `DevOpsMigrationPlatform.CLI.Migration` — never directly from any other .NET 10 code.
- `TfsExporterProcessAdapter` in `CLI.Migration` is the **only** permitted TFS-aware .NET 10 class. It translates raw NDJSON stdout lines from the subprocess into `ProgressEvent` objects and forwards them to the CLI's progress pipeline. No other .NET 10 class may contain TFS-specific logic.
- It MUST communicate with the .NET 10 host exclusively via the process bridge protocol:
  - **stdin** — `TfsExportRequest` as UTF-8 JSON
  - **stdout** — NDJSON progress lines (one JSON object per line)
  - **stderr** — unstructured error detail only
  - **exit code** — 0 for success, non-zero for failure
  - **cancellation sentinel file** — a file written by the host to signal abort
- It MUST NOT be referenced directly as a project dependency in any .NET 10 project.
- It MUST NOT expose shared libraries consumed by modern modules.
- Credentials MUST be passed via stdin JSON only — never via command-line arguments.

The legacy exporter is a bounded extraction adapter only.

No other component may use .NET Framework.

See [docs/tfs-exporter.md](../../docs/tfs-exporter.md) for the full process bridge protocol.

---

# 🖥️ UI & CLI Libraries

## CLI Layer (command parsing and argument handling)

- MUST use **Spectre.Console** (`Spectre.Console.Cli`) for all command definitions, argument/option binding, help-text generation, and console output formatting.
- MUST NOT use `System.CommandLine`, `McMaster.Extensions.CommandLineUtils`, or any other argument-parsing library in command-layer code.

## TUI Layer (live progress rendering)

- MUST use **Terminal.Gui** for all interactive terminal windows, panels, progress tables, and live-updating views.
- MUST NOT use `System.Console` directly, raw ANSI escape sequences, or Spectre.Console rendering primitives inside TUI view classes.
- Terminal.Gui is the single permitted UI rendering library within the TUI layer.

These boundaries are strict. The CLI layer calls into the TUI layer for display only — no migration logic crosses the boundary.

---

# 🧱 Architectural Rules

- MUST follow SOLID principles (see concrete examples below).
- MUST use dependency injection.
- MUST NOT use service locator patterns.
- MUST NOT use static mutable state.
- MUST NOT perform direct file IO inside modules.
- MUST use IArtefactStore for file writes.
- MUST use IStateStore for resume/checkpoint state.
- MUST isolate modules by interface boundaries.

## SOLID Principles - Concrete Rules

### Single Responsibility Principle
❌ **REJECT:** Commands that contain both CLI parsing AND business logic
```csharp
// VIOLATION: Command doing both argument parsing and migration execution
public class ExportCommand : Command<ExportSettings> 
{
    public override int Execute(CommandContext context, ExportSettings settings) 
    {
        // CLI logic mixed with business logic
        var modules = LoadModules(); 
        ExecuteMigration(modules); // Should be delegated
        return 0;
    }
}
```

✅ **ACCEPT:** Commands delegate to services via DI
```csharp
// CORRECT: Command delegates business logic to service
public class ExportCommand : CommandBase<ExportSettings>
{
    private readonly IMigrationOrchestrator _orchestrator;
    
    protected override async Task<int> ExecuteInternalAsync(...)
    {
        return await _orchestrator.ExecuteExportAsync(...);
    }
}
```

### Open/Closed Principle  
❌ **REJECT:** Adding new export types by modifying existing classes
```csharp
// VIOLATION: Have to modify this class for each new export type
public class ExportService
{
    public void Export(string type) 
    {
        if (type == "WorkItems") { /* ... */ }
        else if (type == "GitRepos") { /* ... */ }
        // Adding new type requires modifying this method
    }
}
```

✅ **ACCEPT:** New export types via new IExportModule implementations
```csharp
// CORRECT: New modules added without changing existing code
public interface IExportModule { }
public class WorkItemsExportModule : IExportModule { }
public class GitReposExportModule : IExportModule { }
// New types just implement IExportModule
```

### Liskov Substitution
❌ **REJECT:** IArtefactStore implementations that change behavioral contracts
```csharp
// VIOLATION: Changes the contract - FileSystem throws, Blob returns null
public class FileSystemArtefactStore : IArtefactStore 
{
    public async Task<Stream> ReadAsync(string path) 
    {
        if (!File.Exists(path)) throw new FileNotFoundException();
        return File.OpenRead(path);
    }
}
```

✅ **ACCEPT:** FileSystemArtefactStore and AzureBlobArtefactStore work identically
```csharp
// CORRECT: Both implementations have identical contracts
public async Task<Stream?> ReadAsync(string path) 
{
    // Both return null if not found, both throw on access errors
    // Callers can substitute either implementation
}
```

### Interface Segregation
❌ **REJECT:** Fat interfaces with methods not all implementations need  
```csharp
// VIOLATION: Not all stores need caching or compression
public interface IArtefactStore 
{
    Task WriteAsync(string path, Stream content);
    Task<Stream> ReadAsync(string path);
    Task ClearCacheAsync(); // Not needed by all implementations
    Task CompressAsync(string path); // Not needed by all implementations
}
```

✅ **ACCEPT:** Focused interfaces like IArtefactStore, IStateStore
```csharp
// CORRECT: Each interface has single responsibility
public interface IArtefactStore 
{
    Task WriteAsync(string path, Stream content);
    Task<Stream?> ReadAsync(string path); 
    IAsyncEnumerable<string> EnumerateAsync(string prefix);
}

public interface ICacheStore // Separate concern
{
    Task ClearCacheAsync();
}
```

### Dependency Inversion
❌ **REJECT:** Modules that instantiate concrete FileSystemArtefactStore
```csharp
// VIOLATION: Module depends on concrete implementation
public class WorkItemsExportModule 
{
    public WorkItemsExportModule() 
    {
        _store = new FileSystemArtefactStore("./package"); // Hardcoded dependency
    }
}
```

✅ **ACCEPT:** Modules that inject IArtefactStore abstraction
```csharp
// CORRECT: Depends on abstraction, works with any implementation
public class WorkItemsExportModule 
{
    private readonly IArtefactStore _store;
    
    public WorkItemsExportModule(IArtefactStore store) 
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }
}
```

---

# 🧪 Testing

- MUST use MSTest as the test runner.
- MUST use Reqnroll (`Reqnroll.MSTest`) as the BDD step-binding layer for acceptance tests. Reqnroll reads Gherkin `.feature` files directly.
- Each module MUST have unit tests.
- Business logic MUST be testable in isolation.
- No integration tests may depend on live Azure DevOps unless explicitly marked with `[TestCategory("Integration")]`.
- Replay logic MUST have deterministic tests.

Preferred:
- TDD for new modules.
- Explicit validation tests for package integrity.
- Acceptance tests written as Gherkin in `features/` before implementation begins.

---

# 📦 Determinism Rules

- File names MUST be deterministic.
- Ordering MUST be explicit and stable.
- No non-deterministic GUID usage unless explicitly required.
- Any hash used MUST be reproducible.

---

# 🧾 Error Handling

- MUST NOT swallow exceptions silently.
- MUST use structured logging.
- MUST record module failures explicitly.
- Fail-fast unless configuration explicitly allows continue-on-error.

---

# 📊 Observability

- MUST use OpenTelemetry.
- Each module execution MUST create an activity span.
- Duration and failure metrics MUST be recorded.
- No ad-hoc console logging for runtime diagnostics.
- Every `ILogger` call that references customer-identifiable data (work item IDs, field values, project names, org URLs, attachment paths) MUST be wrapped in a `DataClassification.Customer` scope — either `using (logger.BeginDataScope(DataClassification.Customer))` (.NET 10) or `using (DataClassificationScope.Begin(DataClassification.Customer))` (.NET 4.8).
- Unclassified logs default to `System` and are exported to Azure Monitor. Gradual classification is permitted; unclassified logs are safe-by-default.
- Error messages inside a `Customer` scope that need to reach Azure Monitor MUST use an inner `DataClassificationScope.Begin(DataClassification.System)` scope AND MUST NOT include customer data in the message template.
- `AddDataClassificationFilter()` MUST be called on `builder.Logging` in every host that exports logs via OTel (ControlPlaneHost, MigrationAgent).

---

# �️ Type System and Domain Modelling

- MUST encode intent and constraints in types — avoid primitive obsession.
- MUST use `record` types for value-based equality and immutable DTOs.
- MUST prefer `sealed` types where inheritance is not intended.
- MUST NOT pass raw `string` or `int` where a domain type (e.g., `WorkItemId`) provides safety and clarity.
- Type names MUST align with the domain language (e.g., `WorkItemRevision`, not `WIRev` or `DataRecord`).
- Domain model types MUST live in `DevOpsMigrationPlatform.Abstractions`; no domain types in CLI or infrastructure projects.

---

# 🧊 Immutability and State Management

- MUST prefer immutable data structures for all DTOs and domain models.
- MUST use `init`-only properties or constructor assignment; no public setters on model types.
- MUST use C# `record` types where structural equality is required.
- State transitions MUST be explicit — never mutate an object in place and rely on callers to observe the change.
- Shared mutable state MUST NOT exist; pass immutable snapshots as method parameters.
- Configuration objects (all `IOptions<T>` targets) MUST be `sealed` with `init`-only properties (see Configuration section).

---

# ⚡ Concurrency and Async Safety

- All I/O-bound operations MUST be async/await from top to bottom — no `.Result` or `.Wait()` calls on `Task` or `ValueTask`.
- `CancellationToken` MUST be accepted and forwarded through the entire call chain; swallowing or ignoring a token is a violation.
- `IAsyncEnumerable<T>` MUST be consumed with `await foreach`; materialising with `.ToListAsync()` inside modules is forbidden.
- Shared mutable resources MUST be protected with `SemaphoreSlim` (preferred for async code) or `lock` for synchronous critical sections.
- `ConfigureAwait(false)` MUST be used in library and module code; it may be omitted only in top-level entry-point code.
- Timeout handling MUST use `CancellationTokenSource.CancelAfter` — `Thread.Sleep` and bare `Task.Delay` without a token are prohibited.

❌ **REJECT:** Blocking on async operations
```csharp
// VIOLATION: Deadlock risk, blocks thread pool
var result = _store.ReadAsync(path).Result;
var items = GetItemsAsync(token).GetAwaiter().GetResult();
```

✅ **ACCEPT:** Full async propagation
```csharp
// CORRECT: Async all the way up
var result = await _store.ReadAsync(path, cancellationToken);
await foreach (var item in GetItemsAsync(cancellationToken))
{ ... }
```

---

# 🔑 Configuration and Environment Isolation

- Configuration MUST flow through `IOptions<T>` or `IOptionsSnapshot<T>` only.
- MUST NOT read `IConfiguration` keys directly or call `Environment.GetEnvironmentVariable` inside any service or module.
- Options classes MUST be `sealed`, use `init`-only properties, and declare `public static string SectionName`.
- MUST apply data annotation validation attributes and call `.ValidateDataAnnotations()` during registration.
- Environment-specific behaviour MUST NOT branch in code — vary through configuration values, not `if (env == "Production")`.
- Secrets MUST be sourced from Key Vault or environment injection; never hard-coded or committed to source control.

---

# 📦 Versioning and Contract Stability

- Public API or package schema changes MUST be versioned explicitly.
- Breaking changes MUST increment MAJOR version; additive changes MUST increment MINOR; fixes increment PATCH.
- Deprecated APIs MUST be marked `[Obsolete("Use X instead. Removed in vN.")]` before removal.
- Backward-incompatible package schema changes MUST ship with a corresponding upgrader.
- `manifest.json` MUST embed `packageVersion`, `toolVersion`, `runId`, and `configHash` for traceability.
- Interfaces in `DevOpsMigrationPlatform.Abstractions` MUST NOT have methods removed or signatures changed without a version gate.

---

# 🤝 API and Integration Design

- All integration contracts MUST be explicit — defined as interfaces or schema documents in `DevOpsMigrationPlatform.Abstractions`.
- Operations that may be retried MUST be idempotent, or explicitly documented as non-idempotent in their contract.
- Failure modes MUST be defined and predictable — no untyped exceptions propagating across layer boundaries.
- All external calls MUST have explicit timeout and cancellation propagation.
- SDK and client library usage MUST be wrapped behind an abstraction interface; never call Azure DevOps or TFS SDKs directly from domain or module code.

❌ **REJECT:** Direct SDK calls in module code
```csharp
// VIOLATION: Module coupled to Azure DevOps SDK
public class WorkItemsImportModule
{
    public async Task ImportAsync(...)
    {
        var client = new WorkItemTrackingHttpClient(...); // SDK called directly
        await client.CreateWorkItemAsync(...);
    }
}
```

✅ **ACCEPT:** Module calls abstraction; SDK lives in infrastructure
```csharp
// CORRECT: Module depends on abstraction only
public class WorkItemsImportModule
{
    private readonly IWorkItemImportService _importService;

    public async Task ImportAsync(WorkItemRevision revision, CancellationToken ct)
    {
        await _importService.CreateOrUpdateAsync(revision, ct);
    }
}
```

---

# 🗃️ Data Integrity and Persistence Discipline

- All persistent writes MUST go through `IArtefactStore` or `IStateStore` — no direct file or database access from module code.
- Schema evolution MUST be safe and additive by default; destructive changes require a versioned migration.
- Import idempotency MUST be enforced via `Checkpoints/idmap.db` and cursor state — never by re-querying the target.
- No partial writes: write to a temporary path and atomically rename to the final path where the store supports it.
- Transactional boundaries MUST match the semantic unit of work; one revision folder = one atomic boundary.

---

# 🛡️ Resilience and Fault Tolerance

- All external API calls MUST implement retry with exponential back-off and jitter (Polly or `Microsoft.Extensions.Http.Resilience`).
- Circuit breakers MUST protect calls to external systems that can sustain periods of unavailability.
- Modules MUST continue processing remaining items after a single-item failure unless `failFast` is configured.
- Timeout budgets MUST be explicit at each integration boundary — no unbounded waits.
- When operating in degraded mode, modules MUST emit a structured warning event and a degraded-mode metric.

---

# 🔒 Security by Design

- All user input MUST be validated at system boundaries before processing or persistence.
- Credentials and secrets MUST NEVER appear in logs, file names, paths, or in-memory string variables beyond immediate use.
- Any subprocess invocation MUST pass credentials via stdin JSON only — never via CLI arguments or environment variables in code (see TFS exporter protocol).
- Authentication MUST use managed identity or Key Vault references; interactive credential prompts are permitted only in CLI tooling.
- Dependencies MUST be pinned to exact versions; wildcard or floating version ranges (`*`, `Latest`) are prohibited in production package references.
- Input passed to file system paths or external commands MUST be sanitised to prevent path traversal and injection attacks.
- Every known vulnerability in a dependency or in submitted code MUST be either **remediated** or **explicitly called out** with a written rationale and a tracked issue. Silently shipping known vulnerabilities is a violation.
- `dotnet list package --vulnerable` MUST be run as part of every CI pipeline stage; any HIGH or CRITICAL finding MUST block merge.
- OWASP Top 10 categories MUST be considered during code review for any code touching input handling, authentication, authorisation, or serialisation.

---

# 🔧 Build and Dependency Hygiene

- All NuGet package versions MUST be centralised in `Directory.Packages.props`; per-project version attributes are prohibited.
- The solution MUST build cleanly with zero warnings; treat-warnings-as-errors MUST be enforced in CI.
- **Every code change MUST produce a successful build before it is considered complete.** A change that does not compile is not done.
- **Every code change MUST also produce a passing `dotnet test` before it is considered complete.** A change that causes test failures is not done.
- **Every code change MUST also be validated by running at least one scenario config** (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile before it is considered complete. The scenario must execute without errors and produce expected observable output.
- New NuGet dependencies MUST be justified in the PR that introduces them.
- Transitive dependency conflicts MUST be resolved explicitly in `Directory.Packages.props`.
- CI builds MUST be reproducible: timestamps, random seeds, and non-deterministic outputs MUST be suppressed or fixed.
- No `<PackageReference>` with `Version="*"` or `Version="$(PackageVersion)"` without a concrete version source.
- `dotnet clean && dotnet build --no-incremental` MUST be the first step in every CI pipeline; subsequent steps MUST NOT run if the build fails.
- Dependency vulnerability scanning (`dotnet list package --vulnerable`) MUST run after every build in CI.

---

# ⚙️ Performance and Resource Efficiency

- MUST NOT optimise prematurely — measure first with benchmarks or profiling data.
- Allocations in hot paths MUST be minimised; prefer `Span<T>`, `Memory<T>`, and pooled buffers over array copies.
- `IAsyncEnumerable<T>` and streaming MUST be the default for any unbounded data set — batch loads are prohibited.
- Large objects MUST NOT be cached without a bounded eviction policy (size limit or TTL).
- Module execution durations MUST be recorded as OpenTelemetry histogram metrics named `migration.<module>.duration`.

---

# 💰 Cost Awareness

- Cloud resource provisioning MUST be justified by functional requirements; no over-provisioning by default.
- Scaling configuration MUST have explicit maximum bounds — unbounded auto-scale is prohibited without a documented cost ceiling.
- Long-running operations MUST emit progress metrics sufficient to enable runtime cost estimation.
- Blob storage access patterns MUST minimise redundant list and read operations; use cached manifests where available.

---

# 🚦 Operational Readiness

- Every deployable component MUST expose a health-check endpoint (liveness + readiness) via `AddServiceDefaults()`.
- Structured logs MUST include `correlationId`, `runId`, and `moduleId` on every log event emitted during a migration run.
- Alerting thresholds MUST be defined for error rate, latency SLOs, and queue depth before a component is released.
- Recovery procedures MUST be documented in a `RUNBOOK.md` colocated with each deployable service.
- MTTR MUST be measurable — logs and traces MUST be sufficient to diagnose failures retrospectively without re-running.

---

# 📝 Documentation as an Engineering Asset

- Architecture Decision Records (ADRs) MUST be created in `/docs/decisions/` for any significant design decision.
- All interfaces defined in `DevOpsMigrationPlatform.Abstractions` MUST have XML doc-comments describing contract semantics and invariants.
- Feature files in `features/` are living documentation — they MUST be kept in sync with implementation at all times.
- Agent context files in `.agents/context/` MUST be updated when the corresponding implementation changes.
- `README.md` and relevant `/docs/*.md` files MUST be updated when public behaviour, configuration schema, or CLI surface changes.

---

# �🏷️ Naming Conventions

- MUST use `AzureDevOps` (full, unabbreviated) in all class names, interface names, file names, variable names, and comments.
- MUST NOT abbreviate `AzureDevOps` as `ADO`, `Ado`, or any other shorthand — anywhere in the codebase.

---

# 🚫 Prohibited Patterns

- Direct Source → Target migration logic.
- Global attachment stores.
- Loading entire revision sets into memory.
- Hidden resume state outside Checkpoints/.
- Cross-module direct calls.
- .NET Framework usage outside the explicit TFS exporter boundary.
- `.Result` or `.Wait()` calls on `Task` or `ValueTask` anywhere in production code.
- Ignoring or discarding a `CancellationToken` parameter instead of forwarding it.
- Hard-coded secrets, connection strings, or credentials anywhere in source code.
- Direct SDK calls (Azure DevOps client, TFS OM) from domain or module code — wrapped abstractions only.
- Retry logic without exponential back-off (bare `catch/retry` loops).
- Floating or wildcard NuGet version ranges (`Version="*"`, `Version="Latest"`) in any project file.
- Primitive types (raw `string`, `int`) standing in for domain concepts where a dedicated type provides safety.
- Public mutable settters on domain model or DTO types — use `init`-only or constructor assignment.
- Environment-specific code branching (`if (env == "Production")`) instead of external configuration.
- Breaking schema or API changes without a version increment and corresponding upgrader.
- Unbounded auto-scale configuration without a documented cost ceiling.
- Deployable component without a liveness/readiness health-check endpoint.
- Domain contract interface defined outside `DevOpsMigrationPlatform.Abstractions`. Infrastructure-internal testability seams whose signatures carry SDK types (e.g. `Microsoft.TeamFoundation.*`) are permitted in their infrastructure project provided: (a) no module, agent, or CLI code references them, and (b) a corresponding SDK-free abstraction exists in `Abstractions` for the domain boundary. Host-internal interfaces that serve testability or internal decoupling within a single deployable unit (e.g. `IJobStore` in the control plane, `IExternalToolRunner` in the CLI) are permitted in their host project provided they are not consumed across project boundaries by modules or agents.
- Code change submitted without a successful build verification.
- Code change submitted without a passing test run (`dotnet test`).
- Code change submitted without running at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verifying observable output.
- Known vulnerability shipped without either a fix or an explicit written rationale and tracked issue.
- Holding a compiled reference to `DevOpsMigrationPlatform.CLI.TfsMigration` from any .NET 10 project.
- Spawning the TFS exporter subprocess from any code other than `ExternalToolRunner` in `DevOpsMigrationPlatform.CLI.Migration`.
- Passing credentials as command-line arguments to the TFS subprocess (stdin JSON only).
- Parsing TFS exporter stdout as anything other than NDJSON progress lines.
- Calling source or target APIs from within the control plane.
- Calling source or target APIs from within a Migration Agent outside of the orchestrator execution path.
- Referencing `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly in module code (use `IArtefactStore`).
- Sorting `EnumerateAsync` results in memory.
- Unwrapping Key Vault secrets in the control plane.
- Writing to `Console` or `System.Console` from the Job Engine or any module.
- Emitting progress as console text instead of `IProgressSink` events.
- Placing migration execution logic in the TUI (parsing and transport selection only).
- Using `System.Console`, ANSI escape sequences, or Spectre.Console rendering primitives inside TUI view classes (Terminal.Gui only).
- Using `System.CommandLine`, `McMaster.Extensions.CommandLineUtils`, or any argument-parsing library other than Spectre.Console in CLI command-layer code.
- Adding or changing a CLI command without a corresponding `launch.json` configuration in `.vscode/launch.json`.
- Adding or changing a deployable Host (`AppHost`, `ControlPlaneHost`, `MigrationAgent`) without a corresponding mode or build step covered by `build.ps1`.
- Shipping a CLI-exposed feature without a `[TestCategory("SystemTest")]` test that exercises the feature end-to-end and asserts observable output.

---

# � No `NotImplementedException` Stubs

A `throw new NotImplementedException()` (or any equivalent placeholder — `throw new NotSupportedException("... not yet implemented")`, `TODO: implement`, dead `return default`, `return null` where a real value is required) is **never permitted as a shipped state**.

## Rule

> If a method cannot be fully implemented within the current work unit, **do not create the method**. A task is not complete until every code path it introduces is real, tested, and produces correct output. A half-implemented method is worse than no method — it silently breaks at runtime.

## Permitted exception (ephemeral only)

A `throw new NotImplementedException()` is permitted **only** when:
1. It is introduced and removed **within the same session** (same PR / same commit chain).
2. The implementing agent or contributor has explicitly noted it as in-progress in the session log.
3. It is replaced with a real implementation before the task is marked complete.

There are no other exceptions. A PR with a `NotImplementedException` in any reachable code path **must not be merged**.

## Applies to

- All production code (`src/`)
- All test code (`tests/`)
- All Simulated implementations — a simulated method that throws `NotImplementedException` is a broken Simulated layer, not a placeholder
- Stubs in feature branches: if the branch will be merged before implementation is complete, the method must not exist

## Instant reject triggers

- `throw new NotImplementedException()`
- `throw new NotSupportedException("... not yet implemented")`
- `throw new NotSupportedException("... not yet supported")`
- A method body consisting only of `return default;` or `return null;` where that value would propagate silently
- A `TODO: implement` comment with no corresponding issue or same-session implementation

---

# �🔍 Validation Checklist

Before merging changes, verify:

- Does this code introduce new state outside IStateStore?
- Does this code introduce non-deterministic behaviour?
- Does this code violate module isolation?
- Does this code bypass IArtefactStore?
- Does this code introduce .NET Framework dependencies outside the legacy exporter?
- Does this code hold a compiled reference to `DevOpsMigrationPlatform.CLI.TfsMigration`?
- Does this code invoke the TFS subprocess from anywhere other than `ExternalToolRunner` in `CLI.Migration`?
- Does this code pass credentials via command-line arguments to any subprocess?
- Does this code add migration execution logic to the control plane?
- Does this code reference a concrete artefact store implementation inside a module?
- Does this code sort EnumerateAsync results in memory?
- Does this code write to Console from the Job Engine or a module?
- Does this code place migration logic in the TUI layer?
- Does this code use System.Console, ANSI escapes, or Spectre.Console widgets inside a TUI view class?
- Does this code use System.CommandLine or another argument-parsing library instead of Spectre.Console in CLI command code?
- Does this code call `.Result` or `.Wait()` on a `Task` or `ValueTask`?
- Does this code ignore or discard a `CancellationToken` instead of forwarding it?
- Does this code contain hard-coded secrets, connection strings, or credentials?
- Does this code call an external SDK directly from module or domain code without an abstraction wrapper?
- Does this code use floating or wildcard NuGet version ranges?
- Does this code introduce a breaking schema or API change without a versioned upgrader?
- Does this code use primitive types where a domain-specific type would encode intent?
- Does this code have public mutable setters on a DTO or domain model type?
- Does this code branch on environment name instead of using external configuration?
- Does this code define a domain contract interface outside `DevOpsMigrationPlatform.Abstractions`? (Infrastructure-internal testability seams with SDK types in their signatures are permitted in their infrastructure project when no module, agent, or CLI code references them and a corresponding SDK-free abstraction exists in `Abstractions`. Host-internal interfaces for testability or internal decoupling within a single deployable unit are permitted in their host project when not consumed across project boundaries.)
- Does this code add retry logic without exponential back-off?
- Does this code deploy a component without a health-check endpoint?
- Has this change been verified to produce a successful `dotnet clean && dotnet build --no-incremental`?
- Have all tests been verified to pass with `dotnet test`?
- Has at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) been run via a `.vscode/launch.json` debug profile with observable output verified?
- Does this change introduce or leave any `throw new NotImplementedException()`, `throw new NotSupportedException("... not yet implemented")`, or equivalent placeholder in any reachable code path?
- Does this change introduce or surface a known vulnerability? If so, is it remediated or explicitly called out with a tracked issue?
- Has `dotnet list package --vulnerable` been run and are all HIGH/CRITICAL findings resolved or documented?
- Does this change add or modify a CLI command? If so, is there a matching entry in `.vscode/launch.json`?
- Does this change add or modify a deployable Host? If so, is it exercised by an appropriate `build.ps1` mode?
- Does this change add a CLI-exposed feature? If so, is there a `[TestCategory("SystemTest")]` test asserting observable output?

If yes (to a violation), reject.

---

# Final Rule

Modern platform code runs on .NET 9/10.

Legacy TFS Object Model is allowed only as an isolated, external extraction adapter.

No exceptions.
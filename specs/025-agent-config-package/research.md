# Research: Fix — Tool Config Never Reaches the Agent

**Phase 0 output** for [plan.md](plan.md)

## Research Questions Resolved

All unknowns from the Technical Context were resolved before plan writing. No NEEDS CLARIFICATION items remain.

---

### R-1: Per-job IOptions<T> override pattern in .NET DI

**Question**: How do we supply per-job `IOptions<T>` values to modules that are registered at host startup, given that the host container is shared across all jobs?

**Decision**: Build a fresh `ServiceCollection` per job, populate it with per-job `IOptions<T>` bindings derived from the job's `IConfiguration` (built from `migration-config.json`), register module and tool services in it, build a `ServiceProvider`, and dispose it at job end.

**Rationale**:
- The host container registers infrastructure singletons (stores, telemetry, etc.) that are job-agnostic. Per-job state cannot be injected into it without mutating a shared container.
- Constructing a per-job `ServiceCollection` is the standard .NET DI pattern for scoped configuration overrides (analogous to ASP.NET's per-request DI scope, but without the built-in child-container support).
- Each job gets its own fully isolated module graph; no cross-job contamination.
- The job `ServiceProvider` is `IAsyncDisposable` — disposing it releases all scoped resources at job end.

**Alternatives considered**:
- `IOptionsSnapshot<T>` — designed for per-request scopes in ASP.NET; requires `AddScoped` on the options and a `IServiceScope` from the root container. The root container does not hold the per-job `IConfiguration`, so the snapshot cannot be populated correctly without a custom `IOptionsFactory<T>`. Rejected as more complex than a per-job `ServiceCollection`.
- Named options (`IOptionsMonitor<T>`) — would require a unique per-job name to be propagated to all modules. Not idiomatic; modules would need to know the job name. Rejected.
- Mutating options at runtime (property injection after DI resolution) — violates `init`-only properties. Rejected.

---

### R-2: Building IConfiguration from in-memory JSON on .NET 10 and .NET 4.8

**Question**: `Microsoft.Extensions.Configuration.Json` supports `AddJsonStream`. Is this available on net481?

**Decision**: Yes. `Microsoft.Extensions.Configuration.Json` (5.x and above) targets `netstandard2.0`, which is supported by .NET 4.8. `AddJsonStream(Stream)` builds an `IConfiguration` from a `MemoryStream` populated from `IArtefactStore.ReadTextAsync`. No new package dependencies are required; `Microsoft.Extensions.Configuration.Json` is already referenced in the solution.

**Pattern**:
```csharp
var json = await artefactStore.ReadTextAsync(PackagePaths.MigrationConfigFileName, ct);
using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
```

**net481 note**: `System.Text.Encoding.UTF8.GetBytes` is available on .NET 4.8. The `MemoryStream` + `ConfigurationBuilder` approach is fully net481-compatible.

---

### R-3: net481 serialisation compatibility for migration-config.json

**Question**: `System.Text.Json` source generators are not available on net481. How does `PackageConfigStore` serialise `MigrationOptions` in the TFS agent path?

**Decision**: Use `#if !NET481` / `#else` conditional compilation. On .NET 10: `System.Text.Json.JsonSerializer.Serialize(options, MigrationOptionsSerializerContext.Default.MigrationOptions)`. On net481: `Newtonsoft.Json.JsonConvert.SerializeObject(options, Formatting.Indented)`.

**Rationale**: `Newtonsoft.Json` is already a dependency in the TFS agent (net481). Both serialisers produce structurally identical JSON for POCO objects without custom converters. The `MigrationEndpointOptions` polymorphism is handled by `MigrationEndpointOptions` being a concrete (not abstract) type; the discriminator field (`Type`) is already present as a plain string property, so no custom converter is needed for the write path.

**Alternatives considered**:
- Use only `Newtonsoft.Json` on both runtimes — rejected to avoid introducing `Newtonsoft.Json` as a hard dependency on the .NET 10 path, which otherwise avoids it.
- Use only `System.Text.Json` on both runtimes — `System.Text.Json` is available on net481 as a NuGet package, but the version compatible with net481 lacks some features (e.g., `WriteIndented` with polymorphic support for derived types). Rejected.

---

### R-4: MigrationJob minimal fields — what stays, what moves to migration-config.json

**Question**: Which fields of `MigrationJob` / `Job` are operational dispatch tokens vs. configuration data?

**Decision**:

| Field | Current location | After this change | Rationale |
|---|---|---|---|
| `JobId` | `Job` | `Job` (keep) | Dispatch identity |
| `ConfigVersion` | `Job` | `Job` (keep) | Schema versioning |
| `Package` (URI) | `Job` | `Job` (keep) | Agent needs to know where the package is |
| `Guardrails` | `Job` | `Job` (keep) | Enforcement flags for the agent |
| `Diagnostics` | `Job` | `Job` (keep) | Log level for this job run — operational |
| `Resume` | `Job` | `Job` (keep) | ForceFresh flag — operational |
| `ConfigHash` | `Job` | Remove | Hash of the config stored in the package; redundant once package is on disk |
| `Policies` | `Job` | Remove → `migration-config.json` | Config data, not dispatch token |
| `Modules` | `Job` | Remove → `migration-config.json` | Config data, not dispatch token |
| `Source` | `MigrationJob` | Remove → `migration-config.json` | Config + credentials |
| `Target` | `MigrationJob` | Remove → `migration-config.json` | Config + credentials |
| `Mode` | `MigrationJob` | `MigrationJob` (keep) | Dispatch routing (agent capability selection) |

**Rationale**: The control plane uses `Mode` and `Package.PackageUri` to route jobs to capable agents. `Diagnostics` and `Resume` are per-invocation overrides specified at submission time by the operator — they do not belong in a stored config file. Everything else is operator-authored configuration that belongs in `migration-config.json`.

---

### R-5: EF Core upgrader for MigrationJob schema breaking change

**Question**: The control plane stores `MigrationJob` as serialised JSON in the `JobPayload` column. When `ConfigVersion` "1.0" jobs exist in the database, how does the upgrader migrate them?

**Decision**: An EF Core data migration is required. The upgrader:
1. Reads all stored jobs with `ConfigVersion = "1.0"`.
2. Deserialises the full payload using the v1 schema (anonymous / `JsonElement`).
3. Reconstructs a `MigrationOptions`-equivalent object from `Source`, `Target`, `Modules`, `Policies` fields.
4. Creates a `PackageConfigStore`-compatible JSON blob (wraps in `{ "MigrationPlatform": { ... } }`).
5. Writes it to the package root via `FileSystemArtefactStore` for the URI in `Package.PackageUri`.
6. Strips `Source`, `Target`, `Modules`, `Policies`, `ConfigHash` from the stored JSON.
7. Sets `ConfigVersion = "2.0"` in the stored JSON.
8. Persists the updated payload.

**Note on atomicity**: Because steps 5 and 7 are separate (file write then DB update), the upgrader is not fully atomic. The risk is acceptable: if the process crashes between steps 5 and 7, re-running the upgrader will detect `ConfigVersion = "1.0"` still in the DB, re-read the (already partially written) config file, and overwrite it idempotently. Jobs already in `Running` state are excluded from the upgrade (agent owns the job).

---

### R-5: PackagePaths constant location

**Question**: Where does `PackagePaths.MigrationConfigFileName` live?

**Decision**: `DevOpsMigrationPlatform.Abstractions/PackagePaths.cs`. This file already exists (used by `WorkItemExportOrchestrator` for well-known paths). Add a new `public const string MigrationConfigFileName = "migration-config.json";` constant there.

# Draft Spec: Schema Generation from IOptions DI Registrations

**Status**: Draft — updated after 025-agent-config-package preparatory changes  
**Branch**: 024-teams-module → next branch (025+ complete)  
**Date**: 2026-04-29 (updated 2026-04-30)

---

## 1. Problem Statement

The platform currently has two incompatible binding patterns living side-by-side:

**Pattern A — Monolithic graph binding (current `MigrationOptions`)**  
`MigrationOptions` is bound from the entire `MigrationPlatform` section. Sub-classes (`MigrationPackageOptions`, `MigrationModulesOptions`, etc.) have no `SectionName` and cannot be individually injected — a module or tool that wants only its own config must receive the entire `MigrationOptions` object and navigate into it.

**Pattern B — Flat, path-based individual binding (tools today)**  
`FieldTransformOptions`, `NodeTranslationOptions`, and `IdentityLookupOptions` each register themselves via `IOptions<T>.BindConfiguration(SectionName)`. They are entirely invisible to `MigrationOptions` — no property on `MigrationOptions` points to them. Schema generation walking the `MigrationOptions` type graph will silently omit the entire `Tools` section.

**The gap:**  
The JSON file contains `MigrationPlatform.Tools.*` keys today. Neither `MigrationOptions` nor any schema generator knows about them. Adding new options under a new path requires two non-obvious things: a `SectionName` constant AND a property on some parent container — and there is no enforcement that both exist.

---

## 2. Goal

> Move the **entire** system to `IOptions<T>` via DI as the single, canonical way config is consumed.  
> Generate the JSON Schema from the DI registration graph at build time, not from `MigrationOptions` type properties.  
> Schema is always complete and always accurate because it is derived from the same registrations that the running system uses.

---

## 3. Proposed Architecture

### 3.1 Every options class self-registers

Every options class that participates in the config file:

1. Declares a `public static string SectionName` — the canonical colon-delimited path.  
2. Is registered in DI via `services.AddOptions<T>().BindConfiguration(T.SectionName)` in its own `Add*Services()` extension method.  
3. Is **not** required to appear as a property on any parent class (the type graph is no longer the source of truth).

```csharp
// Before: module has to navigate MigrationOptions
public class WorkItemsModule(IOptions<MigrationOptions> opts) { }

// After: module binds directly to its own slice
public class WorkItemsModule(IOptions<WorkItemsModuleOptions> opts) { }
```

### 3.2 Schema registry — the new source of truth

A new lightweight interface in `Abstractions`:

```csharp
/// <summary>
/// Marks an options class for inclusion in the generated JSON schema.
/// Registered as a singleton; the schema generator resolves all instances at build time.
/// </summary>
public sealed class SchemaOptionsEntry
{
    public Type OptionsType { get; init; }
    public string SectionPath { get; init; }  // e.g. "MigrationPlatform:Tools:FieldTransform"
    public string? Description { get; init; }
}
```

Each `Add*Services()` call that registers `IOptions<T>` also calls:

```csharp
services.AddSingleton(new SchemaOptionsEntry
{
    OptionsType = typeof(FieldTransformOptions),
    SectionPath = FieldTransformOptions.SectionName,
});
```

### 3.3 Schema generator reads the registry

At build time (MSBuild `Exec` task or a `dotnet run` tool), a schema generator:

1. Builds a minimal DI container using all the same `Add*Services()` calls as the real host.
2. Resolves `IEnumerable<SchemaOptionsEntry>` from that container.
3. For each entry, uses NJsonSchema to generate the schema for `OptionsType`.
4. Places the generated sub-schema at the correct JSON path derived from `SectionPath` (splitting on `:`).
5. Assembles and emits `migration.schema.json`.

Because the schema generator uses the **same DI registrations** as the production host, the schema is structurally identical to what the running system will load. If a developer adds a new options class but forgets to call `AddSchemaEntry`, the options simply won't be loaded in production either — so the schema omission is a symptom of the real bug, not a silent schema gap.

### 3.4 Handling polymorphic types (Source/Target)

`MigrationEndpointOptions` is abstract with concrete subtypes registered in `EndpointOptionsTypeRegistry`. The schema generator handles this by:

- Treating `MigrationEndpointOptions` as a JSON Schema `oneOf` with one sub-schema per registered concrete type.
- The `EndpointOptionsTypeRegistry` already exists and is populated during DI setup — the schema generator resolves it from the same container and iterates `GetAllRegisteredTypes()`.
- Each concrete type (`AzureDevOpsEndpointOptions`, `TeamFoundationServerEndpointOptions`, `SimulatedEndpointOptions`) gets its own sub-schema with the `type` discriminator as a `const` string.

### 3.5 Handling lists

`IReadOnlyList<FieldTransformGroupOptions>` → NJsonSchema generates `array` with `items` schema automatically. No special handling needed.

---

## 4. What Changes

### 4.1 `MigrationOptions` — demoted

`MigrationOptions` no longer needs to be the complete shape of the JSON. It becomes a **transition/validation helper** only — used by `MigrationOptionsValidator`, `ConfigurationService.SaveConfigurationAsync`, and the config wizard. It may be deprecated entirely once every consumer migrates to `IOptions<T>` directly.

The monolithic `Bind(configuration.GetSection("MigrationPlatform"))` registration in `AddMigrationPlatformOptions` is replaced by individual flat registrations.

### 4.2 `MigrationModulesOptions`, `MigrationToolsOptions`, `MigrationPoliciesOptions`, `MigrationPackageOptions`

These container classes either:
- **Survive** as convenience groupings for the schema tree structure (they hold no logic, just shape), OR  
- **Are eliminated** and each leaf options class stands alone with its own `SectionName`.

The cleaner approach is to keep the containers as pure schema/structure markers (no constructor injection needed — only the leaf types are injected via `IOptions<T>`).

### 4.3 Every module and tool

Changes constructor injection from:
```csharp
IOptions<MigrationOptions> opts  →  opts.Value.Modules.WorkItems
```
to:
```csharp
IOptions<WorkItemsModuleOptions> opts  →  opts.Value
```

This is the **largest blast radius** — every module and tool needs updating. A migration plan is required.

### 4.4 `SectionName` correctness is enforced, not assumed

If `SectionName` is wrong, `IOptions<T>` gets an empty object (default values) and `ValidateOnStart()` throws. The mismatch is immediately visible. This is the argument the user raised: a wrong `SectionName` is self-detecting because the options won't load.

---

## 5. Cross-Section Config Access — Modules That Need Source/Target

This is the most important design question for the `IOptions<T>`-per-slice model.

### 5.1 The apparent problem

With the flat model, `WorkItemsModule` only receives `IOptions<WorkItemsModuleOptions>`. But what if it also needs:
- The source org URL (to build hyperlinks in exported artefacts)
- The target project name (to construct import URLs)
- The migration `Mode` (to skip export-only logic during an import run)

If you solve this by injecting `IOptions<MigrationOptions>` alongside the slice, you have re-introduced the monolithic dependency and negated the benefit.

### 5.2 Why this is mostly not a real problem

**Modules should not consume endpoint config directly.** They consume *connectors* — `IWorkItemSource`, `ITeamTarget`, etc. — which are *constructed from* the endpoint options by the connector assembly's own DI registration. The module never needs to know the org URL; it calls `source.GetWorkItemAsync(id)` and the connector resolves the URL internally.

```
Config (Source.Url)
  └─ resolved by AzureDevOpsConnector DI setup
       └─ produces IWorkItemSource (injected into WorkItemsModule)
            └─ WorkItemsModule never sees Source.Url
```

This is already how the architecture is designed. Modules that currently navigate `opts.Value.Source.Url` are violating the intended boundary — they should be receiving a resolved service.

### 5.3 The legitimate remaining cases

Some cross-cutting concerns genuinely belong to the *job execution context*, not to any single module's options slice:

| Need | Example | Resolution |
|---|---|---|
| Migration `Mode` | Skip target writes during export | `IMigrationJobContext.Mode` (resolved service, not raw options) |
| Source endpoint URL | Embed source hyperlinks in exported artefacts | `ISourceEndpointInfo.Url` (provided by connector registration) |
| Package path | Know where to write artefact files | `IArtefactStore` abstraction — already exists |
| Target project name | Used in import log messages | `ITargetEndpointInfo.Project` (provided by connector registration) |

### 5.4 Proposed `IMigrationJobContext`

A new thin service in `Abstractions`:

```csharp
/// <summary>
/// Read-only view of the current job's top-level execution context.
/// Registered as a scoped singleton for the duration of a migration job.
/// Provides the resolved values that modules genuinely need cross-section.
/// NOT a substitute for module-specific IOptions<T>.
/// </summary>
public interface IMigrationJobContext
{
    string Mode { get; }            // "Export" | "Import" | "Migrate"
    string PackagePath { get; }     // resolved, expanded path
    string ConfigVersion { get; }   // for upgrader checks
}
```

This is registered from the job's resolved config, not from raw `IOptions<MigrationOptions>`. Any module that truly needs Mode or PackagePath injects `IMigrationJobContext` rather than options.

**Key rule**: `IMigrationJobContext` is read-only and carries only resolved scalar values — no sub-graphs, no options objects. It is NOT a back door into `MigrationOptions`.

### 5.5 Connector-side endpoint info

For source/target URL access, each connector assembly registers its own endpoint info service:

```csharp
// Registered by AddAzureDevOpsSourceServices():
services.AddSingleton<ISourceEndpointInfo>(sp => {
    var opts = sp.GetRequiredService<IOptions<AzureDevOpsEndpointOptions>>().Value;
    return new AzureDevOpsSourceEndpointInfo(opts.ResolvedUrl, opts.Project);
});
```

A module that needs the source URL for link generation injects `ISourceEndpointInfo`, not `IOptions<AzureDevOpsEndpointOptions>` — keeping it connector-agnostic.

---

## 6. Implications / Risks

| Area | Impact | Mitigation |
|---|---|---|
| **Blast radius** | Every module/tool constructor changes | Can be done incrementally — `MigrationOptions` and individual `IOptions<T>` can coexist during transition |
| **`MigrationOptionsValidator`** | Currently validates the whole graph at once | Must be split per-type or aggregated across per-type `IValidateOptions<T>` |
| **`ConfigurationService.SaveConfigurationAsync`** | Serialises `MigrationOptions` to write config | Must reconstruct the full JSON from individual options — or keep `MigrationOptions` for serialisation only |
| **Config wizard (`ConfigNewCommand`)** | Builds `MigrationOptions` then saves | Needs to build individual options and write them to their correct paths |
| **Schema generator needs a full DI container** | The generator must reference all connector assemblies | Requires a schema-generator host project that references the same assemblies as the CLI |
| **Polymorphic endpoints** | `Source`/`Target` are abstract — each connector must register its `SchemaOptionsEntry` | Already done for serialisation via `EndpointOptionsTypeRegistry` — extend the same pattern |
| **net481 (TFS agent)** | Schema generation is net10.0 only — TFS agent just consumes config | No change to TFS agent |
| **`IMigrationJobContext`** | New abstraction needed for cross-section scalar values | Small; lives in `Abstractions`; replaces ad-hoc `MigrationOptions` reads |
| **Connector endpoint info** | Each connector must register `ISourceEndpointInfo` / `ITargetEndpointInfo` | Follows same pattern as existing connector DI extensions |

---

## 7. Schema Generator Implementation Sketch

```
src/
  DevOpsMigrationPlatform.SchemaGenerator/
    Program.cs             — builds DI container, resolves SchemaOptionsEntry[], emits schema
    SchemaGeneratorHost.cs — registers all the same Add*Services() calls as the CLI host
```

MSBuild target in `DevOpsMigrationPlatform.CLI.Migration.csproj`:

```xml
<Target Name="GenerateConfigSchema" AfterTargets="Build" Condition="'$(TargetFramework)' == 'net10.0'">
  <Exec Command="dotnet run --project $(SolutionDir)src\DevOpsMigrationPlatform.SchemaGenerator\DevOpsMigrationPlatform.SchemaGenerator.csproj -- --output $(OutDir)migration.schema.json" />
</Target>
```

The schema file is:
- Copied to the CLI output directory alongside the binary
- Committed to source for IDE integration (VS Code `json.schemas` setting)
- Validated in CI to detect drift

---

## 8. Open Questions

1. **Keep `MigrationOptions` for serialisation?**  
   `SaveConfigurationAsync` and the config wizard need to write a complete, ordered JSON file. We can keep `MigrationOptions` as a write-time DTO only, distinct from the runtime `IOptions<T>` bindings. Is this acceptable or should we build a JSON assembly path from individual `SchemaOptionsEntry` records?

2. **Incremental vs big-bang migration?**  
   The safest path is to migrate one section at a time (`Tools` first, since those already use the correct pattern) and defer removing the monolithic `MigrationOptions` bind until all consumers are migrated. Does that match the preferred approach?

3. **Container classes (`MigrationToolsOptions`, `MigrationModulesOptions`)**  
   Do we keep them purely for JSON shape (they appear in the schema but nothing in production is injected as `IOptions<MigrationToolsOptions>`)? Or do we register them too so a module can get the whole `Tools` section if it genuinely needs it?

4. **Schema generator project or MSBuild task?**  
   A separate `SchemaGenerator` project is cleanest but adds a project to the solution. Alternatively, the CLI itself can expose a hidden `schema export` command that emits the schema. The latter avoids a new project but couples schema generation to the CLI binary.

5. **VS Code `json.schemas` integration**  
   Should the committed schema be registered in `.vscode/settings.json` to give IntelliSense on `migration.json` files automatically? This is a developer-experience win with very low cost.

---

## 9. Actual Boundary Audit (Code Scan Results)

A scan of all `src/**/*.cs` files was performed looking for:
- Direct injection of `IOptions<MigrationOptions>`  
- Navigation via `.Source`, `.Target`, `.Package`, `.Modules`, `.Policies` on options objects inside modules/tools

### Finding: The config file never travels to the agent ~~(RESOLVED — feature 025)~~

> **⚠️ UPDATE (2026-04-30):** Feature `025-agent-config-package` resolved this. Config now travels to the agent via `Job.ConfigPayload`. See updated flow below.

The actual implemented flow (post-025) is:

```
migration.json
  └─ CLI loads via ConfigurationService → MigrationOptions (C# object, in-process only)
       └─ QueueCommand serialises MigrationOptions → JSON string → Job.ConfigPayload
            └─ ControlPlaneClient.SubmitAsync → POST /jobs  (JSON over HTTP, ConfigPayload inline)
                 └─ ControlPlane stores Job in JobStore
                      └─ Agent polls → DequeueAsync → receives Job with ConfigPayload
                           └─ Agent writes ConfigPayload → migration-config.json at package root
                                └─ Agent reads migration-config.json → IConfiguration
                                     └─ Per-job ServiceCollection built with IOptions<T> bound from IConfiguration
                                          └─ Modules resolved from per-job ServiceProvider
```

The **config travels through `Job.ConfigPayload`** — a raw JSON string property on `Job`. The agent materialises it to `migration-config.json` at the package root on startup before any module executes. The `MigrationOptions` C# object is ephemeral — it exists only within the CLI process during job construction.

Key implementation details in `Job.cs`:
```csharp
/// Raw JSON contents of the migration-config file.
/// Set by the CLI from the scenario config file before job submission.
/// The agent writes this to migration-config.json at the package root on job startup,
/// before any module reads the config.
public string? ConfigPayload { get; init; }
```

### What `Job` contains after feature 025

`Job` (v2.0) is now a **minimal dispatch token** — credentials and config are in `ConfigPayload`/`migration-config.json`:

| Field | Status | Purpose |
|---|---|---|
| `JobId` | Kept | Dispatch identity |
| `ConfigVersion` | Kept (`"2.0"`) | Schema versioning |
| `Kind` | Kept | Operation routing |
| `Connectors` | Kept | Agent capability matching |
| `Package` (URI) | Kept | Where the package lives |
| `Diagnostics` | Kept | Per-run log level |
| `Resume` | Kept | ForceFresh flag |
| `ConfigPayload` | **New** | Full `MigrationOptions` as JSON — written to `migration-config.json` by agent |
| `Source` / `Target` | **Removed** | Now in `migration-config.json` |
| `Modules` / `Policies` | **Removed** | Now in `migration-config.json` |
| `ConfigHash` | **Removed** | Redundant once config is on disk |

### Finding: No module or tool injects `IOptions<MigrationOptions>`

**There are zero instances** of `IOptions<MigrationOptions>` being injected anywhere in module or tool code. The monolithic binding exists only in:
- `MigrationPlatformServiceExtensions.AddMigrationPlatformOptions` (the registration itself)
- `MigrationOptionsValidator` (the whole-graph validator)
- `ConfigurationService` (load/save, the wizard)

### Finding: Modules access `context.Job`, not config options

What modules actually access (`WorkItemsModule`, `NodesModule`, `TeamsModule`, `IdentitiesModule`) are properties on `context.Job` — the `MigrationJob` runtime DTO received from the control plane queue, not `MigrationOptions`. These are the correct types to access.

### Finding: Tool options on the agent are always defaults — ~~a current silent bug~~ RESOLVED

> **⚠️ UPDATE (2026-04-30):** Feature `025-agent-config-package` resolved this bug. See below.

Previously, `MigrationAgentServiceExtensions` bound `IOptions<FieldTransformOptions>` etc. from `appsettings.json`, which had no `MigrationPlatform` section. All tool options were silently empty.

**This is now fixed.** The agent receives `Job.ConfigPayload` (the full serialised `MigrationOptions` JSON), writes it to `migration-config.json` at the package root, then builds a per-job `IConfiguration` from that file. A fresh `ServiceCollection` is constructed per job with all `IOptions<T>` bound from the per-job `IConfiguration`:

```
migration.json:  MigrationPlatform.Tools.FieldTransform.TransformGroups[...]
                    ↓  CLI serialises to JSON → Job.ConfigPayload
Job.ConfigPayload:  { "MigrationPlatform": { "Tools": { "FieldTransform": { ... } } } }
                    ↓  Agent writes to package
migration-config.json: at package root
                    ↓  Agent reads → IConfiguration
IOptions<FieldTransformOptions>.Value  ← correctly populated per-job
```

The implementation used **a variant of Fix B** (raw JSON blob in the job, not a typed projection). The `Job.ConfigPayload` string is opaque to the control plane and agent router — it is the complete `MigrationPlatform` JSON section, structurally identical to the source `migration.json`. This is simpler than Fix A and does not require `MigrationJob` to grow typed tool-config properties.

### What this means for the IOptions migration proposal

The per-job `IOptions<T>` override is **already implemented and working**. The IOptions migration scope is therefore scoped to the **CLI-side** only (moving module/tool constructors from `IOptions<MigrationOptions>` navigation to direct `IOptions<T>` injection) and the **schema generation** concern. The agent-side transport is solved.

---

## 10. Proposed Next Steps

> **Updated 2026-04-30** — Steps 1–2 of the original list are unchanged. Step 3 (agent-side config transport) is **complete** via `025-agent-config-package`. Remaining work is CLI-side DI cleanup and schema generation.

**Already done (025-agent-config-package):**
- `Job.ConfigPayload` carries the full `MigrationOptions` JSON to the agent
- Agent materialises `migration-config.json` at the package root
- Per-job `ServiceCollection` built with `IOptions<T>` bound from per-job `IConfiguration`
- `IPackageConfigStore` abstraction + `PackageConfigStore` implementation shipped
- `Job` v2.0 schema: `Source`, `Target`, `Modules`, `Policies`, `ConfigHash` removed

**Remaining work:**
1. Add `SchemaOptionsEntry` to `Abstractions` and `AddSchemaEntry<T>()` helper  
2. Migrate `Tools` (FieldTransform, NodeTranslation, IdentityLookup) constructor injection as pilot — they already use `SectionName`, just need `SchemaOptionsEntry` registration  
3. Validate schema output matches `docs/configuration.md`  
4. Decide on `MigrationOptions` fate (Open Question 1) before migrating Modules and Policies  
5. Full migration of all module constructors from `IOptions<MigrationOptions>` navigation to direct `IOptions<T>` injection  
6. Wire schema generation into MSBuild and CI  
7. Register `.vscode/settings.json` `json.schemas` entry for IDE IntelliSense on `migration.json`  

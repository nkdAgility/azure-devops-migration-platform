# Draft Spec: Schema Generation from IOptions DI Registrations

**Status**: Draft — discussion in progress  
**Branch**: 024-teams-module  
**Date**: 2026-04-29  

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

### Finding: The config file never travels to the agent

The complete flow is:

```
migration.json
  └─ CLI loads via ConfigurationService → MigrationOptions (C# object, in-process only)
       └─ QueueCommand builds MigrationJob from MigrationOptions
            └─ ControlPlaneClient.SubmitAsync → POST /jobs  (JSON over HTTP)
                 └─ ControlPlane stores MigrationJob in JobStore (in-memory or persistent)
                      └─ Agent polls → DequeueAsync → receives MigrationJob (JSON → deserialised)
                           └─ modules receive ExportContext / ImportContext which contains the MigrationJob
```

The **config file (`migration.json`) is never sent to the agent**. It is read once by the CLI, converted into a `MigrationJob` DTO, serialised to JSON, `POST`ed to the Control Plane over HTTP, stored, then dequeued by the agent and deserialised back into a `MigrationJob`. The `MigrationOptions` C# object is ephemeral — it exists only within the CLI process during job construction.

This is confirmed by `ControlPlaneClient.SubmitAsync`:
```csharp
await _http.PostAsJsonAsync("/jobs", job, _jsonOptions, ct)
```
...and `JobAgentWorker.OnMigrationJobAsync(MigrationJob job, ...)` which receives the already-deserialised job.

### What `MigrationJob` contains vs `MigrationOptions`

`MigrationJob` is NOT a projection of `MigrationOptions`. It is a **resolved, validated, execution-ready contract** with its own parallel types:

| `MigrationOptions` (config-time) | `MigrationJob` (runtime) | Difference |
|---|---|---|
| `MigrationPackageOptions` (raw path, env vars) | `JobPackage` (resolved `file:///` URI) | CLI expands paths before submitting |
| `MigrationModulesOptions` (typed per-module config) | `List<JobModule>` (name + scopes + extensions bag) | Lossy projection — only what the agent needs |
| `MigrationPoliciesOptions` (nested sub-objects) | `JobPolicies` (flat scalars only) | Simplified for transport |
| `MigrationEndpointOptions` (polymorphic) | `MigrationEndpointOptions` (same type — carried as-is) | Endpoint options are shared |

The CLI's `QueueCommand.BuildModules(config)` method is the translation boundary — it maps `MigrationModulesOptions` → `List<JobModule>`. Config detail that the agent doesn't need is dropped here.

### Finding: No module or tool injects `IOptions<MigrationOptions>`

**There are zero instances** of `IOptions<MigrationOptions>` being injected anywhere in module or tool code. The monolithic binding exists only in:
- `MigrationPlatformServiceExtensions.AddMigrationPlatformOptions` (the registration itself)
- `MigrationOptionsValidator` (the whole-graph validator)
- `ConfigurationService` (load/save, the wizard)

### Finding: Modules access `context.Job`, not config options

What modules actually access (`WorkItemsModule`, `NodesModule`, `TeamsModule`, `IdentitiesModule`) are properties on `context.Job` — the `MigrationJob` runtime DTO received from the control plane queue, not `MigrationOptions`. These are the correct types to access.

### Finding: Tool options on the agent are always defaults — a current silent bug

`MigrationAgentServiceExtensions` calls:
```csharp
builder.Services.AddFieldTransformToolServices();   // binds IOptions<FieldTransformOptions>
builder.Services.AddNodeTranslationToolServices();  // binds IOptions<NodeTranslationOptions>
```

These bind via `BindConfiguration("MigrationPlatform:Tools:FieldTransform")` etc.

The agent's `appsettings.json` contains **no `MigrationPlatform` section**:
```json
{ "Logging": { ... }, "Telemetry": { ... } }
```

**Result: `FieldTransformOptions`, `NodeTranslationOptions`, `IdentityLookupOptions` are always empty/default on the agent. Any transform rules or node mappings the user configured in `migration.json` are silently ignored.**

This is a pre-existing bug, not introduced by the IOptions migration proposal. The tool config is defined in the CLI-side `migration.json`, converted to a `MigrationJob`, but `JobModule.Extensions` only carries enabled flags and generic parameters — the full typed options (regex patterns, transform rules) are **never transferred to the agent**.

The flow that should exist but doesn't:

```
migration.json:  MigrationPlatform.Tools.FieldTransform.TransformGroups[...]
                                      ↓  MISSING STEP
MigrationJob:   ??? FieldTransform config ???
                                      ↓
Agent:          IOptions<FieldTransformOptions>.Value  ← always empty
```

### What this means for the IOptions migration proposal

The IOptions-per-slice model on the agent side is currently a **no-op** for tool options — they bind to nothing. This is an **existing architectural gap that the IOptions migration must solve**, not ignore.

Two ways to fix it:

**Fix A — Embed serialised tool config in `MigrationJob`**  
Add typed tool config to `MigrationJob` (or a `JobTools` bag), populated by the CLI from `MigrationOptions.Tools.*` during job construction. The agent deserialises it and uses `IOptionsFactory`/`IPostConfigureOptions` to override the empty binding.

**Fix B — Pass the raw config section as a JSON blob in the job**  
`MigrationJob` carries a `RawConfig` JSON string (the `MigrationPlatform` section). The agent creates an `IConfiguration` from it per-job and overrides the empty `IOptions<T>` bindings. This is simpler but makes the job transport opaque.

**Fix A is preferred** — it keeps the job contract explicit and typed, consistent with the rest of `MigrationJob`. The schema registry approach (section 3.2) naturally extends to this: every `SchemaOptionsEntry` that has a corresponding agent-side `IOptions<T>` also needs a `JobXxxOptions` projection in `MigrationJob`.

This finding significantly changes the IOptions migration scope — the blast radius includes **`MigrationJob` itself**, which must grow to carry tool config explicitly.

---

## 10. Proposed Next Steps

1. Add `SchemaOptionsEntry` to `Abstractions`  
2. Add `AddSchemaEntry<T>()` helper to `MigrationPlatformServiceExtensions`  
3. Migrate `Tools` (FieldTransform, NodeTranslation, IdentityLookup) as the pilot — they already use the right pattern  
4. Validate schema output matches `docs/configuration.md`  
5. Decide on `MigrationOptions` fate (question 1 above) before migrating Modules and Policies  
6. Full migration of all module constructors  
7. Wire schema generation into MSBuild and CI  

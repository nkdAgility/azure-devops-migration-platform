# Implementation Plan: Schema Generation from IOptions DI Registrations

**Branch**: `028-ioptions-schema-gen` | **Date**: 2026-04-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/028-ioptions-schema-gen/spec.md`

## Summary

Replace the `ActiveJobConfigState` global mutable singleton with a flat `IOptions<T>` per-slice injection pattern across all modules and tools. Introduce a `SchemaOptionsEntry` registry so that JSON Schema is derived at build time from the same DI registrations the running system uses. Add `IAgentJobContext` (in `Abstractions.Agent`) for cross-cutting job scalars, and `ISourceEndpointInfo`/`ITargetEndpointInfo` (in `Abstractions.Agent`) for connector-registered endpoint access. Wire `migration.schema.json` into the CLI's Tier 0 validation before `queue` submission. Delete `ActiveJobConfigState` once all consumers are migrated. `MigrationOptions` is reduced to a transient deserialisation bootstrap shim with no module injection role.

## Technical Context

**Language/Version**: C# 12, .NET 10 (schema generator + CLI + agent); .NET 4.8 (TFS agent — unaffected)  
**Primary Dependencies**: `Microsoft.Extensions.Options`, `NJsonSchema` (schema generation + Tier 0 validation), existing `IArtefactStore`/`IStateStore` abstractions  
**Storage**: N/A — no new persistence; schema file written to output directory  
**Testing**: MSTest + Reqnroll (Gherkin feature files in `features/`), `[TestCategory("SystemTest_Simulated")]` for end-to-end  
**Target Platform**: CLI (`net10.0`), MigrationAgent (`net10.0`), TfsMigrationAgent (`net481` — no changes required)  
**Project Type**: Infrastructure refactor + new build-time tool (`SchemaGenerator`)  
**Performance Goals**: Schema generation completes in < 5s on a development machine; Tier 0 JSON Schema validation adds < 50ms to `queue` command startup  
**Constraints**: `Abstractions` and `Abstractions.Agent` must remain multi-targeted (`net481;net10.0`); `SchemaGenerator` is `net10.0` only; TFS agent must compile and pass all tests throughout every migration step  
**Scale/Scope**: ~6 modules, ~3 tools, ~3 connector assemblies to migrate; 1 new project (`SchemaGenerator`); ~15 new or modified types

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All guardrail files, context files, and relevant docs have been read in this session.

- [x] **Package-First (I):** No direct source-to-target migration. This feature is a DI/config refactor with no new package I/O. The `SchemaGenerator` writes only to the build output directory, not to a migration package.
- [x] **Streaming (II):** Not applicable — no new module processing. Existing streaming guarantees are preserved; this feature does not touch `EnumerateAsync` or revision processing.
- [x] **WorkItems Layout (III):** Not applicable — no changes to the package layout.
- [x] **Checkpointing (IV):** Not applicable — no new module state.
- [x] **Module Isolation (V):** This feature **improves** isolation by removing `ActiveJobConfigState` (global mutable state accessed from modules) and replacing it with `IOptions<T>` constructor injection. All persistence remains through `IArtefactStore`/`IStateStore`.
- [x] **Separation of Planes (VI):** Respected. The Tier 0 JSON Schema validation addition stays entirely within the CLI layer. No migration logic is added to the CLI. The `SchemaGenerator` is a build-time tool, not a runtime component.
- [x] **Determinism (VII):** Schema generation is deterministic — same DI registrations produce the same schema. The `SectionName` constants are compile-time values.
- [x] **ATDD-First (VIII):** All user stories in spec.md have Given/When/Then scenarios. Feature files will be written before implementation.
- [x] **SOLID & DI (IX):** This feature IS the enforcement of the `IOptions<T>` with `SectionName` pattern across all modules and tools. `IAgentJobContext`, `ISourceEndpointInfo`, and `ITargetEndpointInfo` interfaces are defined in `Abstractions.Agent`. All registrations in dedicated `Add*Services` extension methods.
- [x] **Full Connector Coverage (XI):** Each connector assembly (Simulated, AzureDevOps, TFS) must register `SchemaOptionsEntry` for its options types and register `ISourceEndpointInfo`/`ITargetEndpointInfo`. No connector is exempt from self-registration. TFS is source-only so registers `ISourceEndpointInfo` only.

## Observability Contract

*GATE: Must be completed before task generation.*

> This feature introduces two operations with observable boundaries. It does not introduce a runtime migration module — O-4 ProgressEvent emission is not applicable. Existing `WellKnownActivitySourceNames.Cli` and `WellKnownActivitySourceNames.Migration` are reused; no new meter or activity source names are required.

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|-----------|---------------|-----------------|--------------------------|-----------------|--------------------------|
| Schema generation | `SchemaGeneratorHost.RunAsync` | `schema.generate` (source: `WellKnownActivitySourceNames.Migration`) — **build-time tool; span is advisory/no-op unless OTel is wired by caller** | None — build-time tool; no runtime OTel exporter | `Information`: "Schema generation started — {EntryCount} entries"; `Information`: "Schema generation succeeded — {EntryCount} entries in {DurationMs}ms → {OutputPath}"; `Error`: "Schema generation failed at step '{Step}': {Error}"; `Error`: "Duplicate SectionName '{SectionPath}' registered by {Type1} and {Type2}" | N/A — build tool |
| Tier 0 JSON Schema validation | `QueueCommand` (before `LoadConfigurationAsync`) | None — synchronous pre-flight, no distributed trace needed | None | `Error`: "Config validation failed: {JsonPath} — {Constraint} ({ConfigFile})"; `Warning`: "Schema validation skipped — schema file not found at {ExpectedSchemaPath}" | N/A — CLI pre-flight |
| `IAgentJobContext` resolution | `AgentJobContext` (registered per-job) | None — DI registration, not an operation | None | `Debug`: "Agent job context resolved — Mode={Mode} ConfigVersion={ConfigVersion}" | N/A |

### Wiring Checklist

- [x] **O-1 ActivitySource:** `schema.generate` uses `WellKnownActivitySourceNames.Migration` (existing). No new source names required.
- [x] **O-2 Metric instruments:** No new metric instruments. Schema generation and Tier 0 validation are not metered operations.
- [x] **O-2 Meter registration:** No new meters. No changes to MigrationAgent or TFS host registration.
- [x] **O-3 Log structured params:** All log calls use structured params (`{EntryCount}`, `{DurationMs}`, `{OutputPath}`, `{JsonPath}`, `{Constraint}`, `{ConfigFile}`, `{ExpectedSchemaPath}`, `{Mode}`, `{ConfigVersion}`).
- [x] **O-4 IProgressSink wiring:** Not applicable — no runtime migration module introduced.
- [x] **O-4 ModuleCounters property:** Not applicable.
- [x] **O-4 CLI row:** Not applicable — no new progress bar row.
- [x] **DI wiring verified:** `IAgentJobContext` → `AgentJobContext` registered in `MigrationAgentServiceExtensions`. `ISourceEndpointInfo`/`ITargetEndpointInfo` registered by each connector's own `Add*Services` extension.

### Tests Required for Observability

- [ ] Unit test: `SchemaGenerator` logs `Information` at start and success, with `EntryCount > 0`
- [ ] Unit test: `SchemaGenerator` logs `Error` and fails when two entries share a `SectionPath`
- [ ] Unit test: `QueueCommand` Tier 0 validation logs `Error` with `JsonPath` when config contains an unknown key
- [ ] Unit test: `QueueCommand` Tier 0 validation logs `Warning` when `migration.schema.json` is absent

- [ ] Unit test: verify `ActivitySource.StartActivity` is called with correct span name (use `TestActivityListener` or mock)
- [ ] Unit test: verify `IMigrationMetrics` receives attempt/completion/error calls (inject mock `IMigrationMetrics`)
- [ ] Unit test: verify `IProgressSink.EmitAsync` is called at start, per-item (or per batch ≤50), and completion
- [ ] Unit test: verify `ILogger` receives `Information` at start and end with correct structured parameters
- [ ] Simulated system test: run scenario end-to-end → CLI output shows progress bar row for this module

## Project Structure

### Documentation (this feature)

```text
specs/028-ioptions-schema-gen/
├── spec.md
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── contracts/           ← Phase 1 output
│   ├── IAgentJobContext.md
│   ├── ISourceEndpointInfo.md
│   └── SchemaOptionsEntry.md
└── tasks.md             ← Phase 2 output (speckit.tasks)
```

### Source Code Changes

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   └── Options/
│       └── SchemaOptionsEntry.cs               ← NEW: registry record
│   └── Configuration/
│       └── IConfigSchemaValidator.cs           ← NEW: CLI Tier 0 contract
│
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   └── Context/
│       ├── IAgentJobContext.cs                 ← NEW
│       ├── ISourceEndpointInfo.cs              ← NEW
│       └── ITargetEndpointInfo.cs              ← NEW
│   └── Lease/
│       └── ActiveJobConfigState.cs             ← DELETE (after all consumers migrated)
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   └── Context/
│       └── AgentJobContext.cs                  ← NEW: IAgentJobContext impl
│   └── Modules/
│       ├── WorkItemsModule.cs                  ← MODIFY: remove ActiveJobConfigState, inject IOptions<WorkItemsModuleOptions>, IAgentJobContext, ISourceEndpointInfo, ITargetEndpointInfo
│       ├── TeamsModule.cs                      ← MODIFY: same
│       ├── NodesModule.cs                      ← MODIFY: same
│       └── IdentitiesModule.cs                 ← MODIFY: same
│   └── MigrationAgentServiceExtensions.cs      ← MODIFY: register IAgentJobContext
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated/
│   └── SimulatedConnectorServiceExtensions.cs  ← MODIFY: register SchemaOptionsEntry + ISourceEndpointInfo + ITargetEndpointInfo
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   └── AzureDevOpsConnectorServiceExtensions.cs ← MODIFY: register SchemaOptionsEntry + ISourceEndpointInfo + ITargetEndpointInfo
│
├── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
│   └── TfsConnectorServiceExtensions.cs        ← MODIFY: register SchemaOptionsEntry + ISourceEndpointInfo (source only)
│
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Config/
│       └── JsonSchemaConfigValidator.cs        ← NEW: IConfigSchemaValidator impl (NJsonSchema)
│
├── DevOpsMigrationPlatform.CLI.Migration/
│   └── Commands/
│       └── QueueCommand.cs                     ← MODIFY: Tier 0 schema validation before LoadConfigurationAsync
│   └── DevOpsMigrationPlatform.CLI.Migration.csproj ← MODIFY: copy migration.schema.json to output
│
├── DevOpsMigrationPlatform.MigrationAgent/
│   └── JobAgentWorker.cs                       ← MODIFY: remove ActiveJobConfigState population; build IAgentJobContext from parsed MigrationOptions
│
└── DevOpsMigrationPlatform.SchemaGenerator/    ← NEW PROJECT
    ├── DevOpsMigrationPlatform.SchemaGenerator.csproj
    ├── Program.cs
    └── SchemaGeneratorHost.cs

features/
└── cli/
    └── schema-validation.feature               ← NEW: Tier 0 schema validation Gherkin
export/
    └── ioptions-migration.feature              ← NEW: module config isolation Gherkin

tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/
    └── SchemaValidation/
        ├── SchemaValidationSteps.cs            ← NEW
        └── SchemaValidationContext.cs          ← NEW
└── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
    └── Context/
        └── AgentJobContextTests.cs             ← NEW
```

**Structure Decision**: Single `SchemaGenerator` project (`net10.0` only) that references all connector assemblies and `Infrastructure` to build the DI container. The CLI project gets a build dependency on `SchemaGenerator` output via an MSBuild `Exec` target and a `<Content CopyToOutputDirectory="PreserveNewest">` item for `migration.schema.json`.

## Complexity Tracking

No constitution violations. The `SchemaGenerator` project is the minimal additional project needed — the alternative (a hidden `schema export` CLI command) would couple schema generation to the CLI binary and prevent build-time schema drift detection without running the full CLI.

---

## Phase 0: Research

> **Output**: [research.md](research.md)

### Unknowns to Resolve

| Unknown | Research Task |
|---------|--------------|
| NJsonSchema API for `oneOf` discriminated union generation | Verify `JsonSchema.OneOf` collection and `JsonSchemaProperty.IsRequired` for `type` discriminator constant |
| MSBuild `Exec` target dependency ordering for schema generator | Verify `AfterTargets="Build"` vs `BeforeTargets` and output file freshness |
| `IOptions<T>` registration from `Abstractions` multi-target (`net481;net10.0`) | Confirm `SchemaOptionsEntry` can be registered in a multi-targeted project without `Microsoft.Extensions.Options` version conflict |
| `ActiveJobConfigState` usage in TfsMigrationAgent | Scan TfsMigrationAgent for `ActiveJobConfigState` references — must remain functional throughout migration |

---

## Phase 1: Design & Contracts

> **Prerequisites**: research.md complete
> **Outputs**: data-model.md, contracts/, quickstart.md

### New Types

#### `SchemaOptionsEntry` (in `Abstractions`)

```csharp
/// <summary>
/// Registration record linking an options type to its canonical config section path.
/// Registered as a singleton by each Add*Services() call that also registers IOptions&lt;T&gt;.
/// Resolved in bulk by the SchemaGenerator at build time.
/// </summary>
public sealed class SchemaOptionsEntry
{
    public required Type OptionsType { get; init; }
    public required string SectionPath { get; init; }   // e.g. "MigrationPlatform:Tools:FieldTransform"
    public string? Description { get; init; }
}
```

Registration helper (extension method on `IServiceCollection`):
```csharp
public static IServiceCollection AddSchemaEntry<T>(
    this IServiceCollection services, string? description = null)
    where T : class
    => services.AddSingleton(new SchemaOptionsEntry
    {
        OptionsType = typeof(T),
        SectionPath = (string)typeof(T).GetField("SectionName",
            BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,
        Description = description
    });
```

#### `IAgentJobContext` (in `Abstractions.Agent`)

```csharp
/// <summary>
/// Read-only view of the current agent job's execution context.
/// Registered per-job in the per-job ServiceCollection.
/// Provides scalar values modules need without exposing MigrationOptions.
/// </summary>
public interface IAgentJobContext
{
    string Mode { get; }            // "Export" | "Import" | "Prepare" | "Migrate"
    string PackagePath { get; }     // resolved, expanded absolute path
    string ConfigVersion { get; }   // e.g. "2.0"
}
```

#### `ISourceEndpointInfo` / `ITargetEndpointInfo` (in `Abstractions.Agent`)

```csharp
public interface ISourceEndpointInfo
{
    string Url { get; }
    string Project { get; }
    string ConnectorType { get; }   // "AzureDevOpsServices" | "TeamFoundationServer" | "Simulated"
}

public interface ITargetEndpointInfo
{
    string Url { get; }
    string Project { get; }
    string ConnectorType { get; }
}
```

#### `IConfigSchemaValidator` (in `Abstractions`)

```csharp
/// <summary>
/// Validates a raw JSON config string against the platform's migration.schema.json.
/// Used by QueueCommand as a Tier 0 pre-flight check.
/// </summary>
public interface IConfigSchemaValidator
{
    /// <summary>
    /// Returns an empty collection on success; one entry per violation on failure.
    /// </summary>
    IReadOnlyList<SchemaValidationError> Validate(string rawJson);
}

public sealed class SchemaValidationError
{
    public required string JsonPath { get; init; }
    public required string Constraint { get; init; }
}
```

### Module Migration Pattern

Before (current):
```csharp
// Constructor injects ActiveJobConfigState
var projectName = _activeJobConfig?.Current?.Source?.GetProject() ?? string.Empty;
var opts = _activeJobConfig?.Current?.Modules?.WorkItems ?? new WorkItemsModuleOptions();
```

After (target):
```csharp
// Constructor injects IOptions<WorkItemsModuleOptions>, IAgentJobContext, ISourceEndpointInfo
var projectName = _sourceEndpointInfo.Project;
var opts = _options;   // IOptions<WorkItemsModuleOptions>.Value bound at DI build time
```

### Connector Registration Pattern

Each connector's `Add*Services()` extension adds:
```csharp
services.AddSchemaEntry<SimulatedEndpointOptions>();
services.AddSingleton<ISourceEndpointInfo>(sp => {
    var opts = sp.GetRequiredService<IOptions<SimulatedEndpointOptions>>().Value;
    return new SimulatedSourceEndpointInfo(opts.Project);
});
```

### `SchemaGenerator` — build-time host

```csharp
// Program.cs — net10.0 only
var services = new ServiceCollection();
// Register all the same Add*Services() calls as the production host
AddAllPlatformServices(services);
var provider = services.BuildServiceProvider();

var entries = provider.GetServices<SchemaOptionsEntry>().ToList();
// Build JSON Schema tree from entries using NJsonSchema
// Write to --output path
```

MSBuild target in `CLI.Migration.csproj`:
```xml
<Target Name="GenerateConfigSchema" AfterTargets="Build" Condition="'$(TargetFramework)' == 'net10.0'">
  <Exec Command="dotnet run --project $(SolutionDir)src\DevOpsMigrationPlatform.SchemaGenerator\ -- --output $(OutDir)migration.schema.json" />
</Target>
<Content Include="$(OutDir)migration.schema.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('$(OutDir)migration.schema.json')" />
```

CI drift check:
```yaml
- name: Verify schema not drifted
  run: |
    git diff --exit-code migration.schema.json
```

---

## Complexity Tracking

No constitution violations introduced. No architectural workarounds required.

| Concern | Decision | Rationale |
|---------|----------|-----------|
| SchemaGenerator as separate project | Adopted | Avoids coupling schema generation to CLI binary; enables independent build-time invocation |
| `MigrationOptions` retained as bootstrap shim | Retained transiently | `JobAgentWorker` needs to parse `migration-config.json` before building per-job DI; once the agent can bind `IConfiguration` directly from raw JSON, `MigrationOptions` can be deleted |
| `net481` multi-target for `SchemaOptionsEntry` | Required | `Abstractions` is multi-targeted; `SchemaOptionsEntry` carries only a `Type` and two strings — no platform APIs needed |

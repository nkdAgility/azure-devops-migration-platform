# Implementation Plan: Fix — Tool Config Never Reaches the Agent

**Branch**: `025-agent-config-package` | **Date**: 2026-04-29 | **Spec**: [spec.md](spec.md)

## Summary

The agent currently binds `IOptions<FieldTransformOptions>`, `IOptions<NodeTranslationOptions>`, and `IOptions<IdentityLookupOptions>` from `appsettings.json` at host startup. The agent's `appsettings.json` has no `MigrationPlatform` section, so every tool option is always default/empty — and all user-configured rules are silently ignored at runtime.

The fix: before submitting a job, the CLI writes `migration-config.json` to the package root (the full resolved `MigrationOptions` JSON). `MigrationJob` is stripped to a minimal dispatch token (`jobId`, `mode`, `package`, `configVersion`, `diagnostics`, `resume`). After the agent opens the package store it reads `migration-config.json`, builds a per-job `IConfiguration`, and creates a child `IServiceScope` that overrides all `IOptions<T>` with the correct values. This applies equally to `MigrationAgent` (.NET 10) and `TfsMigrationAgent` (.NET 4.8).

## Technical Context

**Language/Version**: C# 12 / .NET 10 (MigrationAgent, CLI, Infrastructure.Agent); C# 12 / .NET 4.8 (TfsMigrationAgent, Infrastructure.Agent net481 path)
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Options`, `DevOpsMigrationPlatform.Abstractions`, `DevOpsMigrationPlatform.Infrastructure.Agent`
**Storage**: `IArtefactStore` → `FileSystemArtefactStore` / `AzureBlobArtefactStore` (well-known path `migration-config.json` at package root)
**Testing**: MSTest + Reqnroll; `TestCategory("Unit")`, `TestCategory("SystemTest_Simulated")`
**Target Platform**: .NET 10 + .NET 4.8 (net481 compatibility required throughout)
**Project Type**: Cross-cutting infrastructure (no new module; touches Abstractions, Infrastructure.Agent, CLI, MigrationAgent, TfsMigrationAgent)
**Performance Goals**: Config read adds < 5ms per job start (single small JSON file)
**Constraints**: No `.Result`/`.Wait()`; propagate `CancellationToken`; `IArtefactStore` only for file access; net481-compatible serialisation (no `System.Text.Json` source gen in net481 path)
**Scale/Scope**: One `migration-config.json` per package; read once per job start

## Constitution Check

- [x] **Package-First (I):** Config written to package via `IArtefactStore`. No direct source-to-target. ✅
- [x] **Streaming (II):** Not applicable — no module enumeration changes. ✅
- [x] **WorkItems Layout (III):** No WorkItems folder changes. ✅
- [x] **Checkpointing (IV):** Not applicable — config is not a cursor. ✅
- [x] **Module Isolation (V):** All file access via `IArtefactStore`. Interface defined in `Abstractions`. ✅
- [x] **Separation of Planes (VI):** CLI writes config to package (Rule 23 exception approved). ControlPlane does not participate. Job Engine reads config from package. ✅
- [x] **Determinism (VII):** `MigrationJob` schema break requires `configVersion` bump to `"2.0"` + upgrader. ✅ (planned)
- [x] **ATDD-First (VIII):** All 3 user stories have Given/When/Then scenarios. ✅
- [x] **SOLID & DI (IX):** `IPackageConfigStore` in `Abstractions`; `PackageConfigStore` in `Infrastructure.Agent`; `Add*` extension method. ✅
- [x] **Full Connector Coverage (XI):** Cross-cutting infrastructure — no connector-specific logic. Both agent types (`MigrationAgent` + `TfsMigrationAgent`) explicitly covered. ✅

**Complexity Tracking**:

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Rule 23 exception (CLI writes to package) | Config must exist before job is dispatched; agent cannot write before receiving the job | Agent-writes-after-receive would require embedding full config in `MigrationJob` — defeats the purpose |

## Observability Contract

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|---|---|---|---|---|---|
| Write config to package | `PackageConfigStore.WriteAsync` | `config.write` | `migration.config.write.count`, `migration.config.write.errors` | `Information`: "Writing config to package {PackageUri}"; `Error`: "Failed to write config: {ErrorMessage}" | N/A — CLI-side; no ProgressSink |
| Read config from package | `PackageConfigStore.ReadAsync` | `config.read` | `migration.config.read.count`, `migration.config.read.errors`, `migration.config.read.fallbacks` | `Information`: "Reading config from package {PackageUri}"; `Warning`: "migration-config.json not found — job fails"; `Error`: "Failed to parse config: {ErrorMessage}" | N/A — pre-module setup |

### Wiring Checklist

- [x] **O-1 ActivitySource:** `config.write` / `config.read` span names added under `WellKnownActivitySourceNames.Migration`
- [x] **O-2 Metric instruments:** `migration.config.write.count`, `migration.config.write.errors`, `migration.config.read.count`, `migration.config.read.errors`, `migration.config.read.fallbacks` added to `WellKnownMetricNames`
- [x] **O-2 Meter registration:** New instruments on existing `WellKnownMeterNames.Migration` — no new meter registration needed
- [x] **O-3 Log structured params:** All log calls use structured params (`{PackageUri}`, `{ErrorMessage}`)
- [ ] **O-4 IProgressSink wiring:** N/A — `IPackageConfigStore` is infrastructure, not a module; no progress events
- [ ] **O-4 ModuleCounters property:** N/A
- [ ] **O-4 CLI row:** N/A — no progress bar row for config write/read
- [x] **DI wiring verified:** `services.AddSingleton<IPackageConfigStore, PackageConfigStore>()` in `PackageConfigServiceCollectionExtensions` called from both agent host startups

### Tests Required for Observability

- [x] Unit test: verify `ActivitySource.StartActivity("config.write")` is called
- [x] Unit test: verify `ActivitySource.StartActivity("config.read")` is called
- [x] Unit test: verify `IMigrationMetrics` receives `config.write.count` / `config.read.count`
- [x] Unit test: verify `ILogger` receives `Information` on success and `Error` on parse failure
- [x] Simulated system test: run export → assert `migration-config.json` exists in package root + `IOptions<FieldTransformOptions>` has correct values

## Project Structure

### Documentation (this feature)

```text
specs/025-agent-config-package/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── contracts/
│   └── IPackageConfigStore.md
└── tasks.md             ← Phase 2 (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Jobs/
│   │   ├── Job.cs                         ← MODIFY: remove Modules, Policies, ConfigHash
│   │   ├── MigrationJob.cs                ← MODIFY: remove Source, Target
│   │   └── JobPackage.cs                  ← no change (PackageUri stays)
│   ├── Options/
│   │   └── MigrationOptions.cs            ← no change (this is the config file object)
│   ├── Storage/
│   │   └── IPackageConfigStore.cs         ← NEW
│   └── PackagePaths.cs                    ← MODIFY: add MigrationConfigFileName constant
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Storage/
│   │   └── PackageConfigStore.cs          ← NEW
│   ├── PackageConfigServiceCollectionExtensions.cs ← NEW
│   └── ModulePipelineWorkerBase.cs        ← MODIFY: read config + build per-job scope
│
├── DevOpsMigrationPlatform.CLI.Migration/
│   └── Commands/QueueCommand.cs           ← MODIFY: WriteAsync before SubmitJobAsync
│
├── DevOpsMigrationPlatform.MigrationAgent/
│   └── MigrationAgentServiceExtensions.cs ← MODIFY: register IPackageConfigStore
│
└── DevOpsMigrationPlatform.TfsMigrationAgent/
    ├── TfsJobAgentWorker.cs               ← MODIFY: read config before calling base
    └── TfsMigrationAgentServiceExtensions.cs ← MODIFY: register IPackageConfigStore

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
│   └── Storage/PackageConfigStoreTests.cs ← NEW (Unit)
└── DevOpsMigrationPlatform.CLI.Migration.Tests/
    └── Commands/QueueRoundtripSimulatedTests.cs ← MODIFY: assert migration-config.json present

.agents/20-guardrails/
└── architecture-boundaries.md                 ← MODIFY: Rule 23 exception

.agents/30-context/
├── job-lifecycle.md                        ← MODIFY: new minimal schema
└── migration-package-concept.md                      ← MODIFY: add migration-config.json entry

docs/
└── migration-agent.md                     ← MODIFY: add config read step to execution flow
```

**Structure Decision**: Existing modular project layout. `IPackageConfigStore` goes in `Abstractions/Storage/` (consistent with `IArtefactStore`). `PackageConfigStore` in `Infrastructure.Agent` follows the same project as all existing agent-side infrastructure.

## Phase 0: Research

See [research.md](research.md) for findings. Key decisions resolved:

1. **Per-job IOptions<T> override pattern** — build a new `IServiceCollection`, copy root registrations, add per-job `IOptions<T>` bindings from the loaded `IConfiguration`. Build a child `IServiceProvider`. Resolve modules from it. Dispose at job end. This is the standard ASP.NET middleware pattern applied to agent workers.
2. **net481 serialisation** — `Newtonsoft.Json` is already used in the TFS agent path. `System.Text.Json` is used in .NET 10. `IPackageConfigStore` serialises via `System.Text.Json` on .NET 10 and `Newtonsoft.Json` in net481 (conditional compilation in `PackageConfigStore`). The JSON output is identical.
3. **IConfiguration from string** — `Microsoft.Extensions.Configuration.Json` supports `AddJsonStream` on both .NET 10 and .NET 4.8. No new package dependencies.
4. **MigrationJob minimal fields** — `Job` base retains: `JobId`, `ConfigVersion`, `Package`, `Guardrails`, `Diagnostics`, `Resume`. Remove: `Policies`, `Modules`, `ConfigHash`. `MigrationJob` retains `Mode` only; removes `Source`, `Target`. `ConfigVersion` bumped to `"2.0"`.

## Phase 1: Design & Contracts

See [data-model.md](data-model.md) and [contracts/IPackageConfigStore.md](contracts/IPackageConfigStore.md).

### Key Design Decisions

#### D-1: Where does `IPackageConfigStore` live?
`DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageConfigStore.cs` — agent-layer abstraction, net481-compatible (no .NET 10-only APIs). Lives alongside other agent-specific abstractions (e.g. `IArtefactStore` consumers). No circular references. Note: `IArtefactStore` itself lives in `DevOpsMigrationPlatform.Abstractions`; `IPackageConfigStore` is a higher-level composition that depends on it, so it belongs in the `.Agent` abstractions layer.

#### D-2: What does `migration-config.json` contain?
The complete serialised `MigrationOptions` POCO as written by `System.Text.Json`. The root JSON key is `MigrationPlatform` so it is directly compatible with `IConfiguration.AddJsonFile` / `AddJsonStream`. The agent builds an `IConfiguration` from the stream and binds `IOptions<T>` exactly as the CLI would from `migration.json`.

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export",
    "Source": { "Type": "AzureDevOpsServices", "Url": "...", "Authentication": { "AccessToken": "..." } },
    "Target": { ... },
    "Package": { "WorkingDirectory": "..." },
    "Policies": { ... },
    "Modules": { "WorkItems": { "Enabled": true, ... } },
    "Tools": {
      "FieldTransform": { "TransformGroups": [...] },
      "NodeTranslation": { ... },
      "IdentityLookup": { ... }
    }
  }
}
```

#### D-3: Per-job IServiceScope pattern
```csharp
// Inside ModulePipelineWorkerBase.OnMigrationJobAsync, after opening package store:

var packageConfig = await _packageConfigStore.ReadAsync(artefactStore, ct);
// packageConfig is an IConfiguration built from migration-config.json

var jobServices = new ServiceCollection();
// Copy infrastructure singletons from root
jobServices.AddSingleton(artefactStore);
jobServices.AddSingleton(stateStore);
// Bind all IOptions<T> from per-job config
jobServices.AddOptions<FieldTransformOptions>()
    .Bind(packageConfig.GetSection(FieldTransformOptions.SectionName));
jobServices.AddOptions<NodeTranslationOptions>()
    .Bind(packageConfig.GetSection(NodeTranslationOptions.SectionName));
jobServices.AddOptions<IdentityLookupOptions>()
    .Bind(packageConfig.GetSection(IdentityLookupOptions.SectionName));
// Add module services
jobServices.AddFieldTransformToolServices();
jobServices.AddNodeTranslationToolServices();
// Build per-job provider
await using var jobProvider = jobServices.BuildServiceProvider();
// Resolve modules from job scope
var modules = jobProvider.GetServices<IModule>();
```

#### D-4: MigrationJob breaking change — upgrader
`Job.ConfigVersion` field currently defaults to `"1.0"`. After this change the new schema is `"2.0"`. The control plane's EF Core database stores the full `MigrationJob` JSON in a `JobPayload` column. An EF Core migration must transform stored `"1.0"` jobs: extract `Source`/`Target`/`Modules`/`Policies` from the stored JSON and write `migration-config.json` to the package, then strip those fields from the stored JSON and set `ConfigVersion = "2.0"`.

#### D-5: CLI write sequence
```
1. Build outputPath (existing logic — unchanged)
2. Create IArtefactStore for outputPath
3. await _packageConfigStore.WriteAsync(artefactStore, config, ct)  ← NEW
4. Build minimal MigrationJob (no Source/Target/Modules/Policies)
5. await controlPlaneClient.SubmitJobAsync(job, ct)                ← unchanged
```
If step 3 throws, step 5 is never reached — atomicity is maintained.

#### D-6: Rule 23 amendment wording
> Rule 23 (amended): "Only Migration Agent (and TFS Export Agent) may write to the working directory/package during migration execution. The CLI MAY write `migration-config.json` to the package root as a pre-submission step before calling the control plane. This is the only package write permitted from the CLI."

## Post-Phase Constitution Re-Check

All design decisions confirmed consistent with constitution:
- `IPackageConfigStore` in `Abstractions` (Principle V, IX)
- No raw `IConfiguration` in module code — modules still receive `IOptions<T>` (Principle IX)
- CLI write is pre-submission, not migration execution (Principle VI / Rule 23 amendment)
- `migration-config.json` stored via `IArtefactStore` — consistent with Principle I, V
- Breaking `MigrationJob` change has upgrader (Principle VII)
- All agent types covered (Constitution XI)


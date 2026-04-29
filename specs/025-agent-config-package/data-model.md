# Data Model: Fix — Tool Config Never Reaches the Agent

**Phase 1 output** for [plan.md](plan.md)

---

## New Types

### `IPackageConfigStore` (interface — NEW)

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageConfigStore.cs`
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Storage`

```csharp
/// <summary>
/// Reads and writes the per-job migration configuration file (<c>migration-config.json</c>)
/// that the CLI writes to the package root before job submission.
/// The agent reads this file to obtain the full <see cref="MigrationOptions"/> —
/// source, target, credentials, modules, policies, and tools — that is not carried
/// in the minimal <see cref="MigrationJob"/>.
/// </summary>
public interface IPackageConfigStore
{
    /// <summary>
    /// Writes the full <paramref name="options"/> to <c>migration-config.json</c>
    /// at the root of <paramref name="artefactStore"/>.
    /// </summary>
    /// <remarks>
    /// Called by the CLI before job submission. Throws if the file already exists
    /// (FR-012 — must not silently overwrite).
    /// </remarks>
    Task WriteAsync(
        IArtefactStore artefactStore,
        MigrationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>migration-config.json</c> from <paramref name="artefactStore"/> and
    /// returns an <see cref="IConfiguration"/> rooted at the <c>MigrationPlatform</c> section.
    /// </summary>
    /// <remarks>
    /// Called by the agent after opening the package store. Throws
    /// <see cref="PackageConfigNotFoundException"/> if the file is absent (FR-005 — fail fast,
    /// no graceful fallback).
    /// </remarks>
    Task<IConfiguration> ReadAsync(
        IArtefactStore artefactStore,
        CancellationToken cancellationToken = default);
}
```

---

### `PackageConfigNotFoundException` (exception — NEW)

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageConfigNotFoundException.cs`
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Storage`

```csharp
/// <summary>
/// Thrown by <see cref="IPackageConfigStore.ReadAsync"/> when
/// <c>migration-config.json</c> is absent from the package root.
/// The agent must fail the job and instruct the operator to re-submit.
/// </summary>
public sealed class PackageConfigNotFoundException : Exception
{
    public PackageConfigNotFoundException(string packageUri)
        : base($"migration-config.json not found in package '{packageUri}'. Re-submit the job from the CLI to regenerate it.")
    { }
}
```

---

### `PackageConfigStore` (implementation — NEW)

**Location**: `DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs`
**Namespace**: `DevOpsMigrationPlatform.Infrastructure.Agent.Storage`

Implements `IPackageConfigStore`. Uses `IArtefactStore.WriteTextAsync` / `ReadTextAsync` for file access. Conditional compilation:
- `.NET 10`: `System.Text.Json.JsonSerializer` with `MigrationOptionsSerializerContext`.
- `net481`: `Newtonsoft.Json.JsonConvert`.

ActivitySource: `WellKnownActivitySourceNames.Migration`.

---

### `PackagePaths` (constant — MODIFY)

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs`
**Change**: Add `public const string MigrationConfigFileName = "migration-config.json";`

---

## Modified Types

### `Job` (base class — MODIFY)

**Location**: `DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs`

Remove the following properties (moved to `migration-config.json`):
- `ConfigHash` — SHA-256 of the old config JSON; no longer meaningful once config is in the package
- `Policies` (`JobPolicies`) — moved to `migration-config.json` → `MigrationPlatform.Policies`
- `Modules` (`List<JobModule>`) — moved to `migration-config.json` → `MigrationPlatform.Modules`

Retain:
- `JobId`, `ConfigVersion` (→ `"2.0"`), `Package`, `Guardrails`, `Diagnostics`, `Resume`

---

### `MigrationJob` (class — MODIFY)

**Location**: `DevOpsMigrationPlatform.Abstractions/Jobs/MigrationJob.cs`

Remove:
- `Source` (`MigrationEndpointOptions?`) — moved to `migration-config.json`
- `Target` (`MigrationEndpointOptions?`) — moved to `migration-config.json`

Retain:
- `Mode` (string) — used for capability-based agent routing

`GetSourceType()` implementation: read from `migration-config.json` during agent routing is not required at submission time (the CLI knows the type). `GetSourceType()` can return the mode string or be removed from the routing path — exact behaviour TBD in task generation.

---

### `QueueCommand` (class — MODIFY)

**Location**: `DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs`

All four `Execute*Async` methods (`ExecuteExportAsync`, `ExecuteImportAsync`, `ExecutePrepareAsync`, `ExecuteAdoExportAsync`, `ExecuteSimulatedExportAsync`) are modified to:

1. Resolve `outputPath` (existing logic — unchanged)
2. Create a transient `IArtefactStore` for `outputPath`
3. `await _packageConfigStore.WriteAsync(artefactStore, config, ct)` ← NEW
4. Build minimal `MigrationJob` (no `Source`/`Target`/`Modules`/`Policies`)
5. Submit via `controlPlaneClient.SubmitJobAsync(job, ct)` (unchanged)

Inject `IPackageConfigStore` via constructor (registered in `QueueCommand`'s host builder).

---

### `ModulePipelineWorkerBase` (class — MODIFY)

**Location**: `DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs`

`OnMigrationJobAsync` gains a step after opening the package store:

```csharp
var (artefactStore, stateStore) = PackageStoreFactory.Create(job.Package.PackageUri ?? ".");
var packageConfig = await _packageConfigStore.ReadAsync(artefactStore, ct);

// Build per-job service scope
var jobServices = new ServiceCollection();
jobServices.AddSingleton(artefactStore);
jobServices.AddSingleton(stateStore);
// Bind IOptions<T> from packageConfig
jobServices.Configure<FieldTransformOptions>(
    packageConfig.GetSection($"{MigrationOptionsConstants.SectionName}:Tools:FieldTransform"));
jobServices.Configure<NodeTranslationOptions>(
    packageConfig.GetSection($"{MigrationOptionsConstants.SectionName}:Tools:NodeTranslation"));
jobServices.Configure<IdentityLookupOptions>(
    packageConfig.GetSection($"{MigrationOptionsConstants.SectionName}:Tools:IdentityLookup"));
// Re-register modules so they get the per-job IOptions<T>
foreach (var module in MigrationModules)
    jobServices.AddSingleton(typeof(IModule), module.GetType());
// ... (full wiring in implementation task)
await using var jobProvider = jobServices.BuildServiceProvider();
```

---

### `TfsJobAgentWorker` (class — MODIFY)

**Location**: `DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`

`OnMigrationJobAsync` adds config read before delegating to base:

1. Open package store for `job.Package.PackageUri`
2. `await _packageConfigStore.ReadAsync(artefactStore, ct)` ← NEW
3. Extract `Source` from config (replaces `job.Source` — which is now null in v2 jobs)
4. Validate not null + mode is Export (existing logic adapted)
5. Store source in `ActiveTfsJobServices` or pass to `OnBeforeModulesAsync`
6. Delegate to `base.OnMigrationJobAsync(job, controlPlane, leaseId, ct)`

---

## Package File

### `migration-config.json`

**Well-known path**: `migration-config.json` (package root, accessed via `PackagePaths.MigrationConfigFileName`)
**Written by**: CLI (`QueueCommand`) before job submission
**Read by**: MigrationAgent (`ModulePipelineWorkerBase`), TfsMigrationAgent (`TfsJobAgentWorker`)
**Access control**: Package root is owned by the operator. CLI writes, agent reads. No other component accesses this file.

**JSON structure**:
```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export",
    "Source": {
      "Type": "AzureDevOpsServices",
      "Url": "https://dev.azure.com/myorg",
      "Project": "MyProject",
      "Authentication": {
        "Type": "PersonalAccessToken",
        "AccessToken": "****"
      }
    },
    "Target": { ... },
    "Package": {
      "WorkingDirectory": "D:\\exports\\run-001"
    },
    "Policies": {
      "Retries": { "Max": 8 },
      "Throttle": { "MaxConcurrency": 4 },
      "Checkpoints": { "Interval": 300 }
    },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Scope": { "Query": "SELECT ..." },
        "Tools": {
          "FieldTransform": {
            "TransformGroups": [...]
          },
          "NodeTranslation": { ... },
          "IdentityLookup": { ... }
        }
      }
    }
  }
}
```

**Security note**: The file contains credentials. Access is controlled at the filesystem level by the operator. Log statements in `PackageConfigStore` MUST NOT log credential values — they must be redacted or omitted. Log only the package URI and success/failure status.

---

## State Transitions

```
CLI (QueueCommand)
  1. Validates config (Tier 0 + Tier 1)
  2. Resolves outputPath
  3. IPackageConfigStore.WriteAsync → writes migration-config.json  [NEW]
  4. Builds minimal MigrationJob (no Source/Target/Modules/Policies)
  5. Submits MigrationJob to ControlPlane

ControlPlane
  6. Stores MigrationJob in DB (now minimal)
  7. Routes to capable agent by Mode

Agent (ModulePipelineWorkerBase / TfsJobAgentWorker)
  8. Receives MigrationJob lease
  9. Opens IArtefactStore for job.Package.PackageUri
 10. IPackageConfigStore.ReadAsync → loads IConfiguration from migration-config.json  [NEW]
 11. Builds per-job IServiceScope with IOptions<T> populated from IConfiguration        [NEW]
 12. Resolves modules from per-job scope
 13. Executes module pipeline (unchanged)
 14. Disposes per-job IServiceProvider
```

---

## Breaking Changes Summary

| Change | Old behaviour | New behaviour | Migration path |
|---|---|---|---|
| `Job.Policies` removed | In `MigrationJob` JSON | In `migration-config.json` | EF Core upgrader |
| `Job.Modules` removed | In `MigrationJob` JSON | In `migration-config.json` | EF Core upgrader |
| `Job.ConfigHash` removed | SHA-256 in `MigrationJob` JSON | Not stored | EF Core upgrader (drop field) |
| `MigrationJob.Source` removed | In `MigrationJob` JSON | In `migration-config.json` | EF Core upgrader |
| `MigrationJob.Target` removed | In `MigrationJob` JSON | In `migration-config.json` | EF Core upgrader |
| `ConfigVersion` default | `"1.0"` | `"2.0"` | EF Core upgrader |

# Bug: Tool Config Never Reaches the Agent

**Status**: Confirmed pre-existing bug  
**Branch**: 024-teams-module  
**Date**: 2026-04-29  

---

## Problem

`MigrationAgentServiceExtensions` registers tool option bindings at agent startup:

```csharp
builder.Services.AddFieldTransformToolServices();   // binds IOptions<FieldTransformOptions>
builder.Services.AddNodeTranslationToolServices();  // binds IOptions<NodeTranslationOptions>
```

These call `BindConfiguration("MigrationPlatform:Tools:FieldTransform")` etc. against the agent's `IConfiguration`. But the agent's `appsettings.json` contains no `MigrationPlatform` section:

```json
{ "Logging": { ... }, "Telemetry": { ... } }
```

**Result: `FieldTransformOptions`, `NodeTranslationOptions`, `IdentityLookupOptions` are always empty/default on the agent. Any transform rules or node mappings the user configured in `migration.json` are silently ignored at runtime.**

---

## Root Cause

The config file (`migration.json`) is CLI-only. It is read once by the CLI, converted into a `MigrationJob` DTO, serialised to JSON, POSTed to the Control Plane over HTTP, stored, then dequeued by the agent. The `MigrationOptions` C# object is ephemeral — it exists only within the CLI process during job construction.

`MigrationJob` carries a lossy projection of `MigrationOptions` (endpoints, module enable/disable, policies as flat scalars) but does **not** carry the typed tool configuration (transform groups, regex patterns, node mappings).

The broken flow:

```
migration.json:  MigrationPlatform.Tools.FieldTransform.TransformGroups[...]
                                      ↓  MISSING STEP
MigrationJob:   ??? FieldTransform config ???
                                      ↓
Agent:          IOptions<FieldTransformOptions>.Value  ← always empty defaults
```

---

## Proposed Fix: Config Travels in the Package

Rather than expanding `MigrationJob` with every tool's typed options (growing the wire contract indefinitely), the config should travel to the agent **via the package** — the same durable store that holds all other migration artefacts.

```
CLI:   WriteConfigToPackageAsync(artefactStore, migrationOptions)
            ↓  writes migration-config.json to package root (well-known path)
       SubmitJobAsync(job)  ← job only carries: endpoints + package URI + module list
                                      ↓  HTTP (unchanged)
Agent: OnMigrationJobAsync(job, ...)
       ReadConfigFromPackageAsync(artefactStore)  ← same store, same well-known path
            ↓
       per-job IConfiguration built from package file
            ↓
       IOptions<FieldTransformOptions>, IOptions<NodeTranslationOptions>  ← correct user values
```

### Why this is better than embedding typed options in `MigrationJob`

| Concern | Embed in MigrationJob | Config in package |
|---|---|---|
| `MigrationJob` size | Grows with every new tool/module | Stays lean |
| Audit trail | Lost after job dispatch | `migration-config.json` in the package forever |
| Resume / replay | Config in a different place than data | Config and data co-located in the same package |
| Schema evolution | Job wire format breaks | Package file format is independent |
| Correctness guarantee | Config on wire ≠ config that actually ran | Config that ran **is** in the package |

---

## Shared Contract

A single abstraction in `Abstractions` — callable from CLI, ControlPlane (read-only verify), and Agent:

```csharp
/// <summary>
/// Reads and writes the resolved migration config into/from the package store.
/// The well-known path is <c>migration-config.json</c> at the package root.
/// CLI writes; Agent reads; ControlPlane may read for validation.
/// </summary>
public interface IPackageConfigStore
{
    Task WriteAsync(IArtefactStore package, MigrationOptions options, CancellationToken ct);
    Task<IConfiguration> ReadAsync(IArtefactStore package, CancellationToken ct);
}
```

The ControlPlane does not participate in config transport — it never touches the package config, only job metadata.

---

## Per-Job DI Scope — Chicken-and-Egg Constraint

`IOptions<T>` registered as singletons at agent host startup cannot be overridden per-job. Naively, you might want to load the package config before building DI — but this is impossible: the agent needs DI (specifically `IArtefactStore` / `IPackageStoreFactory`) to open the package in the first place.

The correct resolution is a **two-phase startup per job**:

```
Phase 1 — Host DI built (at process/worker startup)
  └─ IPackageStoreFactory registered (root singleton)
  └─ Tool IOptions<T> registered with empty/default bindings — intentionally incomplete

Phase 2 — Per-job scope (inside OnMigrationJobAsync, after dequeue)
  └─ Open package store using root IPackageStoreFactory  ← uses host DI
  └─ Read migration-config.json from package            ← uses IArtefactStore
  └─ Build per-job IConfiguration from package file
  └─ Create child IServiceScope, override IOptions<T>   ← per-job DI scope
       └─ Resolve modules from child scope              ← correct tool config
```

This is the same two-phase pattern used by ASP.NET (host DI built once; request scope built per-request from it). The root container holds infrastructure; the per-job scope holds the job-specific `IConfiguration` and the `IOptions<T>` values derived from it.

`ExportContext` / `ImportContext` already exist as the per-job execution boundary — the per-job `IServiceScope` and the `IConfiguration` loaded from the package should be created at the same point where those contexts are constructed, inside `OnMigrationJobAsync`.

---

## Scope of Change

1. **`IPackageConfigStore`** — new interface in `Abstractions`  
2. **`PackageConfigStore`** — implementation in `Infrastructure` (reads/writes `migration-config.json`)  
3. **CLI `QueueCommand`** — call `WriteAsync` before `SubmitJobAsync`  
4. **Agent `JobAgentWorker`** — call `ReadAsync` after opening the package store; build per-job `IConfiguration`; override tool `IOptions<T>` bindings for the job scope  
5. **Package format** — `migration-config.json` added as a well-known root file (update `.agents/context/package-format.md`)

This fix is a prerequisite for the `IOptions`-per-slice migration described in `analysis/draftspec-schema-from-ioptions-di.md`.

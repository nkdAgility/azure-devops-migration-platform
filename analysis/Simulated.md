# Simulated Infrastructure Design Analysis

## Overview

This document captures the design for a `DevOpsMigrationPlatform.Infrastructure.Simulated` assembly — a fully self-contained simulated source and target provider that requires no Azure DevOps or TFS connectivity. It enables operator validation, long-running tests, multi-agent testing, and any data shape or scenario without real infrastructure.

---

## Current Pattern Summary

Each infrastructure provider delivers concrete implementations behind the Abstractions interfaces, wired together by `*ServiceCollectionExtensions` methods. The host (CLI/agent) picks which extension to call at startup.

> **Note on TFS**: `Infrastructure.TfsObjectModel` is a special case. TFS 2010–2018 requires a proprietary .NET Framework 4.x object-model SDK that cannot use the REST API and cannot target .NET 8+. Where TFS integration can be aligned with the clean connector pattern it should be, but it is not required to do so — its .NET 4 constraints and proprietary SDK make full conformance impractical. TFS is therefore excluded from the connector target model below. All new connectors — Simulated, GitHub, Jira — follow the REST/pure-abstraction pattern established by `Infrastructure.AzureDevOps`, with `Infrastructure.TfsObjectModel` remaining a best-effort parallel that improves over time as constraints allow.

### Connector Target Model

**Connectors in scope**: `AzureDevOps`, `TeamFoundationServer` (best-effort, .NET 4 constraints), `Simulated`. GitHub and Jira are future connectors — the architecture accommodates them but no assemblies or stubs are created for them now.

The shared `Infrastructure` assembly contains no connector knowledge — only orchestration, transforms, and storage.

| Concern | Infrastructure (shared) | Infrastructure.AzureDevOps | Infrastructure.TfsObjectModel (best-effort) | Infrastructure.Simulated |
|---|---|---|---|---|
| Revision source | — | `AzureDevOpsWorkItemRevisionSource` | `TfsWorkItemRevisionSource` | `SimulatedWorkItemRevisionSource` |
| Revision source factory | — | `AzureDevOpsWorkItemRevisionSourceFactory` | `TfsWorkItemRevisionSourceFactory` | `SimulatedWorkItemRevisionSourceFactory` |
| Revision mapper | — | `AzureDevOpsWorkItemRevisionMapper` | `TfsWorkItemRevisionMapper` | (generator — no mapper needed) |
| Project discovery | — | `AzureDevOpsProjectDiscoveryService` | `TfsProjectDiscoveryService` | `SimulatedProjectDiscoveryService` |
| Work item discovery | — | `AzureDevOpsWorkItemDiscoveryService` | `TfsWorkItemDiscoveryService` | `SimulatedWorkItemDiscoveryService` |
| Catalog service | `CatalogService` ← **MOVE here** | `CatalogService` (currently here — wrong boundary) | — | — |
| Link analysis (keyed by type) | — | keyed `"AzureDevOpsServices"` | keyed `"TeamFoundationServer"` (best-effort) | keyed `"Simulated"` |
| Import target | — | `AzureDevOpsWorkItemImportTarget` | — (TFS is source-only) | `SimulatedWorkItemImportTarget` |
| Import target factory (keyed) | — | keyed `"AzureDevOpsServices"` | — | keyed `"Simulated"` |
| Resolution strategy factory (keyed) | — | keyed `"AzureDevOpsServices"` | — | keyed `"Simulated"` → `NullResolutionStrategy` |
| Field transform pipeline | `FieldTransformPipeline` (future) | — | — | — |
| Orchestration | `WorkItemExportOrchestrator` `WorkItemImportOrchestrator` | — | — | — |
| DI wiring | — | `AddAzureDevOpsWorkItemExport()` `AddAzureDevOpsWorkItemImport()` | `AddTfsWorkItemExport()` (best-effort) | `AddSimulatedWorkItemExport()` `AddSimulatedWorkItemImport()` |
 
---

## Option A: Polymorphic Endpoint Config Design

This is the chosen approach. `MigrationEndpointOptions` becomes an abstract base with `[JsonPolymorphic]` discrimination. Connector-specific options live in typed derived classes. Shared code never reads connector-specific fields. Factory interfaces accept the base type.

### Why Option A

- **Option B** (flat base + sub-sections) keeps `MigrationEndpointOptions` aware of every connector — adding `Simulated`, `GitHub`, or `Jira` requires changing `Abstractions`. That is a systematic boundary violation.
- **Option C** (fully separate keyed sections) is clean but forces every consumer of `job.Source` to resolve config via DI rather than reading a model — high friction for little gain.
- **Option A** moves connector knowledge into the connector assemblies where it belongs. The shared `WorkItemsModule` becomes fully connector-agnostic. Adding a new connector requires zero changes to `Abstractions`, `Infrastructure`, or any other connector.

### Config model changes

**In `Abstractions`** — replaces the current `sealed class MigrationEndpointOptions`. No STJ attributes, no knowledge of derived types. The base cannot carry `[JsonDerivedType]` — that would require a compile-time reference to each connector assembly, which in turn references `Abstractions`, creating a circular dependency.

```csharp
// Abstractions — clean, no STJ dependency, no connector knowledge
public abstract class MigrationEndpointOptions
{
    public string Type { get; set; } = string.Empty;
}
```

**In `Infrastructure.AzureDevOps`** — ADO-specific leaf only:
```csharp
public sealed class AzureDevOpsEndpointOptions : MigrationEndpointOptions
{
    public string Url { get; set; } = string.Empty;
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;
    public string Project { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
    public EndpointAuthenticationOptions? Authentication { get; set; }
}
```

**In `Infrastructure.TfsObjectModel`** — TFS-specific leaf (same shape, separate assembly, best-effort):
```csharp
public sealed class TeamFoundationServerEndpointOptions : MigrationEndpointOptions
{
    public string Url { get; set; } = string.Empty;
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;
    public string Project { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
    public EndpointAuthenticationOptions? Authentication { get; set; }
}
```

**In `Infrastructure.Simulated`** — generator config leaf:
```csharp
public sealed class SimulatedEndpointOptions : MigrationEndpointOptions
{
    public SimulatedGeneratorConfig Generator { get; set; } = new();
}

public sealed class SimulatedGeneratorConfig
{
    public List<SimulatedProjectConfig> Projects { get; set; } = new();
}

public sealed class SimulatedProjectConfig
{
    public string Name { get; set; } = string.Empty;
    public List<SimulatedWorkItemTypeConfig> WorkItemTypes { get; set; } = new();
    public string LinkTopology { get; set; } = "Flat";  // Flat | Tree | TreeWithCrossLinks
    public int AttachmentSizeKb { get; set; }
    public bool HasComments { get; set; }
    public bool HasEmbeddedImages { get; set; }
}

public sealed class SimulatedWorkItemTypeConfig
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public int RevisionsPerItem { get; set; } = 1;
}
```

### Why `[JsonDerivedType]` cannot live on the base class

`[JsonDerivedType(typeof(AzureDevOpsEndpointOptions), "AzureDevOpsServices")]` on `MigrationEndpointOptions` would require `Abstractions` to hold a compile-time reference to `Infrastructure.AzureDevOps`. But `Infrastructure.AzureDevOps` references `Abstractions`. Circular dependency — will not compile. The same constraint applies to `OrganisationEntry` and any other polymorphic base.

### How polymorphism is registered instead

Each connector's DI extension method contributes its derived type to a shared `EndpointOptionsTypeRegistry` in `Infrastructure`. The host's `PolymorphicEndpointOptionsConverter` (a `JsonConverter<MigrationEndpointOptions>`) reads the `type` discriminator field first, looks up the registered C# type, and deserialises into it:

```csharp
// In Infrastructure.AzureDevOps — AddAzureDevOpsWorkItemExport():
services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
services.AddOrganisationEntryType("AzureDevOpsServices", typeof(AzureDevOpsOrganisationEntry));

// In Infrastructure.TfsObjectModel — AddTfsWorkItemExport():
services.AddEndpointOptionsType("TeamFoundationServer", typeof(TeamFoundationServerEndpointOptions));
services.AddOrganisationEntryType("TeamFoundationServer", typeof(TeamFoundationServerOrganisationEntry));

// In Infrastructure.Simulated — AddSimulatedWorkItemExport():
services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
services.AddOrganisationEntryType("Simulated", typeof(SimulatedOrganisationEntry));
```

`AddEndpointOptionsType` and `AddOrganisationEntryType` are helper extension methods in `Infrastructure` (shared). They register into `EndpointOptionsTypeRegistry`, a singleton the converter reads at deserialisation time. Zero changes to `Abstractions` when a new connector is added. Zero changes to any other connector assembly.

### Factory interface changes

`IWorkItemRevisionSourceFactory` and `IWorkItemImportTargetFactory` accept the base type. No more decomposed scalars:

```csharp
// IWorkItemRevisionSourceFactory — was: CreateAsync(OrganisationEndpoint, string project, string wiql, CancellationToken)
Task<IWorkItemRevisionSource> CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct);

// IWorkItemImportTargetFactory — was: CreateAsync(string targetType, string orgUrl, string project, string accessToken, CancellationToken)
Task<IWorkItemImportTarget> CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct);
```

Each connector factory casts to its derived type and fails fast if the wrong type arrives:
```csharp
// In AzureDevOpsWorkItemRevisionSourceFactory:
if (endpoint is not AzureDevOpsEndpointOptions ado)
    throw new ArgumentException($"Expected AzureDevOpsEndpointOptions, got {endpoint.GetType().Name}");
```

### `WorkItemsModule` becomes connector-agnostic

```csharp
// Before — ADO-shaped, reads connector-specific fields:
var orgUrl = job.Source?.ResolvedUrl ?? throw ...;
var project = job.Source?.Project ?? throw ...;
var sourceEndpoint = new OrganisationEndpoint { ResolvedUrl = orgUrl, ... };
var source = await _sourceFactory.CreateAsync(sourceEndpoint, project, ext.Query, ct);

// After — connector-agnostic:
var source = await _sourceFactory.CreateAsync(job.Source!, ct);
```

The module no longer constructs `OrganisationEndpoint`. That becomes an internal type inside `Infrastructure.AzureDevOps`.

### `OrganisationEndpoint` moves inward

`OrganisationEndpoint` currently lives in `Abstractions` as a shared model. After this change it is only used inside `Infrastructure.AzureDevOps` as an internal connection context. It moves to that assembly and is no longer part of the shared contract.

### JSON `[JsonPolymorphic]` and `net481`

`[JsonPolymorphic]` and `[JsonDerivedType]` are part of `System.Text.Json` 7+, available via NuGet on `net481`. However, `Abstractions` currently has no STJ dependency and the `net481` build is consumed by the TFS subprocess which **never deserialises JSON** — it only uses the model types at runtime.

Two approaches:
1. Add the STJ NuGet package to `Abstractions` and use the attributes — the attribute is inert on `net481`.
2. Register the polymorphism in `JsonSerializerOptions` in the host (`net10.0` only) and keep `Abstractions` attribute-free.

Approach 2 is preferred: keeps `Abstractions` dependency-free, registers types in the host where STJ is already present:

```csharp
// In host startup (net10.0 only):
services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0,
        new DefaultJsonTypeInfoResolver().WithAddedModifier(typeInfo =>
        {
            if (typeInfo.Type == typeof(MigrationEndpointOptions))
                // register derived types here
        }));
});
```

Or more simply, a static `JsonSerializerOptions` factory registered by each connector's `AddXxx()` extension method.

---

## `OrganisationEntry` and the Organisations Array

The `DiscoveryOptions.Organisations` array uses `OrganisationEntry`, which has the same shape as the current `MigrationEndpointOptions` (`Type`, `Url`, `Project`, `ApiVersion`, `Authentication`, plus `Projects[]` and `Enabled`).

### Can `OrganisationEntry` use the same polymorphic base?

Yes — and it should. The same connectors apply: ADO, TFS, and in future GitHub/Jira. An `OrganisationEntry` is conceptually "an endpoint to discover from", which is structurally the same contract as `MigrationEndpointOptions` with two additions:

- `Projects` — scope filter (empty = all projects)
- `Enabled` — skip flag

**Proposed model** — same pattern as `MigrationEndpointOptions`: attribute-free base in `Abstractions`, derived types in connector assemblies, registered via `AddOrganisationEntryType` in each connector's DI extension:

```csharp
// In Abstractions — no STJ attributes, no connector knowledge
public abstract class OrganisationEntry
{
    public string Type { get; set; } = string.Empty;
    public List<string> Projects { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
```

The `Url`/`Authentication`/`ApiVersion` fields live in the derived leaves in their respective connector assemblies, exactly as with `MigrationEndpointOptions`.

### Should `MigrationEndpointOptions` and `OrganisationEntry` share a common base?

They should **not** be unified into a single type. They represent different roles:

| | `MigrationEndpointOptions` | `OrganisationEntry` |
|---|---|---|
| Used in | `MigrationOptions.Source` / `Target` | `DiscoveryOptions.Organisations[]` |
| Cardinality | Exactly one source, one target | Array of n orgs |
| Extra fields | — | `Projects[]`, `Enabled` |
| Context | Export / Import job | Inventory / Dependency discovery job |

They are structurally similar but semantically distinct. Separate polymorphic hierarchies keeps them independently evolvable. The connector-specific leaves (`AzureDevOpsEndpointOptions` vs `AzureDevOpsOrganisationEntry`) can share a private base or helper if the duplication becomes a problem — but that is an `Infrastructure.AzureDevOps`-internal concern, not a shared-Abstractions concern.

### `ScopedOrganisationEndpoint` (runtime model)

`ScopedOrganisationEndpoint` is the runtime model passed to service factory interfaces (`IInventoryServiceFactory`, `IDependencyDiscoveryServiceFactory`). It currently wraps `OrganisationEndpoint` (ADO-centric) + `Projects[]`.

After Option A, `ScopedOrganisationEndpoint` should wrap the base `OrganisationEntry` (or its post-refactor equivalent) directly — no ADO-specific inner type visible to shared service interfaces. The connector factory unwraps and casts.

---

## Interface Map: Abstractions → Simulated

Every interface in `DevOpsMigrationPlatform.Abstractions` is listed below, grouped by concern. The **Simulated** column shows what the new assembly provides, what already exists, and what can be skipped.

### Export Pipeline (Source Side)

| Interface | Purpose | ADO impl | Simulated impl |
|---|---|---|---|
| `IWorkItemRevisionSource` | Streams `WorkItemRevision` records one at a time | `AzureDevOpsWorkItemRevisionSource` | `SimulatedWorkItemRevisionSource` — generates synthetic revisions from config |
| `IWorkItemRevisionSourceFactory` | Creates a `IWorkItemRevisionSource` per job — accepts `MigrationEndpointOptions` base | `AzureDevOpsWorkItemRevisionSourceFactory` — casts to `AzureDevOpsEndpointOptions` | `SimulatedWorkItemRevisionSourceFactory` — casts to `SimulatedEndpointOptions` |
| `IWorkItemQueryWindowStrategy` | Chunks queries into windows (ADO-WIQL-specific 20k cap) | `WorkItemQueryWindowStrategy` | **Not needed** — generator iterates config directly; internal to factory |
| `IAttachmentBinarySource` | Provides raw binary content of an attachment during export | `AzureDevOpsAttachmentBinarySource` | `SimulatedAttachmentBinarySource` — deterministic bytes by filename+ID |
| `IEmbeddedImageDownloader` | Downloads embedded images referenced in HTML/Markdown fields | `AzureDevOpsEmbeddedImageDownloader` | `SimulatedEmbeddedImageDownloader` — returns 1×1 placeholder PNG |
| `IEmbeddedImageExportService` | Rewrites embedded image URLs in field values | Shared `Infrastructure` | shared |
| `IWorkItemCommentSource` | Streams inline comments for a work item | `AzureDevOpsWorkItemCommentSource` | `SimulatedWorkItemCommentSource` — synthetic comments from config |
| `IWorkItemCommentSourceFactory` | Creates a `IWorkItemCommentSource` per job | `AzureDevOpsWorkItemCommentSourceFactory` | `SimulatedWorkItemCommentSourceFactory` |

### Discovery / Inventory (Source Side)

| Interface | Purpose | ADO impl | Simulated impl |
|---|---|---|---|
| `IProjectDiscoveryService` | Lists all team projects / spaces | `AzureDevOpsProjectDiscoveryService` | `SimulatedProjectDiscoveryService` — returns names from `SimulatedEndpointOptions.Generator.Projects` |
| `IWorkItemDiscoveryService` | Streams work item IDs + revision counts | `AzureDevOpsWorkItemDiscoveryService` | `SimulatedWorkItemDiscoveryService` — deterministic counts from config |
| `ICatalogService` | Composes project + work item discovery | Currently in `AzureDevOps` — **MOVE to shared `Infrastructure`** (only uses abstractions) | No separate impl needed after move |
| `IRepoDiscoveryService` | Lists Git repositories | `AzureDevOpsRepoDiscoveryService` | Not needed |
| `IWorkItemLinkAnalysisService` | Streams cross-project link dependencies (keyed by source type) | keyed `"AzureDevOpsServices"` | keyed `"Simulated"` — resolves current `NotSupportedException` in `DependencyDiscoveryService` |
| `IDependencyDiscoveryService` | Orchestrates link analysis across all orgs | Shared `Infrastructure` | shared |
| `IDependencyDiscoveryServiceFactory` | Creates per-job `IDependencyDiscoveryService` | Shared | shared |
| `IInventoryService` | Runs inventory job | Shared `Infrastructure` | shared |
| `IInventoryServiceFactory` | Creates per-job `IInventoryService` | Shared | shared |

### Import Pipeline (Target Side)

| Interface | Purpose | ADO impl | Simulated impl |
|---|---|---|---|
| `IWorkItemImportTarget` | Accepts created/updated work items on the target | `AzureDevOpsWorkItemImportTarget` | `SimulatedWorkItemImportTarget` ← move from `Infrastructure` |
| `IWorkItemImportTargetFactory` | Creates `IWorkItemImportTarget` per job — accepts `MigrationEndpointOptions` base | keyed `"AzureDevOpsServices"` — casts to `AzureDevOpsEndpointOptions` | keyed `"Simulated"` — casts to `SimulatedEndpointOptions` |
| `IWorkItemResolutionStrategy` | Looks up whether source item already exists on target | `TargetFieldResolutionStrategy`, `TargetHyperlinkResolutionStrategy` | `NullResolutionStrategy` (shared) — always create-new |
| `IWorkItemResolutionStrategyFactory` | Creates the correct strategy per job (keyed by target type) | keyed `"AzureDevOpsServices"` | keyed `"Simulated"` — always `NullResolutionStrategy` |
| `IIdentityMappingService` | Maps source user identities to target identities | `PassThroughIdentityMappingService` (shared) | shared (pass-through correct for simulated) |
| `IIdMapStore` | Persists source→target ID mapping | `SqliteIdMapStore` (shared) | shared |
| `IIdMapStoreFactory` | Creates `IIdMapStore` per job | Shared | shared |

### Orchestration / Checkpointing (Shared)

| Interface | Purpose | Simulated notes |
|---|---|---|
| `IRevisionFolderProcessor` | Processes a single revision folder during import | Shared impl in `Infrastructure`; no simulated variant needed |
| `IRevisionFolderProcessorFactory` | Creates a `IRevisionFolderProcessor` per job | Shared; no simulated variant needed |
| `ICheckpointingService` | Reads/writes cursor-based checkpoint state | Shared; backed by `IStateStore`; no simulated variant needed |
| `ICheckpointingServiceFactory` | Creates `ICheckpointingService` per job | Shared; no simulated variant needed |
| `IPhaseTrackingService` | Records job phase transitions | Shared; no simulated variant needed |
| `IPhaseTrackingServiceFactory` | Creates `IPhaseTrackingService` per job | Shared; no simulated variant needed |
| `IProgressSink` | Receives progress events during a job | Shared; no simulated variant needed |

### Storage (Shared)

| Interface | Purpose | Simulated notes |
|---|---|---|
| `IArtefactStore` | Single permitted file abstraction for all module reads/writes | `FileSystemArtefactStore` (shared); simulated jobs use this unchanged |
| `IStateStore` | Persists checkpoint and phase state | `FileSystemStateStore` / SQLite (shared); no simulated variant needed |
| `IPackageStoreFactory` | Creates `IArtefactStore` + `IStateStore` for a package | Shared; no simulated variant needed |
| `IPackageValidator` | Validates a migration package before use | Shared; no simulated variant needed |

### Infrastructure / Platform (Shared or Not Needed)

| Interface | Purpose | Simulated notes |
|---|---|---|
| `IConfigurationService` | Reads and validates scenario config | Shared; `SimulatedSourceOptions` adds a new config section |
| `IModule` | A pluggable migration module (e.g. WorkItems) | Shared `WorkItemsModule`; no simulated variant — modules are source-agnostic |
| `IDiscoveryModule` | A pluggable discovery module | Shared `DependencyDiscoveryModule`; keyed `"Simulated"` registration handles routing |
| `IJobRunner` | Executes a job (export, import, inventory, discovery) | Shared; no simulated variant needed |
| `IControlPlaneClient` | HTTP client to the control plane REST API | Shared; simulated jobs use the real control plane to queue and report |

---

## New Assembly: `DevOpsMigrationPlatform.Infrastructure.Simulated`

```
Infrastructure.Simulated/
  SimulatedWorkItemRevisionSource.cs         ← IWorkItemRevisionSource
  SimulatedWorkItemRevisionSourceFactory.cs  ← IWorkItemRevisionSourceFactory
  SimulatedProjectDiscoveryService.cs        ← IProjectDiscoveryService
  SimulatedWorkItemDiscoveryService.cs       ← IWorkItemDiscoveryService
  SimulatedCatalogService.cs                 ← ICatalogService
  SimulatedWorkItemLinkAnalysisService.cs    ← IWorkItemLinkAnalysisService (keyed "Simulated")
  SimulatedEmbeddedImageDownloader.cs        ← IEmbeddedImageDownloader (no-op / placeholder)
  SimulatedWorkItemCommentSourceFactory.cs   ← IWorkItemCommentSourceFactory
  SimulatedServiceCollectionExtensions.cs    ← AddSimulatedWorkItemExport() / AddSimulatedWorkItemImport() / AddSimulatedDependencyAnalysis()
```

`SimulatedWorkItemImportTarget` should **move** from `Infrastructure` → `Infrastructure.Simulated`, since it belongs with the other simulated implementations. `NullResolutionStrategy` stays in `Infrastructure` — it is a generic no-op, not simulated-specific.

---

## Simulation Data: Three Layers of Fidelity

### 1. Fixture-driven (already exists)
Unpack a real package zip and replay it. This is what `queue-import-workitems-simulated-fixture.json` already does for import. Good for deterministic replay of known shapes.

### 2. Config-driven generator (primary target)
A `SimulatedSourceOptions` model describes *shape*: how many projects, how many work items, what types, how many revisions, attachment sizes, link topologies (flat / tree / cross-project). `SimulatedWorkItemRevisionSource` reads this config and synthetically generates `WorkItemRevision` records, yielding them lazily without any in-memory buffer. This enables long-running agent tests, multi-agent tests, and arbitrary scale scenarios.

### 3. Script-driven (future)
A Lua/JSON script drives the generator for complex edge-case shapes.

---

## Config-Driven Generator: Config Shape

A new `SimulatedSourceOptions` section under `Source`:

```json
{
  "Source": {
    "Type": "Simulated",
    "Url": "https://simulated.local",
    "Project": "SimulatedProject",
    "Simulated": {
      "Projects": [
        {
          "Name": "ProjectA",
          "WorkItemTypes": [
            { "Type": "User Story", "Count": 500, "RevisionsPerItem": 5 },
            { "Type": "Task",       "Count": 2000, "RevisionsPerItem": 3 }
          ],
          "LinkTopology": "TreeWithCrossLinks",
          "AttachmentSizeKb": 50,
          "HasComments": true,
          "HasEmbeddedImages": true
        }
      ]
    }
  }
}
```

`SimulatedWorkItemRevisionSource.GetRevisionsAsync()` iterates these specs and `yield return`s synthetic `WorkItemRevision` records one at a time — streaming, never buffered.  
`SimulatedCatalogService.GetProjectsAsync()` returns the configured project names.  
`SimulatedWorkItemDiscoveryService` returns deterministic counts from the same config.

---

## Keyed DI Registration for Link Analysis

`DependencyDiscoveryService` already expects `"Simulated"` as a key (and currently throws `NotSupportedException` with "Simulated source not yet implemented — add in Phase 4"). `AddSimulatedDependencyAnalysis()` resolves this by registering:

```csharp
services.AddKeyedSingleton<IWorkItemLinkAnalysisService,
    SimulatedWorkItemLinkAnalysisService>("Simulated");
```

---

## What Belongs in `Infrastructure` (shared)

The rule: if a class only depends on Abstractions interfaces and has no connector SDK imports, it belongs here.

### Already Correctly Placed

| Class | Notes |
|---|---|
| `WorkItemExportOrchestrator` | Pure abstractions. Correct. |
| `WorkItemImportOrchestrator` | Pure abstractions. Correct. |
| `RevisionFolderProcessor` | Pure abstractions. Correct. |
| `WorkItemsModule` | Pure abstractions. Correct. |
| `InventoryService` | Pure abstractions. Correct. |
| `DependencyDiscoveryService` | Pure abstractions. Correct. |
| `NullResolutionStrategy` | Generic no-op; not simulated-specific. Correct. |
| `PassThroughIdentityMappingService` | Generic pass-through. Correct. |
| `SqliteIdMapStore` | No connector dependency. Correct. |
| `FileSystemArtefactStore` | Generic file abstraction. Correct. |

### Must Move to Correct Assembly

| Class | Current | Correct | Why |
|---|---|---|---|
| `SimulatedWorkItemImportTarget` | `Infrastructure` | `Infrastructure.Simulated` | Simulated connector code, not shared orchestration |
| `CatalogService` | `Infrastructure.AzureDevOps` | `Infrastructure` | Only uses `IWorkItemDiscoveryService` + `IProjectDiscoveryService` — pure abstractions, zero ADO SDK dependency |

### Must Be Added (Future)

| Class | Location | Notes |
|---|---|---|
| `FieldTransformPipeline` | `Infrastructure` | Config-driven field value transforms (FieldMap, AreaPathMap, IterationPathMap, StateMap, TagMap, WorkItemTypeMap). Pure data operations — no connector dependency. `RevisionFolderProcessor` calls `Apply()` before writing to the import target. All connectors get field mapping for free. |

---

## Boundary Violations to Fix (Current Leaks)

Two concrete violations exist today and must be fixed when `Infrastructure.Simulated` is created.

### Leak 1 — ADO import factory routes `"Simulated"` internally

`AzureDevOpsWorkItemImportTargetFactory` contains:
```csharp
if (string.Equals(targetType, "Simulated", StringComparison.OrdinalIgnoreCase))
    return new SimulatedWorkItemImportTarget();
```
The ADO assembly knows about the Simulated connector. **Fix**: switch to keyed DI. Each connector assembly registers its factory under its own key. `WorkItemsModule` resolves via `GetRequiredKeyedService<IWorkItemImportTargetFactory>(targetType)`.

### Leak 2 — ADO resolution strategy factory type-checks `SimulatedWorkItemImportTarget`

`AzureDevOpsResolutionStrategyFactory` contains:
```csharp
if (target is SimulatedWorkItemImportTarget)
    return new NullResolutionStrategy();
```
A direct cross-assembly type reference. **Fix**: register `SimulatedResolutionStrategyFactory` (keyed `"Simulated"`) in the Simulated assembly, returning `NullResolutionStrategy`. The ADO factory never sees the Simulated type.

---

## Target Assembly Architecture

```
Abstractions                       ← Interfaces, models, options only. No impl.

Infrastructure                     ← Connector-agnostic orchestration + transforms
  WorkItemExportOrchestrator
  WorkItemImportOrchestrator
  RevisionFolderProcessor
  WorkItemsModule
  CatalogService                  ← MOVE from AzureDevOps
  InventoryService
  DependencyDiscoveryService
  NullResolutionStrategy
  PassThroughIdentityMappingService
  SqliteIdMapStore
  FileSystemArtefactStore / StateStore
  EndpointOptionsTypeRegistry     ← Polymorphic type map populated by each connector at DI startup
  PolymorphicEndpointOptionsConverter  ← JsonConverter<MigrationEndpointOptions> reads registry
  PolymorphicOrganisationEntryConverter ← JsonConverter<OrganisationEntry> reads registry
  FieldTransformPipeline          ← FUTURE

Infrastructure.AzureDevOps         ← Everything that touches the ADO REST SDK
  AzureDevOpsEndpointOptions      ← registered as "AzureDevOpsServices"
  AzureDevOpsOrganisationEntry    ← registered as "AzureDevOpsServices"
  AzureDevOpsClientFactory
  AzureDevOpsWorkItemRevisionSource + Factory
  AzureDevOpsWorkItemRevisionMapper
  AzureDevOpsWorkItemDiscoveryService
  AzureDevOpsProjectDiscoveryService
  AzureDevOpsDependencyAnalysisService    (keyed "AzureDevOpsServices")
  AzureDevOpsAttachmentBinarySource
  AzureDevOpsEmbeddedImageDownloader
  AzureDevOpsWorkItemCommentSource + Factory
  AzureDevOpsWorkItemImportTarget
  AzureDevOpsWorkItemImportTargetFactory  (keyed "AzureDevOpsServices")
  AzureDevOpsResolutionStrategyFactory    (keyed "AzureDevOpsServices")
  WorkItemQueryWindowStrategy             (ADO-WIQL-specific; not a shared abstraction)

Infrastructure.TfsObjectModel      ← .NET 4.x TFS OM SDK (best-effort, net481 only)
  TeamFoundationServerEndpointOptions  ← registered as "TeamFoundationServer"
  TeamFoundationServerOrganisationEntry ← registered as "TeamFoundationServer"
  TfsWorkItemRevisionSource + Factory
  TfsProjectDiscoveryService
  TfsWorkItemDiscoveryService

Infrastructure.Simulated           ← Synthetic data generation; no network I/O
  SimulatedEndpointOptions        ← registered as "Simulated"
  SimulatedOrganisationEntry      ← registered as "Simulated"
  SimulatedWorkItemRevisionSource + Factory
  SimulatedWorkItemDiscoveryService
  SimulatedProjectDiscoveryService
  SimulatedWorkItemLinkAnalysisService    (keyed "Simulated")
  SimulatedAttachmentBinarySource
  SimulatedEmbeddedImageDownloader
  SimulatedWorkItemCommentSource + Factory
  SimulatedWorkItemImportTarget           ← MOVE from Infrastructure
  SimulatedWorkItemImportTargetFactory    (keyed "Simulated")
  SimulatedResolutionStrategyFactory      (keyed "Simulated" → NullResolutionStrategy)
  SimulatedServiceCollectionExtensions

```

> **Not created now**: GitHub and Jira assemblies are intentionally absent. The architecture accommodates them — adding one requires only the steps in the checklist below, with zero changes to `Infrastructure`, `Abstractions`, or any other connector. No stubs, placeholder projects, or `NotImplementedException` methods are created for future connectors.

### The Key Invariant

> `IWorkItemQueryWindowStrategy` does **not** belong in `Infrastructure`. It is an ADO-WIQL implementation detail — the 20k item cap is specific to WIQL. Other connectors hide their enumeration strategy inside `GetRevisionsAsync()`. The shared contract is `IWorkItemRevisionSource` — not how a connector discovers items internally.

### Adding a New Connector: Checklist

1. Create `Infrastructure.Xyz` assembly.
2. Add `XyzEndpointOptions : MigrationEndpointOptions` and `XyzOrganisationEntry : OrganisationEntry` in the connector assembly.
3. Call `services.AddEndpointOptionsType("Xyz", typeof(XyzEndpointOptions))` and `services.AddOrganisationEntryType("Xyz", typeof(XyzOrganisationEntry))` in the connector's `AddXxx()` extension — no changes to `Abstractions` or any other assembly.
4. Implement: `XyzWorkItemRevisionSourceFactory` (casts to `XyzEndpointOptions`), `XyzProjectDiscoveryService`, `XyzWorkItemDiscoveryService`, `XyzWorkItemImportTargetFactory` (keyed `"Xyz"`), `XyzResolutionStrategyFactory` (keyed `"Xyz"`).
5. Optionally implement: `XyzAttachmentBinarySource`, `XyzEmbeddedImageDownloader`, `XyzWorkItemCommentSourceFactory`, `XyzWorkItemLinkAnalysisService` (keyed `"Xyz"`).
6. Add `AddXyzWorkItemExport()` / `AddXyzWorkItemImport()` extension methods.
7. Add a `scenarios/queue-export-workitems-xyz-*.json` scenario config and a `.vscode/launch.json` debug profile.
8. **Zero changes** to `Infrastructure`, `Abstractions`, or any other connector assembly.

---

## Lock-Step Development Principle

> **Rule**: Every feature added to the real pipeline that introduces a new interface, extension type, or data-transformation service MUST have a corresponding Simulated implementation shipped in the same work unit. A feature is not complete until the Simulated layer supports it.

This ensures:
- The Simulated mode remains usable end-to-end at every point in the codebase history.
- Operators can validate any new capability before connecting real infrastructure.
- System tests remain runnable without credentials after every merge.

The Simulated implementations are not stubs — they must exercise the same code paths and produce deterministic, observable output.

---

## Extension Registry: Current + Planned

The `WorkItemsModuleExtensions` class parses the `Extensions` array in the job contract. Each extension type is listed below with its current Simulated status and what the simulated counterpart must do.

### Currently Implemented Extensions

| Extension Type | Controls | ADO behaviour | Simulated behaviour | Status |
|---|---|---|---|---|
| `Revisions` | Whether full revision history is exported/imported | Streams all revisions per work item | Generator produces `RevisionsPerItem` synthetic revisions | **Needs implementation** |
| `Links` | Whether related/external/hyperlinks are exported/imported | Fetches and writes `WorkItemLink` records | Generator produces synthetic link graph per `LinkTopology` config | **Needs implementation** |
| `Attachments` | Whether attachment binaries are downloaded/uploaded | Downloads via `IAttachmentBinarySource`; uploads via `IWorkItemImportTarget` | `SimulatedAttachmentBinarySource` returns deterministic bytes; target accepts and no-ops | **Partial** — target exists; binary source needed |
| `Comments` | Whether inline comments are fetched (export) / written (import) | `AzureDevOpsWorkItemCommentSource` streams comments | `SimulatedWorkItemCommentSource` yields synthetic comments driven by config | **Needs implementation** |
| `EmbeddedImages` | Whether embedded images in HTML fields are rewritten | Downloads real images; rewrites URLs in field values | `SimulatedEmbeddedImageDownloader` returns 1×1 placeholder PNG | **Needs implementation** |
| `WorkItemResolutionStrategy` | How source→target ID mappings are seeded at import start | `TargetField` or `TargetHyperlink` — queries live target | `NullResolutionStrategy` — always create-new (correct for simulated) | **Already works** |

### Planned Extensions (Not Yet Implemented)

These do not exist in code yet. When each is implemented in the real pipeline, the Simulated assembly must receive a parallel implementation in the same PR.

| Extension Type | Purpose | Simulated behaviour |
|---|---|---|
| `FieldMap` | Renames or transforms field values on import (e.g. rename `Custom.OldField` → `Custom.NewField`, apply regex substitution) | `SimulatedFieldMapper` — applies the same mapping rules to synthetic field values; verifies the mapped output is present in the import target call |
| `AreaPathMap` | Rewrites `System.AreaPath` values from source structure to target structure | `SimulatedAreaPathMapper` — maps synthetic area paths according to the configured rules; asserts remapped paths appear in `CreateWorkItemAsync` / `UpdateFieldsAsync` calls |
| `IterationPathMap` | Rewrites `System.IterationPath` values from source sprint structure to target | `SimulatedIterationPathMapper` — same pattern as area path; verifies sprint identifiers are rewritten correctly |
| `WorkItemTypeMap` | Renames work item types (e.g. `Bug` → `Defect`, `User Story` → `Feature`) | `SimulatedWorkItemTypeMapper` — generator produces items of source types; verifies type remapping in `CreateWorkItemAsync` |
| `StateMap` | Rewrites `System.State` values when source and target processes differ | `SimulatedStateMapper` — generator produces items with source states; verifies remapped states appear in field writes |
| `TagMap` | Adds, removes, or transforms `System.Tags` values | `SimulatedTagMapper` — verifies tag mutations are applied to synthetic revisions |
| `BatchLoader` / `BatchImport` | Groups revision writes into batches for throughput (if/when added) | Simulated target must handle batched calls without any buffering into memory; batch boundaries must be observable in progress output |
| *(Future link transforms)* | Cross-project link rewriting when source and target org differ | `SimulatedLinkRewriter` — rewrites synthetic link targets according to configured org/project mapping; verifies rewritten links in `AddLinksAsync` calls |

### How to Add a New Extension: Checklist

When a new extension type is added to `WorkItemsModuleExtensions.FromModule()`:

1. Add the `case "ExtensionType":` parse branch in `WorkItemsModuleExtensions`.
2. Add the corresponding options record (e.g. `FieldMapExtensionOptions`).
3. Implement the real service (e.g. `AzureDevOpsFieldMapper`) in `Infrastructure.AzureDevOps` or `Infrastructure` as appropriate.
4. **In the same PR**: implement `SimulatedFieldMapper` (or equivalent) in `Infrastructure.Simulated`.
5. Update the scenario config shape in `SimulatedSourceOptions` if the simulated generator needs to produce data that exercises the new extension.
6. Add or update a scenario config under `scenarios/` that exercises the extension in simulated mode.
7. Update the Planned Extensions table above to move the entry to Currently Implemented.

---

## Summary of Work

| What | Where |
|---|---|
| New project `Infrastructure.Simulated` | New assembly |
| Move `SimulatedWorkItemImportTarget` | From `Infrastructure` → `Infrastructure.Simulated` |
| `SimulatedWorkItemRevisionSource` + factory | Lazy generator driven by `SimulatedSourceOptions` config |
| `SimulatedProjectDiscoveryService` / `SimulatedCatalogService` | Return config-defined project list |
| `SimulatedWorkItemDiscoveryService` | Return deterministic counts from config |
| `SimulatedWorkItemLinkAnalysisService` (keyed `"Simulated"`) | Resolves `DependencyDiscoveryService` `NotSupportedException` |
| `SimulatedServiceCollectionExtensions` | `AddSimulatedWorkItemExport()` / `AddSimulatedWorkItemImport()` / `AddSimulatedDependencyAnalysis()` |
| `SimulatedSourceOptions` config model | New options class, bound from `Source.Simulated` config section |
| New scenario configs | e.g. `scenarios/queue-export-workitems-simulated-source.json` |

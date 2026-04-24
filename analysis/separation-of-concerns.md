# Separation of Concerns — Abstractions & Project Boundary Specification

## Governing principle

The system enforces a strict three-channel communication topology:

```
CLI  ←HTTP→  ControlPlane  ←HTTP→  Agent
```

Each component runs in its own process. No component may compile against another
component's internal contracts. The compiler enforces this via project reference
topology — boundary violations are build errors, not code review findings.

---

## Current state — what is wrong

### `DevOpsMigrationPlatform.Abstractions` is a flat technical bucket

| Folder | Problem |
|--------|---------|
| `Models/` (70+ files) | CLI contracts, Agent domain objects, and CP HTTP payloads all mixed together |
| `Services/` (40+ files) | CLI config interfaces next to Agent execution interfaces |
| `Telemetry/` (18 files) | Cross-cutting constants mixed with Agent/CP-specific OTel metric interfaces |
| `Checkpointing/` (3 files) | Agent-only — only the Agent writes cursors |
| `Storage/` (2 files) | Agent-only — `IArtefactStore`/`IStateStore` have a data residency restriction |
| `Utilities/` (2 files) | Generic bucket name — screaming architecture violation |

**Impact:** `IArtefactStore` and `IControlPlaneClient` are in the same assembly. A CLI
command can accidentally inject `IArtefactStore` and the compiler will not object.

### `CLI.Migration` references five projects it should never touch

```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.ControlPlane\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.MigrationAgent\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.Simulated\..." />
```

**Root cause:** `LocalStackHost` has an in-process fallback that hosts the ControlPlane
and MigrationAgent inside the CLI process when published binaries are not found. This
makes the CLI the composition root for both CP and Agent — pulling in every Agent-internal
contract, every Infrastructure implementation, and every backend-specific package.

The correct pattern already exists: `ChildProcessHost` launches CP and Agent as separate
OS processes (identical to how `ExternalToolRunner` launches `CLI.TfsMigration` for TFS
export). The in-process fallback is unnecessary and must be deleted.

---

## Target state

### `CLI.Migration.csproj` — single reference

```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\DevOpsMigrationPlatform.Abstractions.csproj" />
```

The CLI compiles against `Abstractions` only. It launches ControlPlane and Agent as child
processes via `ChildProcessHost`. All communication with CP is via HTTP using the contracts
in `Abstractions/ControlPlane/`.

### Project reference topology — enforced by compiler

**Abstractions layer** (contract assemblies only — no implementation code):

| Project | Abstractions | Abstractions.ControlPlane | Abstractions.Agent |
|---------|:---:|:---:|:---:|
| `CLI.Migration` | ✅ | ❌ | ❌ |
| `TUI` | ✅ | ❌ | ❌ |
| `ControlPlane` | ✅ | ✅ | ❌ |
| `ControlPlaneHost` | ✅ | ✅ | ❌ |
| `MigrationAgent` | ✅ | ❌ | ✅ |
| `CLI.TfsMigration` (.NET 4.8) | ✅ | ❌ | ✅ |
| `Infrastructure` | ✅ | ❌ | ❌ |
| `Infrastructure.ControlPlane` | ✅ | ✅ | ❌ |
| `Infrastructure.Agent` | ✅ | ❌ | ✅ |
| `Infrastructure.AzureDevOps` | ✅ | ❌ | ✅ |
| `Infrastructure.Simulated` | ✅ | ❌ | ✅ |
| `Infrastructure.TfsObjectModel` | ✅ | ❌ | ✅ |
| `ServiceDefaults` | ✅ | ❌ | ❌ |
| `AppHost` | — | — | — |

**Implementation layer** (composition roots wire concrete types):

| Project | Infrastructure | Infra.ControlPlane | Infra.Agent | Infra.AzureDevOps | Infra.Simulated | Infra.TfsObjectModel |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|
| `CLI.Migration` | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `TUI` | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `ControlPlane` | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `ControlPlaneHost` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `MigrationAgent` | ✅ | ❌ | ✅ | ✅ | ✅ | ❌ |
| `CLI.TfsMigration` | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| `Infrastructure.ControlPlane` | ✅ | — | ❌ | ❌ | ❌ | ❌ |
| `Infrastructure.Agent` | ✅ | ❌ | — | ❌ | ❌ | ❌ |
| `Infrastructure.AzureDevOps` | ✅ | ❌ | ✅ | — | ❌ | ❌ |
| `Infrastructure.Simulated` | ✅ | ❌ | ✅ | ❌ | — | ❌ |
| `Infrastructure.TfsObjectModel` | ✅ | ❌ | ✅ | ❌ | ❌ | — |

The CLI references base `Infrastructure` for config binding and serialization only —
no Agent or CP implementations are reachable.

`ControlPlaneHost` can see `Infrastructure.ControlPlane` (CP metric stores, lifecycle
metrics) but cannot see `Infrastructure.Agent` (artefact store, export orchestrators,
progress sinks). A developer cannot accidentally `new FileSystemArtefactStore()` in the
CP composition root — the compiler will reject it.

`AppHost` references `ControlPlaneHost` and `MigrationAgent` as Aspire project resources
only — no compile-time type dependency.

Every component talks to the ControlPlane exclusively via HTTP contracts in base
`Abstractions`. Only `ControlPlane`/`ControlPlaneHost` references `Abstractions.ControlPlane`.
The CP-internal store and OTel interfaces are invisible to everything outside the CP process.

**Test projects** mirror the topology of their system-under-test:

| Test project | References (beyond test frameworks) |
|---|---|
| `Infrastructure.Tests` | `Abstractions`, `Abstractions.Agent`, `Infrastructure`, `Infrastructure.AzureDevOps`, `Infrastructure.Simulated` |
| `Infrastructure.Simulated.Tests` | `Abstractions`, `Abstractions.Agent`, `Infrastructure`, `Infrastructure.Simulated` |
| `CLI.Migration.Tests` | `Abstractions`, `CLI.Migration` |
| `ControlPlane.Tests` | `Abstractions`, `Abstractions.ControlPlane`, `ControlPlane` |

Test projects MUST NOT reference across component boundaries:
- `Infrastructure.Tests` must NOT reference `CLI.Migration` (current violation — remove)
- `CLI.Migration.Tests` must NOT reference `Infrastructure` or `Infrastructure.AzureDevOps` (current violation — remove)

---

## `DevOpsMigrationPlatform.Abstractions` — truly cross-cutting

Shared types that all three components (CLI, ControlPlane, Agent) legitimately depend on.
Every folder name is a business noun or a platform capability — no technical buckets.

### `Jobs/`
The central unit of work in the system. A job is queued by the CLI, scheduled by the
ControlPlane, and executed by the Agent.
```
Job.cs
MigrationJob.cs
DiscoveryJob.cs
DiscoveryJobType.cs
JobModule.cs
JobModuleScope.cs
JobPackage.cs
JobResume.cs
JobGuardrails.cs
JobPolicies.cs
ResumeDecision.cs
ResumeDecisionStatus.cs
ResumeRejectedException.cs
MigrationErrorCategory.cs         ← moved from Errors/
MigrationException.cs             ← moved from Errors/
```

### `Streaming/`
Real-time event payloads that flow between all three components via SSE.
```
ProgressEvent.cs                  ← flows Agent → CP → CLI
DiagnosticLogRecord.cs            ← flows Agent → CP → CLI
```

### `Organisations/`
The organisational topology: endpoints, authentication, and project scoping.
```
OrganisationEndpoint.cs
OrganisationEndpointAuthentication.cs
ScopedOrganisationEndpoint.cs
```

### `Configuration/`
CLI-facing service for loading and validating config files on disk before job submission.
```
IConfigurationService.cs
IPreFlightValidator.cs            ← Tier 0/1 validation before job submission
```

### `ControlPlane/`
HTTP contracts and payload types for communicating with the ControlPlane.
All components that talk to CP compile against these — no separate assembly needed.
```
IControlPlaneClient.cs            ← CLI reads jobs, streams logs, gets telemetry via HTTP
IJobRunner.cs                     ← CLI submits jobs to CP via HTTP
IControlPlaneTelemetryClient.cs   ← Agent pushes metrics + snapshots to CP via HTTP
JobSummary.cs                     ← returned by GET /jobs
JobMetrics.cs                     ← pushed by Agent, read by CLI via CP
JobSnapshot.cs                    ← pushed by Agent, read by CLI via CP
JobBootstrap.cs                   ← returned by GET /jobs/{id}/bootstrap
JobScopeCounters.cs               ← embedded in JobMetrics
JobDiagnostics.cs
MigrationCounters.cs              ← embedded in JobMetrics
MigrationDiagnostics.cs
OrgSnapshot.cs                    ← embedded in JobSnapshot
ProjectSnapshot.cs                ← embedded in JobSnapshot
ProjectStatus.cs
TargetStatus.cs
DiscoveryCounters.cs              ← embedded in JobMetrics.Discovery and ProjectSnapshot.Discovery
InventoryCounters.cs              ← embedded in DiscoveryCounters
DependencyCounters.cs             ← embedded in DiscoveryCounters
```

### `Options/`
Config schema — cross-cutting. This is a .NET framework idiom (`IOptions<T>`) that is
well-understood in the ecosystem. Includes backend-specific endpoint option types
(currently in `Infrastructure.AzureDevOps.Options` and `Infrastructure.Simulated.Options`)
because they are config schema, not execution contracts.
```
AuthenticationType.cs
AzureDevOpsEndpointOptions.cs     ← moved from Infrastructure.AzureDevOps.Options
AzureDevOpsOrganisationEntry.cs   ← moved from Infrastructure.AzureDevOps.Options
SimulatedEndpointOptions.cs       ← moved from Infrastructure.Simulated.Options
SimulatedOrganisationEntry.cs     ← moved from Infrastructure.Simulated.Options
SimulatedProjectConfig.cs         ← moved from Infrastructure.Simulated.Options
SimulatedWorkItemTypeConfig.cs    ← moved from Infrastructure.Simulated.Options
SimulatedGeneratorConfig.cs       ← moved from Infrastructure.Simulated.Options
CommentsExtensionOptionsConfig.cs ← moved from Modules/
DiscoveryOptions.cs
EmbeddedImagesExtensionOptionsConfig.cs ← moved from Modules/
EnabledExtensionOptions.cs
EndpointAuthenticationOptions.cs
FilterMode.cs
FilterOperator.cs                 ← moved from Domain/ (config filter concern)
IModuleOptions.cs
MigrationCheckpointsOptions.cs
MigrationEndpointOptions.cs
MigrationModulesOptions.cs
MigrationOptions.cs
MigrationOptionsScope.cs
MigrationPackageOptions.cs
MigrationPlatformRoot.cs
MigrationPoliciesOptions.cs
MigrationRetriesOptions.cs
MigrationThrottleOptions.cs
OrganisationEntry.cs
WorkItemFilterOptions.cs
WorkItemResolutionStrategyOptionsConfig.cs
WorkItemsExtensionsOptions.cs
WorkItemsModuleOptions.cs
WorkItemsScopeOptions.cs
ConfigTokenResolver.cs            ← moved from Utilities/; renamed from TokenResolver
```

### `Serialization/`
Polymorphic JSON converters needed by anyone who deserialises config files or job payloads.
Currently in `Infrastructure.Serialization` — must move here so CLI, CP, and Agent all
have access without referencing `Infrastructure`.
```
PolymorphicEndpointOptionsConverter.cs
MigrationPlatformJsonContext.cs   ← (or equivalent serializer setup)
```

### `Validation/`
```
ValidationContext.cs              ← moved from Domain/
ValidationError.cs
ValidationResult.cs
```

### `Telemetry/`
Constants and classification only — **no interfaces**.
```
DataClassification.cs
DataClassificationScope.cs
MigrationTagList.cs
TelemetryOptions.cs
WellKnownActivitySourceNames.cs
WellKnownDiscoveryMetricNames.cs
WellKnownJobMetricNames.cs
WellKnownMeterNames.cs
WellKnownMetricNames.cs
WellKnownServiceNames.cs
```

### `Diagnostics/`
```
DiagnosticLogOptions.cs
```

### `Polyfills/`
```
IsExternalInit.cs
```

---

## `DevOpsMigrationPlatform.Abstractions.ControlPlane` — CP-internal only

Contracts that only the ControlPlane itself implements internally. No other component
references this project.

### `Metrics/`
```
IJobLifecycleMetrics.cs           ← OTel instruments emitted inside the CP process
IJobMetricsStore.cs               ← CP persists incoming agent metrics
IJobSnapshotStore.cs              ← CP persists incoming agent snapshots
```

---

## `DevOpsMigrationPlatform.Abstractions.Agent` — Agent-internal only

Contracts used exclusively inside the Migration Agent and `CLI.TfsMigration`
(the TFS Export Agent running inline).

### `WorkItems/`
```
WorkItemRevision.cs
WorkItemField.cs
WorkItemComment.cs
WorkItemIdentityRef.cs
WorkItemRelations.cs
WorkItemCounters.cs
WorkItemFetchScope.cs
WorkItemFieldFilterEvaluator.cs
WorkItemFieldFilterOptions.cs
WorkItemQueryChunk.cs
WorkItemQueryCountChunk.cs
ExternalWorkItemLink.cs
HyperlinkWorkItemLink.cs
RelatedWorkItemLink.cs
LinkScope.cs
FetchedWorkItem.cs
IdMapEntry.cs
```

### `Discovery/`
Inventory, dependency analysis, and project/repo discovery — the Agent's first phase.
```
DiscoveryContext.cs
ICatalogService.cs
IInventoryService.cs
IInventoryServiceFactory.cs
IProjectDiscoveryService.cs
IRepoDiscoveryService.cs
IWorkItemDiscoveryService.cs
IDependencyDiscoveryService.cs
IDependencyDiscoveryServiceFactory.cs
InventoryReport.cs
InventorySummary.cs
InventoryProgressEvent.cs
ProjectDiscoverySummary.cs
DependencyRecord.cs
DependencySummary.cs
DependencyProgressEvent.cs
```

### `Export/`
Work item revision extraction — reading from source, writing to package.
```
ExportContext.cs
BatchContinuationToken.cs
RevisionProcessResult.cs
IWorkItemRevisionSource.cs
IWorkItemRevisionSourceFactory.cs
IRevisionFolderProcessor.cs
IRevisionFolderProcessorFactory.cs
IWorkItemFetchService.cs
IWorkItemCommentSource.cs
IWorkItemCommentSourceFactory.cs
IWorkItemQueryWindowStrategy.cs
WorkItemQueryWindow.cs
IQueryFingerprintService.cs
```

### `Import/`
Work item creation in the target system.
```
ImportContext.cs
ImportedWorkItemResult.cs
IWorkItemImportTarget.cs
IWorkItemImportTargetFactory.cs
IWorkItemResolutionStrategy.cs
IWorkItemResolutionStrategyFactory.cs
IWorkItemLinkAnalysisService.cs
```

### `Attachments/`
Binary content — downloads from source, streams to package.
```
AttachmentMetadata.cs
AttachmentCounters.cs
AttachmentDownloadResult.cs
AttachmentMapEntry.cs
EmbeddedImageMetadata.cs
EmbeddedImageDownloadResult.cs
IAttachmentBinarySource.cs
IEmbeddedImageDownloader.cs
IEmbeddedImageExportService.cs
```

### `Identity/`
Mapping source identities to target identities.
```
IIdentityMappingService.cs
```

### `Storage/`
Package persistence — artefact store, state store, and package-level services.
```
IArtefactStore.cs
IStateStore.cs
IPackageLockService.cs
IPackageStoreFactory.cs
IPackageValidator.cs
IIdMapStore.cs
IIdMapStoreFactory.cs
PackageLockConflictException.cs   ← moved from Abstractions/Errors/
```

### `Checkpointing/`
Cursor-based resume and phase tracking.
```
CursorEntry.cs
CursorStage.cs
JobPhaseRecord.cs
ICheckpointingService.cs
ICheckpointingServiceFactory.cs
IPhaseTrackingService.cs
IPhaseTrackingServiceFactory.cs
```

### `Lease/`
Agent session state — set when a lease is acquired, cleared on release.
```
ActiveLeaseState.cs
ActivePackageState.cs
PackagePaths.cs
PackagePathUtilities.cs
IProgressSink.cs                  ← agent lifecycle progress reporting
```

### `Telemetry/`
OTel metric interfaces — only emitted from within Agent execution.
```
IDiscoveryMetrics.cs
IWorkItemExportMetrics.cs
IAttachmentDownloadMetrics.cs
IMigrationMetrics.cs
```

### `Modules/`
The Agent's plugin system — module contracts only, no configuration schema.
```
IModule.cs
IDiscoveryModule.cs
WorkItemsModuleExtensions.cs
```

---

## `DevOpsMigrationPlatform.Infrastructure` — cross-cutting implementations

Config binding, serialization, options validation, and DI extension methods that all
composition roots need. No Agent-specific or CP-specific implementations.

### `Config/`
Options binding and validation.
```
MigrationPlatformServiceExtensions.cs   ← AddMigrationPlatformOptions(), AddMigrationPlatformPolymorphicSerializers()
MigrationOptionsValidator.cs
DiscoveryOptionsOrganisationsBinder.cs
```

### `Serialization/`
Polymorphic JSON converters and type registry.
After Phase 1 Step 1.5, the cross-cutting converters move to `Abstractions/Serialization/`.
What remains here are internal helpers:
```
EndpointOptionsTypeRegistry.cs
EndpointOptionsRegistration.cs
PolymorphicOrganisationEntryConverter.cs  ← if not also moved to Abstractions
```

### `Telemetry/`
Cross-cutting telemetry utilities used by both Agent and CP:
```
DataClassificationLogProcessor.cs
DataClassificationExtensions.cs
```

### `Polyfills/`
```
IsExternalInit.cs                       ← .NET 4.8 only
```

---

## `DevOpsMigrationPlatform.Infrastructure.ControlPlane` — CP-only implementations

Concrete implementations of `Abstractions.ControlPlane` interfaces. Only
`ControlPlaneHost` references this project.

```xml
<!-- Infrastructure.ControlPlane.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions.ControlPlane\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
```

### `Metrics/`
```
InMemoryJobMetricsStore.cs        ← implements IJobMetricsStore
InMemoryJobSnapshotStore.cs       ← implements IJobSnapshotStore
JobLifecycleMetrics.cs            ← implements IJobLifecycleMetrics
SnapshotMetricExporter.cs         ← OTel metric exporter for CP
TelemetryServiceExtensions.cs     ← AddControlPlaneTelemetryServices() — registers CP-only metrics
```

---

## `DevOpsMigrationPlatform.Infrastructure.Agent` — Agent-only implementations

Concrete implementations of `Abstractions.Agent` interfaces. Only `MigrationAgent`
and `CLI.TfsMigration` reference this project.

```xml
<!-- Infrastructure.Agent.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions.Agent\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
```

### `Storage/`
```
FileSystemArtefactStore.cs        ← implements IArtefactStore
AzureBlobArtefactStore.cs         ← implements IArtefactStore (stub, NET481 only)
FileSystemPackageStoreFactory.cs  ← implements IPackageStoreFactory (moved from Factories/)
```

### `Checkpointing/`
```
CheckpointingService.cs           ← implements ICheckpointingService
CheckpointingServiceFactory.cs    ← implements ICheckpointingServiceFactory
FileSystemStateStore.cs           ← implements IStateStore
PhaseTrackingService.cs           ← implements IPhaseTrackingService (moved from JobEngine/)
PhaseTrackingServiceFactory.cs    ← implements IPhaseTrackingServiceFactory (moved from JobEngine/)
```

### `Export/`
```
WorkItemExportOrchestrator.cs
EmbeddedImageExportService.cs     ← implements IEmbeddedImageExportService
CompositeWorkItemRevisionSourceFactory.cs ← implements IWorkItemRevisionSourceFactory
```

### `Import/`
```
WorkItemImportOrchestrator.cs
CompositeWorkItemImportTargetFactory.cs   ← implements IWorkItemImportTargetFactory
CompositeWorkItemResolutionStrategyFactory.cs ← implements IWorkItemResolutionStrategyFactory
RevisionFolderProcessor.cs
RevisionFolderProcessorFactory.cs         ← implements IRevisionFolderProcessorFactory
WorkItemRevisionFolderParser.cs
SqliteIdMapStore.cs               ← implements IIdMapStore
IdMapStoreFactory.cs              ← implements IIdMapStoreFactory
PassThroughIdentityMappingService.cs ← implements IIdentityMappingService
NullResolutionStrategy.cs         ← implements IWorkItemResolutionStrategy
```

### `Discovery/`
```
CatalogService.cs                 ← implements ICatalogService (moved from Services/)
InventoryService.cs               ← implements IInventoryService (moved from Services/)
DependencyDiscoveryService.cs     ← implements IDependencyDiscoveryService (moved from Services/)
QueryFingerprintService.cs        ← implements IQueryFingerprintService (moved from Services/)
PackageLockFileService.cs         ← implements IPackageLockService (moved from Services/)
FileSystemIdentityMappingService.cs ← implements IIdentityMappingService (moved from Services/)
```

### `Modules/`
```
WorkItemsModule.cs                ← implements IModule
InventoryDiscoveryModule.cs       ← implements IDiscoveryModule
DependencyDiscoveryModule.cs      ← implements IDiscoveryModule
ModuleServiceCollectionExtensions.cs ← AddWorkItemsModule(), AddInventoryDiscoveryModule(), AddDependencyDiscoveryModule()
```

### `Modules/Discovery/`
Internal utilities for dependency analysis:
```
ProjectDependencyRecord.cs
MermaidUtilities.cs
MermaidDiagramBuilder.cs
TransitiveDependencyEdge.cs
ProjectPairKey.cs
TransitiveDependencyWalker.cs
TransitiveMermaidBuilder.cs
UnionFindComponentLabeler.cs
```

### `Telemetry/`
Agent-side telemetry: progress sinks, metric implementations, log providers.
```
DiscoveryMetrics.cs               ← implements IDiscoveryMetrics
MigrationMetrics.cs               ← implements IMigrationMetrics
AnsiProgressSink.cs               ← implements IProgressSink
CompositeProgressSink.cs          ← implements IProgressSink
ControlPlaneProgressSink.cs       ← implements IProgressSink (Agent pushes to CP)
PackageProgressSink.cs            ← implements IProgressSink (writes to package)
ControlPlaneTelemetryClient.cs    ← implements IControlPlaneTelemetryClient
ControlPlaneLoggerProvider.cs     ← ILoggerProvider (forwards logs to CP)
PackageLoggerProvider.cs          ← ILoggerProvider (writes logs to package)
TelemetryServiceExtensions.cs     ← AddAgentTelemetryServices() — registers Agent-only telemetry
DiagnosticsServiceExtensions.cs   ← AddDiagnosticsServices() — Agent diagnostic log pipeline
```

### `Validation/`
```
PackageValidator.cs               ← implements IPackageValidator
```

### `DI/`
Factory dispatcher registration — used by `Infrastructure.AzureDevOps` and `Infrastructure.Simulated`:
```
FactoryRegistrationExtensions.cs  ← moved from Extensions/
```

---

## Migration plan

Each step is independently buildable. Run `dotnet clean && dotnet build --no-incremental`
after each step. Do not combine steps — a broken intermediate state is harder to diagnose.

### Rename-preservation strategy

Git tracks renames by content similarity. To maximise rename detection:

1. **One move per file.** Every file moves ONCE to its final destination — never via an
   intermediate location. Phase 2 moves only files that stay in base `Abstractions`.
   Agent-targeted files skip Phase 2 and move directly to `Abstractions.Agent` in Phase 3.
   Same pattern for Infrastructure in Phases 4–5.

2. **Separate rename from edit.** Each step is either a batch of `git mv` operations OR a
   batch of namespace/using edits — not both. Git's rename detection threshold (default 50%
   similarity) breaks when you move AND rewrite in the same commit.

3. **Commit after each step.** Phase 2 Step 2.1a is all `git mv` operations for the
   Models/ dissolution. Step 2.1b is all namespace updates for those files. Each is one commit.

4. **Use `git mv`, not delete+create.** Even though the end result is the same, `git mv`
   records intent and avoids `git add -A` guessing games.

5. **Batch by destination.** Move all files going to the same target folder in one commit.
   This keeps the diff coherent and reviewable.

| Phase | Description | Move type |
|-------|-------------|-----------|
| 1 | Code edits — sever CLI coupling | No moves |
| 2 | Screaming architecture within base `Abstractions` | `git mv` within project |
| 3 | Create `Abstractions.ControlPlane` + `Abstractions.Agent` | `git mv` across projects |
| 4 | Screaming architecture within base `Infrastructure` | `git mv` within project |
| 5 | Create `Infrastructure.ControlPlane` + `Infrastructure.Agent` | `git mv` across projects |
| 6 | Reference cleanup, test boundaries, verification | No moves |

---

### Phase 1 — Sever the CLI ↔ Agent/CP compile-time coupling

#### Step 1.1 — Delete in-process fallback from `LocalStackHost`

**Why:** This single class is the root cause of all five invalid project references.
Removing it makes Steps 1.2–1.5 possible.

| Action | Target |
|--------|--------|
| Delete method | `LocalStackHost.StartControlPlaneInProcessAsync()` |
| Delete method | `LocalStackHost.StartAgentInProcessAsync()` |
| Replace fallback | When published binaries not found → `throw new InvalidOperationException("ControlPlane/Agent binaries not found. Run 'build.ps1 Install' to publish.")` |
| Remove using | Any `using` for `ControlPlane`, `MigrationAgent`, `Infrastructure` namespaces in `LocalStackHost.cs` |

**Verify:** CLI still launches CP + Agent via `ChildProcessHost` in process-per-component mode.

#### Step 1.2 — Extract `IPreFlightValidator` interface

**Why:** `QueueCommand` currently calls `AzureDevOpsValidation` (concrete class in
`Infrastructure.AzureDevOps`) directly. The CLI must depend on an abstraction.

| Action | Detail |
|--------|--------|
| Create file | `Abstractions/Configuration/IPreFlightValidator.cs` |
| Extract interface | From `AzureDevOpsValidation` public surface |
| Update `QueueCommand` | Inject `IPreFlightValidator` instead of `new AzureDevOpsValidation(...)` |
| Register in DI | `Infrastructure.AzureDevOps` registers `AzureDevOpsValidation` as `IPreFlightValidator` |

#### Step 1.3 — Remove direct `ConfigurationService` instantiation

**Why:** CLI commands `new` up a concrete class from `Infrastructure.Services`.

| Action | Detail |
|--------|--------|
| Update | `QueueCommand`, `ConfigNewCommand`, `ConfigureCommand`, `PrepareCommand` |
| Change | Replace `new ConfigurationService(...)` → constructor-inject `IConfigurationService` |
| Register | In `MigrationCliServiceCollectionExtensions`, add `services.AddSingleton<IConfigurationService, ConfigurationService>()` — but this still requires Infrastructure reference |
| Split registration | Create `Abstractions/Configuration/ConfigurationServiceCollectionExtensions.cs` with `AddConfigurationService(this IServiceCollection)` method that takes a factory delegate |
| Actual registration | Move to `Infrastructure` as an extension method that the CLI's composition root calls |

#### Step 1.4 — Move config-schema types to `Abstractions/Options/`

**Why:** The CLI needs these to deserialise config files. Currently they live in
`Infrastructure.AzureDevOps` and `Infrastructure.Simulated`, forcing a reference.

| Source | Destination |
|--------|-------------|
| `Infrastructure.AzureDevOps/Options/AzureDevOpsEndpointOptions.cs` | `Abstractions/Options/AzureDevOpsEndpointOptions.cs` |
| `Infrastructure.AzureDevOps/Options/AzureDevOpsOrganisationEntry.cs` | `Abstractions/Options/AzureDevOpsOrganisationEntry.cs` |
| `Infrastructure.Simulated/Options/SimulatedEndpointOptions.cs` | `Abstractions/Options/SimulatedEndpointOptions.cs` |
| `Infrastructure.Simulated/Options/SimulatedOrganisationEntry.cs` | `Abstractions/Options/SimulatedOrganisationEntry.cs` |
| `Infrastructure.Simulated/Options/SimulatedProjectConfig.cs` | `Abstractions/Options/SimulatedProjectConfig.cs` |
| `Infrastructure.Simulated/Options/SimulatedWorkItemTypeConfig.cs` | `Abstractions/Options/SimulatedWorkItemTypeConfig.cs` |
| `Infrastructure.Simulated/Options/SimulatedGeneratorConfig.cs` | `Abstractions/Options/SimulatedGeneratorConfig.cs` |

Update namespaces from `DevOpsMigrationPlatform.Infrastructure.*.Options` →
`DevOpsMigrationPlatform.Abstractions.Options`.

Also move:
| Source | Destination |
|--------|-------------|
| `Infrastructure/Extensions/EndpointOptionsRegistrationExtensions.cs` | `Abstractions/Options/EndpointOptionsRegistrationExtensions.cs` |

#### Step 1.5 — Move serialization to `Abstractions/Serialization/`

**Why:** Polymorphic JSON converters are needed by CLI, CP, and Agent to deserialise
config and job payloads. Currently in `Infrastructure.Serialization`.

| Source | Destination |
|--------|-------------|
| `Infrastructure/Serialization/PolymorphicEndpointOptionsConverter.cs` | `Abstractions/Serialization/PolymorphicEndpointOptionsConverter.cs` |
| `Infrastructure/Serialization/MigrationPlatformJsonContext.cs` (if exists) | `Abstractions/Serialization/MigrationPlatformJsonContext.cs` |

Update namespace: `DevOpsMigrationPlatform.Infrastructure.Serialization` →
`DevOpsMigrationPlatform.Abstractions.Serialization`.

#### Step 1.6 — Remove all invalid project references

Delete from `CLI.Migration.csproj`:
```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.ControlPlane\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.MigrationAgent\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.Simulated\..." />
```

**Build gate:** `CLI.Migration` must compile with only `Abstractions` as a project reference.
Any remaining compile error means a dependency was missed in Steps 1.1–1.5.

#### Step 1.7 — Remove `LogDownloadController` from ControlPlane

**Why:** `LogDownloadController` injects `IPackageStoreFactory` and `IArtefactStore` to
serve log files. This forces the ControlPlane to reference Agent-internal contracts.
Operators can access logs directly from the well-known package directory.

| Action | Detail |
|--------|--------|
| Delete | `ControlPlane/Controllers/LogDownloadController.cs` |
| Add field | `JobDiagnostics.LogPath` (string) — the Agent populates this with the absolute path to the package log directory |
| Update Agent | Set `JobDiagnostics.LogPath` when reporting diagnostics to CP |
| Update CLI | Display log path to operator: `"Logs available at: {logPath}"` |

After this step, ControlPlane has zero dependency on `IArtefactStore`, `IPackageStoreFactory`,
or any other Agent-internal contract.

#### Step 1.8 — Move ControlPlane → Infrastructure registration to ControlPlaneHost

**Why:** `ControlPlane/Services/ControlPlaneServiceExtensions.cs` directly references
`DevOpsMigrationPlatform.Infrastructure.Factories` and hard-codes
`FileSystemPackageStoreFactory`. The ControlPlane library project must not reference
`Infrastructure`.

| Action | Detail |
|--------|--------|
| Move registration | `services.AddSingleton<IPackageStoreFactory, FileSystemPackageStoreFactory>()` from `ControlPlane/Services/ControlPlaneServiceExtensions.cs` to `ControlPlaneHost/Program.cs` |
| Remove using | `using DevOpsMigrationPlatform.Infrastructure.Factories` from ControlPlane |
| Remove reference | `ControlPlane.csproj` must NOT reference `Infrastructure` |
| Verify | `ControlPlane.csproj` references only `Abstractions` + `Abstractions.ControlPlane` |

Note: After Step 1.7, the `IPackageStoreFactory` registration may no longer be needed
in the ControlPlane at all. If no other CP service uses it, delete it entirely.

#### Step 1.9 — Fix test project references

**Why:** Test projects must not reference across component boundaries. The test project
should only reference its system-under-test and the abstraction layers it needs.

| Test project | Remove reference | Reason |
|---|---|---|
| `Infrastructure.Tests` | `CLI.Migration` | Infrastructure tests must not depend on CLI |
| `CLI.Migration.Tests` | `Infrastructure` | CLI tests must mock abstractions, not use concrete infra |
| `CLI.Migration.Tests` | `Infrastructure.AzureDevOps` | CLI tests must mock abstractions, not use concrete infra |

For any test that currently depends on the removed reference:
- If it tests CLI+Infrastructure integration → move to a dedicated integration test project
- If it uses concrete types for test setup → replace with mocks of the abstraction interfaces
- If it only uses config types → those are now in `Abstractions/Options/` (no reference needed)

---

### Phase 2 — Restructure `Abstractions` folders (screaming architecture)

All moves within the same project. No assembly boundary changes. Every step is a
namespace update + file move. Run build after each step.

#### Step 2.1 — Dissolve `Models/` into business-noun folders

`Models/` is the largest technical bucket (70 files). Every file moves to a folder
named after the business concept it represents.

**Create new folders:** `Jobs/`, `Streaming/`, `Organisations/`, `ControlPlane/`
(ControlPlane/ may already exist for `IControlPlaneClient.cs`).

| Source file in `Models/` | Destination folder | Notes |
|--------------------------|-------------------|-------|
| `Job.cs` | `Jobs/` | |
| `MigrationJob.cs` | `Jobs/` | |
| `DiscoveryJob.cs` | `Jobs/` | |
| `DiscoveryJobType.cs` | `Jobs/` | |
| `JobModule.cs` | `Jobs/` | |
| `JobModuleScope.cs` | `Jobs/` | |
| `JobPackage.cs` | `Jobs/` | |
| `JobResume.cs` | `Jobs/` | |
| `JobGuardrails.cs` | `Jobs/` | |
| `JobPolicies.cs` | `Jobs/` | |
| `ResumeDecision.cs` | `Jobs/` | |
| `ResumeDecisionStatus.cs` | `Jobs/` | |
| `ResumeRejectedException.cs` | `Jobs/` | |
| `ProgressEvent.cs` | `Streaming/` | |
| `DiagnosticLogRecord.cs` | `Streaming/` | |
| `OrganisationEndpoint.cs` | `Organisations/` | |
| `OrganisationEndpointAuthentication.cs` | `Organisations/` | |
| `ScopedOrganisationEndpoint.cs` | `Organisations/` | |
| `ValidationContext.cs` | `Validation/` | merge with existing |
| `FilterOperator.cs` | `Options/` | config filter concern |
| `JobSummary.cs` | `ControlPlane/` | |
| `JobMetrics.cs` | `ControlPlane/` | |
| `JobSnapshot.cs` | `ControlPlane/` | |
| `JobBootstrap.cs` | `ControlPlane/` | |
| `JobScopeCounters.cs` | `ControlPlane/` | |
| `JobDiagnostics.cs` | `ControlPlane/` | |
| `MigrationCounters.cs` | `ControlPlane/` | |
| `MigrationDiagnostics.cs` | `ControlPlane/` | |
| `OrgSnapshot.cs` | `ControlPlane/` | |
| `ProjectSnapshot.cs` | `ControlPlane/` | |
| `ProjectStatus.cs` | `ControlPlane/` | |
| `TargetStatus.cs` | `ControlPlane/` | |
| `DiscoveryCounters.cs` | `ControlPlane/` | |
| `InventoryCounters.cs` | `ControlPlane/` | |
| `DependencyCounters.cs` | `ControlPlane/` | |

**Remaining `Models/` files are Agent-internal.** They stay in `Models/` temporarily —
they move to `Abstractions.Agent` in Phase 3:

| File | Phase 3 destination |
|------|-------------------|
| `WorkItemRevision.cs` | `Agent/WorkItems/` |
| `WorkItemField.cs` | `Agent/WorkItems/` |
| `WorkItemComment.cs` | `Agent/WorkItems/` |
| `WorkItemIdentityRef.cs` | `Agent/WorkItems/` |
| `WorkItemRelations.cs` | `Agent/WorkItems/` |
| `WorkItemCounters.cs` | `Agent/WorkItems/` |
| `WorkItemFetchScope.cs` | `Agent/WorkItems/` |
| `WorkItemFieldFilterEvaluator.cs` | `Agent/WorkItems/` |
| `WorkItemFieldFilterOptions.cs` | `Agent/WorkItems/` |
| `WorkItemQueryChunk.cs` | `Agent/WorkItems/` |
| `WorkItemQueryCountChunk.cs` | `Agent/WorkItems/` |
| `ExternalWorkItemLink.cs` | `Agent/WorkItems/` |
| `HyperlinkWorkItemLink.cs` | `Agent/WorkItems/` |
| `RelatedWorkItemLink.cs` | `Agent/WorkItems/` |
| `LinkScope.cs` | `Agent/WorkItems/` |
| `FetchedWorkItem.cs` | `Agent/WorkItems/` |
| `IdMapEntry.cs` | `Agent/WorkItems/` |
| `AttachmentMetadata.cs` | `Agent/Attachments/` |
| `AttachmentCounters.cs` | `Agent/Attachments/` |
| `AttachmentDownloadResult.cs` | `Agent/Attachments/` |
| `AttachmentMapEntry.cs` | `Agent/Attachments/` |
| `EmbeddedImageMetadata.cs` | `Agent/Attachments/` |
| `EmbeddedImageDownloadResult.cs` | `Agent/Attachments/` |
| `ExportContext.cs` | `Agent/Export/` |
| `BatchContinuationToken.cs` | `Agent/Export/` |
| `RevisionProcessResult.cs` | `Agent/Export/` |
| `DiscoveryContext.cs` | `Agent/Discovery/` |
| `InventoryReport.cs` | `Agent/Discovery/` |
| `InventorySummary.cs` | `Agent/Discovery/` |
| `InventoryProgressEvent.cs` | `Agent/Discovery/` |
| `ProjectDiscoverySummary.cs` | `Agent/Discovery/` |
| `DependencyRecord.cs` | `Agent/Discovery/` |
| `DependencySummary.cs` | `Agent/Discovery/` |
| `DependencyProgressEvent.cs` | `Agent/Discovery/` |
| `ImportContext.cs` | `Agent/Import/` |
| `ImportedWorkItemResult.cs` | `Agent/Import/` |

**After Step 2.1:** `Models/` contains only Agent-internal files (tagged for Phase 3).
Delete `Models/` after Phase 3.

#### Step 2.2 — Dissolve `Services/` into business-noun folders

`Services/` has 41 interfaces. Every interface moves beside the models it operates on.

| Source file in `Services/` | Destination folder | Notes |
|----------------------------|-------------------|-------|
| `IConfigurationService.cs` | `Configuration/` | stays in Abstractions |
| `IJobRunner.cs` | `ControlPlane/` | HTTP contract — stays in Abstractions |

**Remaining 39 files are Agent-internal.** They stay in `Services/` temporarily —
they move to `Abstractions.Agent` in Phase 3:

| File | Phase 3 destination |
|------|-------------------|
| `ICatalogService.cs` | `Agent/Discovery/` |
| `IInventoryService.cs` | `Agent/Discovery/` |
| `IInventoryServiceFactory.cs` | `Agent/Discovery/` |
| `IProjectDiscoveryService.cs` | `Agent/Discovery/` |
| `IRepoDiscoveryService.cs` | `Agent/Discovery/` |
| `IWorkItemDiscoveryService.cs` | `Agent/Discovery/` |
| `IDependencyDiscoveryService.cs` | `Agent/Discovery/` |
| `IDependencyDiscoveryServiceFactory.cs` | `Agent/Discovery/` |
| `IWorkItemRevisionSource.cs` | `Agent/Export/` |
| `IWorkItemRevisionSourceFactory.cs` | `Agent/Export/` |
| `IRevisionFolderProcessor.cs` | `Agent/Export/` |
| `IRevisionFolderProcessorFactory.cs` | `Agent/Export/` |
| `IWorkItemFetchService.cs` | `Agent/Export/` |
| `IWorkItemCommentSource.cs` | `Agent/Export/` |
| `IWorkItemCommentSourceFactory.cs` | `Agent/Export/` |
| `IWorkItemQueryWindowStrategy.cs` | `Agent/Export/` |
| `WorkItemQueryWindow.cs` | `Agent/Export/` |
| `IQueryFingerprintService.cs` | `Agent/Export/` |
| `IWorkItemImportTarget.cs` | `Agent/Import/` |
| `IWorkItemImportTargetFactory.cs` | `Agent/Import/` |
| `IWorkItemResolutionStrategy.cs` | `Agent/Import/` |
| `IWorkItemResolutionStrategyFactory.cs` | `Agent/Import/` |
| `IWorkItemLinkAnalysisService.cs` | `Agent/Import/` |
| `IAttachmentBinarySource.cs` | `Agent/Attachments/` |
| `IEmbeddedImageDownloader.cs` | `Agent/Attachments/` |
| `IEmbeddedImageExportService.cs` | `Agent/Attachments/` |
| `IIdentityMappingService.cs` | `Agent/Identity/` |
| `ICheckpointingService.cs` | `Agent/Checkpointing/` |
| `ICheckpointingServiceFactory.cs` | `Agent/Checkpointing/` |
| `IPhaseTrackingService.cs` | `Agent/Checkpointing/` |
| `IPhaseTrackingServiceFactory.cs` | `Agent/Checkpointing/` |
| `IPackageLockService.cs` | `Agent/Storage/` |
| `IPackageStoreFactory.cs` | `Agent/Storage/` |
| `IPackageValidator.cs` | `Agent/Storage/` |
| `IIdMapStore.cs` | `Agent/Storage/` |
| `IIdMapStoreFactory.cs` | `Agent/Storage/` |
| `IProgressSink.cs` | `Agent/Lease/` |
| `IModule.cs` | `Agent/Modules/` |
| `IDiscoveryModule.cs` | `Agent/Modules/` |

**After Step 2.2:** `Services/` contains only Agent-internal files (tagged for Phase 3).
Delete `Services/` after Phase 3.

#### Step 2.3 — Dissolve `Errors/`

| Source | Destination | Notes |
|--------|-------------|-------|
| `Errors/MigrationErrorCategory.cs` | `Jobs/MigrationErrorCategory.cs` | job-level error classification |
| `Errors/MigrationException.cs` | `Jobs/MigrationException.cs` | job-level exception |
| `Errors/PackageLockConflictException.cs` | stays in `Errors/` temporarily | moves to `Agent/Storage/` in Phase 3 |

Delete `Errors/` after Phase 3.

#### Step 2.4 — Move root-level files into proper folders

| Source | Destination | Notes |
|--------|-------------|-------|
| `Abstractions/IControlPlaneClient.cs` | `Abstractions/ControlPlane/IControlPlaneClient.cs` | HTTP contract |
| `Abstractions/ActiveLeaseState.cs` | stays at root temporarily | moves to `Agent/Lease/` in Phase 3 |
| `Abstractions/ActivePackageState.cs` | stays at root temporarily | moves to `Agent/Lease/` in Phase 3 |
| `Abstractions/PackagePaths.cs` | stays at root temporarily | moves to `Agent/Lease/` in Phase 3 |

#### Step 2.5 — Dissolve `Utilities/`

| Source | Destination | Rename |
|--------|-------------|--------|
| `Abstractions/Utilities/TokenResolver.cs` | `Abstractions/Options/ConfigTokenResolver.cs` | class: `TokenResolver` → `ConfigTokenResolver` |
| `Abstractions/Utilities/PathUtilities.cs` | stays temporarily | moves to `Agent/Lease/PackagePathUtilities.cs` in Phase 3 |
| `CLI.Migration/Utilities/PathUtilities.cs` | `CLI.Migration/Services/CliPathUtilities.cs` | rename to distinguish from Agent version |
| `CLI.Migration/Utilities/ExceptionSanitizer.cs` | `CLI.Migration/Services/ExceptionSanitizer.cs` | |

Delete `Abstractions/Utilities/` after Phase 3.
Delete `CLI.Migration/Utilities/` after this step.

#### Step 2.6 — Move extension options out of `Modules/`

| Source | Destination |
|--------|-------------|
| `Abstractions/Modules/CommentsExtensionOptions.cs` | `Abstractions/Options/CommentsExtensionOptionsConfig.cs` |
| `Abstractions/Modules/EmbeddedImagesExtensionOptions.cs` | `Abstractions/Options/EmbeddedImagesExtensionOptionsConfig.cs` |

`Modules/` retains `WorkItemsModuleExtensions.cs` temporarily (Phase 3 moves it to Agent).

#### Step 2.7 — Separate `Telemetry/` cross-cutting from Agent/CP-specific

| Source | Destination | Notes |
|--------|-------------|-------|
| `Telemetry/IJobLifecycleMetrics.cs` | stays temporarily | Phase 3: `Abstractions.ControlPlane/Metrics/` |
| `Telemetry/IJobMetricsStore.cs` | stays temporarily | Phase 3: `Abstractions.ControlPlane/Metrics/` |
| `Telemetry/IJobSnapshotStore.cs` | stays temporarily | Phase 3: `Abstractions.ControlPlane/Metrics/` |
| `Telemetry/IControlPlaneTelemetryClient.cs` | `ControlPlane/IControlPlaneTelemetryClient.cs` | HTTP contract — cross-cutting |
| `Telemetry/IDiscoveryMetrics.cs` | stays temporarily | Phase 3: `Abstractions.Agent/Telemetry/` |
| `Telemetry/IWorkItemExportMetrics.cs` | stays temporarily | Phase 3: `Abstractions.Agent/Telemetry/` |
| `Telemetry/IAttachmentDownloadMetrics.cs` | stays temporarily | Phase 3: `Abstractions.Agent/Telemetry/` |
| `Telemetry/IMigrationMetrics.cs` | stays temporarily | Phase 3: `Abstractions.Agent/Telemetry/` |

Remaining 10 files (`DataClassification.cs`, `WellKnown*.cs`, etc.) stay in
`Abstractions/Telemetry/` — they are genuinely cross-cutting.

---

### Phase 3 — Extract `Abstractions.ControlPlane` and `Abstractions.Agent` projects

#### Step 3.1 — Create `Abstractions.ControlPlane` project

```
src/DevOpsMigrationPlatform.Abstractions.ControlPlane/
  DevOpsMigrationPlatform.Abstractions.ControlPlane.csproj
  Metrics/
    IJobLifecycleMetrics.cs
    IJobMetricsStore.cs
    IJobSnapshotStore.cs
```

```xml
<!-- Abstractions.ControlPlane.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\DevOpsMigrationPlatform.Abstractions.csproj" />
```

| Move from | Move to |
|-----------|---------|
| `Abstractions/Telemetry/IJobLifecycleMetrics.cs` | `Abstractions.ControlPlane/Metrics/IJobLifecycleMetrics.cs` |
| `Abstractions/Telemetry/IJobMetricsStore.cs` | `Abstractions.ControlPlane/Metrics/IJobMetricsStore.cs` |
| `Abstractions/Telemetry/IJobSnapshotStore.cs` | `Abstractions.ControlPlane/Metrics/IJobSnapshotStore.cs` |

Update namespace: `DevOpsMigrationPlatform.Abstractions.Telemetry` →
`DevOpsMigrationPlatform.Abstractions.ControlPlane.Metrics`

Add project reference in:
- `ControlPlane.csproj`
- `ControlPlaneHost.csproj`

#### Step 3.2 — Create `Abstractions.Agent` project

```
src/DevOpsMigrationPlatform.Abstractions.Agent/
  DevOpsMigrationPlatform.Abstractions.Agent.csproj
  WorkItems/
  Discovery/
  Export/
  Import/
  Attachments/
  Identity/
  Storage/
  Checkpointing/
  Lease/
  Telemetry/
  Modules/
```

```xml
<!-- Abstractions.Agent.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\DevOpsMigrationPlatform.Abstractions.csproj" />
```

Move all files tagged "Phase 3" in Steps 2.1–2.7:

**From `Abstractions/Models/` → `Abstractions.Agent/`:**

| File | Target folder |
|------|---------------|
| 17 WorkItem* files + links + `FetchedWorkItem` + `IdMapEntry` | `WorkItems/` |
| 6 Attachment/EmbeddedImage files | `Attachments/` |
| `ExportContext.cs`, `BatchContinuationToken.cs`, `RevisionProcessResult.cs` | `Export/` |
| `DiscoveryContext.cs`, `InventoryReport.cs`, `InventorySummary.cs`, `InventoryProgressEvent.cs`, `ProjectDiscoverySummary.cs`, `DependencyRecord.cs`, `DependencySummary.cs`, `DependencyProgressEvent.cs` | `Discovery/` |
| `ImportContext.cs`, `ImportedWorkItemResult.cs` | `Import/` |

**From `Abstractions/Services/` → `Abstractions.Agent/`:**

| File | Target folder |
|------|---------------|
| 8 Discovery interfaces + factories | `Discovery/` |
| 10 Export interfaces + factories + `WorkItemQueryWindow.cs` | `Export/` |
| 5 Import interfaces + factories | `Import/` |
| 3 Attachment interfaces | `Attachments/` |
| `IIdentityMappingService.cs` | `Identity/` |
| 5 Checkpointing/PhaseTracking interfaces + factories | `Checkpointing/` |
| 5 Package/IdMap interfaces + factories | `Storage/` |
| `IProgressSink.cs` | `Lease/` |

**From `Abstractions/Services/` → `Abstractions.Agent/Modules/`:**

| File | Target folder |
|------|---------------|
| `IModule.cs` | `Modules/` |
| `IDiscoveryModule.cs` | `Modules/` |

**From `Abstractions/Modules/` → `Abstractions.Agent/Modules/`:**

| File | Target folder |
|------|---------------|
| `WorkItemsModuleExtensions.cs` | `Modules/` |

**From `Abstractions/` root → `Abstractions.Agent/Lease/`:**

| File | Target folder |
|------|---------------|
| `ActiveLeaseState.cs` | `Lease/` |
| `ActivePackageState.cs` | `Lease/` |
| `PackagePaths.cs` | `Lease/` |

**From `Abstractions/Utilities/` → `Abstractions.Agent/Lease/`:**

| File | Target folder | Rename |
|------|---------------|--------|
| `PathUtilities.cs` | `Lease/` | class: `PathUtilities` → `PackagePathUtilities` |

**From `Abstractions/Errors/` → `Abstractions.Agent/Storage/`:**

| File | Target folder |
|------|---------------|
| `PackageLockConflictException.cs` | `Storage/` |

**From `Abstractions/Storage/` → `Abstractions.Agent/Storage/`:**

| File | Target folder |
|------|---------------|
| `IArtefactStore.cs` | `Storage/` |
| `IStateStore.cs` | `Storage/` |

**From `Abstractions/Checkpointing/` → `Abstractions.Agent/Checkpointing/`:**

| File | Target folder |
|------|---------------|
| `CursorEntry.cs` | `Checkpointing/` |
| `CursorStage.cs` | `Checkpointing/` |
| `JobPhaseRecord.cs` | `Checkpointing/` |

**From `Abstractions/Telemetry/` → `Abstractions.Agent/Telemetry/`:**

| File | Target folder |
|------|---------------|
| `IDiscoveryMetrics.cs` | `Telemetry/` |
| `IWorkItemExportMetrics.cs` | `Telemetry/` |
| `IAttachmentDownloadMetrics.cs` | `Telemetry/` |
| `IMigrationMetrics.cs` | `Telemetry/` |

Update root namespace: `DevOpsMigrationPlatform.Abstractions.*` →
`DevOpsMigrationPlatform.Abstractions.Agent.*`

#### Step 3.3 — Add project references to consumers

**Temporary state:** `Infrastructure.csproj` references `Abstractions.Agent` because
it still contains Agent-specific implementations (modules, orchestrators, stores).
This reference moves to `Infrastructure.Agent.csproj` in Phase 5 and is removed from
base `Infrastructure.csproj`.

| Project | Add reference to |
|---------|-----------------|
| `MigrationAgent.csproj` | `Abstractions.Agent` |
| `CLI.TfsMigration.csproj` | `Abstractions.Agent` |
| `Infrastructure.csproj` | `Abstractions.Agent` ← **temporary**, moves to `Infrastructure.Agent` in Phase 5 |
| `Infrastructure.AzureDevOps.csproj` | `Abstractions.Agent` |
| `Infrastructure.Simulated.csproj` | `Abstractions.Agent` |
| `Infrastructure.TfsObjectModel.csproj` | `Abstractions.Agent` |
| `ControlPlane.csproj` | `Abstractions.ControlPlane` |
| `ControlPlaneHost.csproj` | `Abstractions.ControlPlane` |

#### Step 3.4 — Clean up empty folders

Delete from `Abstractions/`:
- `Models/` (empty after all files moved)
- `Services/` (empty after all files moved)
- `Errors/` (empty after all files moved)
- `Utilities/` (empty after all files moved)
- `Storage/` (moved to Agent)
- `Checkpointing/` (moved to Agent)
- `Modules/` (moved to Agent)

#### Step 3.5 — Update `DevOpsMigrationPlatform.slnx`

Add both new projects to the solution file.

---

### Phase 4 — Restructure `Infrastructure` folders (screaming architecture, cross-cutting only)

Same pattern as Phase 2. Move only files that STAY in base `Infrastructure` to their
screaming-architecture folders. Agent-targeted and CP-targeted files skip this phase and
move directly to `Infrastructure.Agent` / `Infrastructure.ControlPlane` in Phase 5.

#### Step 4.1a — `git mv` cross-cutting files to screaming folders

| Source | Destination | Notes |
|--------|-------------|-------|
| `Infrastructure/Config/MigrationPlatformServiceExtensions.cs` | stays — already in `Config/` | no move |
| `Infrastructure/Config/MigrationOptionsValidator.cs` | stays — already in `Config/` | no move |
| `Infrastructure/Config/DiscoveryOptionsOrganisationsBinder.cs` | stays — already in `Config/` | no move |
| `Infrastructure/Telemetry/DataClassificationLogProcessor.cs` | stays — already in `Telemetry/` | no move |
| `Infrastructure/Telemetry/DataClassificationExtensions.cs` | stays — already in `Telemetry/` | no move |
| `Infrastructure/Polyfills/IsExternalInit.cs` | stays — already in `Polyfills/` | no move |

Most cross-cutting files are already in correctly-named folders. The only structural
change is removing files that leave in Phase 5. No `git mv` operations needed here unless
renaming the `Config/` folder (it's already descriptive enough).

#### Step 4.1b — Namespace updates for any moved files

Update `namespace` and `using` statements for any files moved in 4.1a.

---

### Phase 5 — Extract `Infrastructure.ControlPlane` and `Infrastructure.Agent` projects

#### Step 5.1a — Create `Infrastructure.ControlPlane` project

```
src/DevOpsMigrationPlatform.Infrastructure.ControlPlane/
  DevOpsMigrationPlatform.Infrastructure.ControlPlane.csproj
```

```xml
<!-- Infrastructure.ControlPlane.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions.ControlPlane\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
```

#### Step 5.1b — `git mv` CP telemetry files to `Infrastructure.ControlPlane/Metrics/`

| Source | Destination |
|--------|-------------|
| `Infrastructure/Telemetry/InMemoryJobMetricsStore.cs` | `Infrastructure.ControlPlane/Metrics/InMemoryJobMetricsStore.cs` |
| `Infrastructure/Telemetry/InMemoryJobSnapshotStore.cs` | `Infrastructure.ControlPlane/Metrics/InMemoryJobSnapshotStore.cs` |
| `Infrastructure/Telemetry/JobLifecycleMetrics.cs` | `Infrastructure.ControlPlane/Metrics/JobLifecycleMetrics.cs` |
| `Infrastructure/Telemetry/SnapshotMetricExporter.cs` | `Infrastructure.ControlPlane/Metrics/SnapshotMetricExporter.cs` |

#### Step 5.1c — Namespace updates for CP files

Update `namespace` → `DevOpsMigrationPlatform.Infrastructure.ControlPlane.Metrics`

#### Step 5.1d — Create `AddControlPlaneTelemetryServices()` registration

Extract CP-specific registrations from the current `AddTelemetryServices()` into a new
extension method in `Infrastructure.ControlPlane/Metrics/TelemetryServiceExtensions.cs`.

Update `ControlPlaneHost/Program.cs` to call `AddControlPlaneTelemetryServices()`.

#### Step 5.2a — Create `Infrastructure.Agent` project

```
src/DevOpsMigrationPlatform.Infrastructure.Agent/
  DevOpsMigrationPlatform.Infrastructure.Agent.csproj
```

```xml
<!-- Infrastructure.Agent.csproj -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions.Agent\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
```

#### Step 5.2b — `git mv` Agent files to `Infrastructure.Agent/` with screaming architecture

All files move directly from their current `Infrastructure/` location to their final
screaming-architecture folder in `Infrastructure.Agent/`. One move per file.

**Storage/** (from `Infrastructure/Storage/` + `Infrastructure/Factories/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Storage/FileSystemArtefactStore.cs` | `Infrastructure.Agent/Storage/FileSystemArtefactStore.cs` |
| `Infrastructure/Storage/AzureBlobArtefactStore.cs` | `Infrastructure.Agent/Storage/AzureBlobArtefactStore.cs` |
| `Infrastructure/Factories/FileSystemPackageStoreFactory.cs` | `Infrastructure.Agent/Storage/FileSystemPackageStoreFactory.cs` |

**Checkpointing/** (from `Infrastructure/Checkpointing/` + `Infrastructure/JobEngine/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Checkpointing/CheckpointingService.cs` | `Infrastructure.Agent/Checkpointing/CheckpointingService.cs` |
| `Infrastructure/Checkpointing/CheckpointingServiceFactory.cs` | `Infrastructure.Agent/Checkpointing/CheckpointingServiceFactory.cs` |
| `Infrastructure/Checkpointing/FileSystemStateStore.cs` | `Infrastructure.Agent/Checkpointing/FileSystemStateStore.cs` |
| `Infrastructure/JobEngine/PhaseTrackingService.cs` | `Infrastructure.Agent/Checkpointing/PhaseTrackingService.cs` |
| `Infrastructure/JobEngine/PhaseTrackingServiceFactory.cs` | `Infrastructure.Agent/Checkpointing/PhaseTrackingServiceFactory.cs` |

**Export/** (from `Infrastructure/Export/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Export/WorkItemExportOrchestrator.cs` | `Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs` |
| `Infrastructure/Export/EmbeddedImageExportService.cs` | `Infrastructure.Agent/Export/EmbeddedImageExportService.cs` |
| `Infrastructure/Export/CompositeWorkItemRevisionSourceFactory.cs` | `Infrastructure.Agent/Export/CompositeWorkItemRevisionSourceFactory.cs` |

**Import/** (from `Infrastructure/Import/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Import/WorkItemImportOrchestrator.cs` | `Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` |
| `Infrastructure/Import/CompositeWorkItemImportTargetFactory.cs` | `Infrastructure.Agent/Import/CompositeWorkItemImportTargetFactory.cs` |
| `Infrastructure/Import/CompositeWorkItemResolutionStrategyFactory.cs` | `Infrastructure.Agent/Import/CompositeWorkItemResolutionStrategyFactory.cs` |
| `Infrastructure/Import/RevisionFolderProcessor.cs` | `Infrastructure.Agent/Import/RevisionFolderProcessor.cs` |
| `Infrastructure/Import/RevisionFolderProcessorFactory.cs` | `Infrastructure.Agent/Import/RevisionFolderProcessorFactory.cs` |
| `Infrastructure/Import/WorkItemRevisionFolderParser.cs` | `Infrastructure.Agent/Import/WorkItemRevisionFolderParser.cs` |
| `Infrastructure/Import/SqliteIdMapStore.cs` | `Infrastructure.Agent/Import/SqliteIdMapStore.cs` |
| `Infrastructure/Import/IdMapStoreFactory.cs` | `Infrastructure.Agent/Import/IdMapStoreFactory.cs` |
| `Infrastructure/Import/PassThroughIdentityMappingService.cs` | `Infrastructure.Agent/Import/PassThroughIdentityMappingService.cs` |
| `Infrastructure/Import/NullResolutionStrategy.cs` | `Infrastructure.Agent/Import/NullResolutionStrategy.cs` |

**Discovery/** (from `Infrastructure/Services/` + `Infrastructure/Modules/Discovery/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Services/CatalogService.cs` | `Infrastructure.Agent/Discovery/CatalogService.cs` |
| `Infrastructure/Services/InventoryService.cs` | `Infrastructure.Agent/Discovery/InventoryService.cs` |
| `Infrastructure/Services/DependencyDiscoveryService.cs` | `Infrastructure.Agent/Discovery/DependencyDiscoveryService.cs` |
| `Infrastructure/Services/QueryFingerprintService.cs` | `Infrastructure.Agent/Discovery/QueryFingerprintService.cs` |
| `Infrastructure/Services/PackageLockFileService.cs` | `Infrastructure.Agent/Discovery/PackageLockFileService.cs` |
| `Infrastructure/Services/FileSystemIdentityMappingService.cs` | `Infrastructure.Agent/Discovery/FileSystemIdentityMappingService.cs` |
| `Infrastructure/Services/ConfigurationService.cs` | `Infrastructure.Agent/Discovery/ConfigurationService.cs` |
| `Infrastructure/Modules/Discovery/ProjectDependencyRecord.cs` | `Infrastructure.Agent/Discovery/ProjectDependencyRecord.cs` |
| `Infrastructure/Modules/Discovery/MermaidUtilities.cs` | `Infrastructure.Agent/Discovery/MermaidUtilities.cs` |
| `Infrastructure/Modules/Discovery/MermaidDiagramBuilder.cs` | `Infrastructure.Agent/Discovery/MermaidDiagramBuilder.cs` |
| `Infrastructure/Modules/Discovery/TransitiveDependencyEdge.cs` | `Infrastructure.Agent/Discovery/TransitiveDependencyEdge.cs` |
| `Infrastructure/Modules/Discovery/ProjectPairKey.cs` | `Infrastructure.Agent/Discovery/ProjectPairKey.cs` |
| `Infrastructure/Modules/Discovery/TransitiveDependencyWalker.cs` | `Infrastructure.Agent/Discovery/TransitiveDependencyWalker.cs` |
| `Infrastructure/Modules/Discovery/TransitiveMermaidBuilder.cs` | `Infrastructure.Agent/Discovery/TransitiveMermaidBuilder.cs` |
| `Infrastructure/Modules/Discovery/UnionFindComponentLabeler.cs` | `Infrastructure.Agent/Discovery/UnionFindComponentLabeler.cs` |

**Modules/** (from `Infrastructure/Modules/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Modules/WorkItemsModule.cs` | `Infrastructure.Agent/Modules/WorkItemsModule.cs` |
| `Infrastructure/Modules/InventoryDiscoveryModule.cs` | `Infrastructure.Agent/Modules/InventoryDiscoveryModule.cs` |
| `Infrastructure/Modules/DependencyDiscoveryModule.cs` | `Infrastructure.Agent/Modules/DependencyDiscoveryModule.cs` |
| `Infrastructure/ModuleServiceCollectionExtensions.cs` | `Infrastructure.Agent/Modules/ModuleServiceCollectionExtensions.cs` |

**Telemetry/** (from `Infrastructure/Telemetry/` — Agent-specific only):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Telemetry/DiscoveryMetrics.cs` | `Infrastructure.Agent/Telemetry/DiscoveryMetrics.cs` |
| `Infrastructure/Telemetry/MigrationMetrics.cs` | `Infrastructure.Agent/Telemetry/MigrationMetrics.cs` |
| `Infrastructure/Telemetry/AnsiProgressSink.cs` | `Infrastructure.Agent/Telemetry/AnsiProgressSink.cs` |
| `Infrastructure/Telemetry/CompositeProgressSink.cs` | `Infrastructure.Agent/Telemetry/CompositeProgressSink.cs` |
| `Infrastructure/Telemetry/ControlPlaneProgressSink.cs` | `Infrastructure.Agent/Telemetry/ControlPlaneProgressSink.cs` |
| `Infrastructure/Telemetry/PackageProgressSink.cs` | `Infrastructure.Agent/Telemetry/PackageProgressSink.cs` |
| `Infrastructure/Telemetry/ControlPlaneTelemetryClient.cs` | `Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs` |
| `Infrastructure/Telemetry/ControlPlaneLoggerProvider.cs` | `Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs` |
| `Infrastructure/Telemetry/PackageLoggerProvider.cs` | `Infrastructure.Agent/Telemetry/PackageLoggerProvider.cs` |

**DI/** (from `Infrastructure/Extensions/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Extensions/FactoryRegistrationExtensions.cs` | `Infrastructure.Agent/DI/FactoryRegistrationExtensions.cs` |

**Validation/** (from `Infrastructure/Validation/`):

| Source | Destination |
|--------|-------------|
| `Infrastructure/Validation/PackageValidator.cs` | `Infrastructure.Agent/Validation/PackageValidator.cs` |

#### Step 5.2c — Namespace updates for Agent files

Update all moved files: `namespace` → `DevOpsMigrationPlatform.Infrastructure.Agent.*`

#### Step 5.2d — Create `AddAgentTelemetryServices()` registration

Extract Agent-specific registrations from the current `AddTelemetryServices()` into a new
extension method in `Infrastructure.Agent/Telemetry/TelemetryServiceExtensions.cs`.

The original `AddTelemetryServices()` in base `Infrastructure/Telemetry/` retains only
cross-cutting registrations (data classification log processor). Rename to
`AddCoreTelemetryServices()` to make intent explicit.

Update `MigrationAgent/Program.cs` to call `AddAgentTelemetryServices()`.
Update `CLI.TfsMigration` to call `AddAgentTelemetryServices()`.

#### Step 5.3 — Update project references

**Remove from `Infrastructure.csproj`:**
```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions.Agent\..." />
```
Base `Infrastructure` no longer contains Agent-specific implementations.

**Add to consumers:**

| Project | Add reference |
|---------|-------------|
| `MigrationAgent.csproj` | `Infrastructure.Agent` |
| `CLI.TfsMigration.csproj` | `Infrastructure.Agent` |
| `ControlPlaneHost.csproj` | `Infrastructure.ControlPlane` |
| `Infrastructure.AzureDevOps.csproj` | `Infrastructure.Agent` |
| `Infrastructure.Simulated.csproj` | `Infrastructure.Agent` |
| `Infrastructure.TfsObjectModel.csproj` | `Infrastructure.Agent` |

**Remove from consumers (previously referenced base `Infrastructure` for Agent types):**

| Project | Remove reference | Reason |
|---------|-----------------|--------|
| `MigrationAgent.csproj` | — | Still needs base `Infrastructure` for config binding |
| `ControlPlaneHost.csproj` | — | Still needs base `Infrastructure` for config binding + serialization |

#### Step 5.4 — Update `DevOpsMigrationPlatform.slnx`

Add both new projects to the solution file.

#### Step 5.5 — Delete empty folders from base `Infrastructure`

After all moves, delete empty folders:
- `Storage/`, `Factories/`, `Checkpointing/`, `JobEngine/`, `Export/`, `Import/`
- `Modules/` (including `Discovery/` subfolder)
- `Services/`, `Extensions/`, `Validation/`

Base `Infrastructure/` retains only:
- `Config/` (3 files)
- `Telemetry/` (2–3 cross-cutting files)
- `Serialization/` (types not moved to Abstractions in Phase 1)
- `Polyfills/` (1 file)

---

### Phase 6 — Reference topology cleanup and verification

#### Step 6.1 — Fix test project references

| Test project | Remove reference | Reason |
|---|---|---|
| `Infrastructure.Tests` | `CLI.Migration` | Infrastructure tests must not depend on CLI |
| `CLI.Migration.Tests` | `Infrastructure` | CLI tests must mock abstractions, not use concrete infra |
| `CLI.Migration.Tests` | `Infrastructure.AzureDevOps` | CLI tests must mock abstractions, not use concrete infra |

For any test that currently depends on the removed reference:
- If it tests CLI+Infrastructure integration → move to a dedicated integration test project
- If it uses concrete types for test setup → replace with mocks of the abstraction interfaces
- If it only uses config types → those are now in `Abstractions/Options/` (no reference needed)

`Infrastructure.Tests` may need splitting into `Infrastructure.Tests`,
`Infrastructure.Agent.Tests`, and `Infrastructure.ControlPlane.Tests` to match the
production project boundaries. Each test project references only its system-under-test.

#### Step 6.2 — Build gate

```powershell
dotnet clean && dotnet build --no-incremental
```

All projects must compile. Zero warnings from missing types.

#### Step 6.3 — Test gate

```powershell
dotnet test
```

All existing tests must pass. The refactoring is purely structural — no behaviour changes.

#### Step 6.4 — Reference topology audit

Verify that the compiler-enforced topology matches BOTH tables in the Target State section.
Any ❌ cell that has a reference is a violation. Fix before declaring done.

**Abstractions layer:**
- `CLI.Migration` → only `Abstractions` (no `Abstractions.Agent`, no `Abstractions.ControlPlane`)
- `ControlPlane` → `Abstractions` + `Abstractions.ControlPlane` (no `Abstractions.Agent`)
- `ControlPlaneHost` → `Abstractions` + `Abstractions.ControlPlane`
- `MigrationAgent` → `Abstractions` + `Abstractions.Agent` (no `Abstractions.ControlPlane`)
- `Infrastructure` → `Abstractions` only (no `Abstractions.Agent`, no `Abstractions.ControlPlane`)
- `Infrastructure.ControlPlane` → `Abstractions` + `Abstractions.ControlPlane`
- `Infrastructure.Agent` → `Abstractions` + `Abstractions.Agent`

**Implementation layer:**
- `CLI.Migration` → `Infrastructure` only (config binding, serialization — no `Infrastructure.Agent`, no `Infrastructure.ControlPlane`)
- `ControlPlane` → NO Infrastructure references (library project; registrations in Host)
- `ControlPlaneHost` → `Infrastructure` + `Infrastructure.ControlPlane` (no `Infrastructure.Agent`)
- `MigrationAgent` → `Infrastructure` + `Infrastructure.Agent` + `Infrastructure.AzureDevOps` + `Infrastructure.Simulated` (no `Infrastructure.ControlPlane`)
- `Infrastructure.AzureDevOps` → `Infrastructure` + `Infrastructure.Agent`
- `Infrastructure.Simulated` → `Infrastructure` + `Infrastructure.Agent`
- `Infrastructure.TfsObjectModel` → `Infrastructure` + `Infrastructure.Agent`

**Test projects:**
- `Infrastructure.Tests` does NOT reference `CLI.Migration`
- `CLI.Migration.Tests` does NOT reference `Infrastructure`, `Infrastructure.Agent`, or `Infrastructure.AzureDevOps`

#### Step 6.5 — Scenario smoke test

Run at least one scenario config via `launch.json` debug profile and verify observable output.

---

## Notes

### `CLI.TfsMigration` is not a violation

`CLI.TfsMigration` IS the TFS Export Agent — it runs work item export inline, not via
ControlPlane. It correctly references `Abstractions` + `Abstractions.Agent` and directly
resolves `IWorkItemDiscoveryService`, `IProgressSink`, etc. This is by design.

### `Infrastructure` references `Abstractions.Agent` — temporary

During Phase 3, base `Infrastructure` gains a temporary `Abstractions.Agent` reference
because it still contains Agent-specific implementations. Phase 5 moves those
implementations to `Infrastructure.Agent` and removes the reference from base
`Infrastructure`. After Phase 5, base `Infrastructure` references only `Abstractions`
(cross-cutting config, serialization, telemetry utilities).

### `MigrationAgent` references Infrastructure projects

`MigrationAgent` is a composition root — it wires up concrete implementations from
`Infrastructure` (config binding), `Infrastructure.Agent` (stores, orchestrators, modules),
`Infrastructure.AzureDevOps`, and `Infrastructure.Simulated` at startup.
This is the correct place for those references. The topology table shows this explicitly.

### `ControlPlaneHost` vs `ControlPlane` — split responsibility

`ControlPlane` is a library project containing controllers, services, and domain logic.
It references only `Abstractions` + `Abstractions.ControlPlane`. It MUST NOT reference
`Infrastructure` directly.

`ControlPlaneHost` is the ASP.NET Core executable (composition root). It wires up
concrete implementations from `Infrastructure` (config binding) and
`Infrastructure.ControlPlane` (metric stores, lifecycle metrics) via DI. It MUST NOT
reference `Infrastructure.Agent`.

**Current violation:** `ControlPlane/Services/ControlPlaneServiceExtensions.cs` directly
references `DevOpsMigrationPlatform.Infrastructure.Factories` and hard-codes
`FileSystemPackageStoreFactory`. This registration must move to `ControlPlaneHost`.

### Log file access — no package reads from ControlPlane

The ControlPlane MUST NOT read from the package filesystem. The current
`LogDownloadController` injects `IPackageStoreFactory` and `IArtefactStore` to serve
log files — this is a topology violation (`ControlPlane` must not reference
`Abstractions.Agent`).

**Resolution:** Delete `LogDownloadController`. Replace with a log path resolver:
- The Agent already knows the log file paths (they are inside the package working directory)
- The Agent reports the log path to the CP as part of `JobSnapshot` or `JobDiagnostics`
- The CP returns the path to the CLI in the job status response
- The CLI displays the path to the operator: `"Logs available at: /path/to/package/.migration/Logs/"`
- The operator retrieves logs directly from the well-known filesystem location

This removes `IArtefactStore` and `IPackageStoreFactory` from the ControlPlane entirely,
eliminating the last reason for the CP to reference `Abstractions.Agent`.

### Test project boundary violations

**`Infrastructure.Tests`** currently references `CLI.Migration`. This is a cross-boundary
violation — infrastructure tests should not depend on the CLI. Any test that exercises
CLI+Infrastructure integration belongs in `CLI.Migration.Tests` or a dedicated integration
test project.

**`CLI.Migration.Tests`** currently references `Infrastructure` and
`Infrastructure.AzureDevOps`. After Phase 1 severs the CLI from Infrastructure, these
test references must also be removed. Tests that need Infrastructure types should mock
the abstraction layer interfaces instead.

### Simulated config types

`Infrastructure.Simulated/Options/` contains 5 files, not just 2. All must move to
`Abstractions/Options/` because the CLI deserialises simulated config files:
- `SimulatedEndpointOptions.cs` — endpoint connection settings
- `SimulatedOrganisationEntry.cs` — org-level config
- `SimulatedProjectConfig.cs` — per-project generation settings
- `SimulatedWorkItemTypeConfig.cs` — work item type definitions
- `SimulatedGeneratorConfig.cs` — data generation parameters

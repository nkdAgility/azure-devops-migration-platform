# Separation of Concerns — Abstractions & Project Boundary Analysis

## Context

This document captures the boundary-violation analysis of `DevOpsMigrationPlatform.Abstractions`
and `CLI.Migration` project references, and the proposed structural remediation.

The system enforces a strict communication topology:

```
CLI  ←→  ControlPlane  ←→  Agent
```

CLI and TUI must not hold Agent-internal contracts. The compiler should enforce this via
project reference topology, not developer discipline.

---

## Problem 1 — `Abstractions` is a technical bucket, not a boundary map

### Current structure

| Folder | Files | Problem |
|--------|-------|---------|
| `Models/` | 70+ files | CLI contracts, Agent domain objects, and shared types all mixed together |
| `Services/` | 40+ files | CLI-side interfaces mixed with Agent-execution interfaces |
| `Telemetry/` | 18 files | Constants (cross-cutting) and OTel interfaces (Agent/CP-specific) mixed together |
| `Checkpointing/` | 3 files | Agent-only — `CursorEntry` is only written by the Agent |
| `Storage/` | 2 files | Agent-only — `IArtefactStore`/`IStateStore` have data residency restriction |
| `Utilities/` | 2 files | Generic bucket name — screaming architecture violation |
| `Options/` | 24 files | ✅ Cross-cutting — config schema only |
| `Errors/` | 3 files | ✅ Cross-cutting |
| `Validation/` | 2 files | ✅ Cross-cutting |

### Impact

`IArtefactStore` and `IControlPlaneClient` sit in the same project. A CLI command can
accidentally inject `IArtefactStore` and the compiler will not object. The data residency
guardrail (agents.md rule 23 — only the Migration Agent and TFS Export Agent may write
to the package) relies entirely on code review.

---

## Proposed fix — split into three projects

### `DevOpsMigrationPlatform.Abstractions` — truly cross-cutting

Shared types that all three components (CLI, ControlPlane, Agent) legitimately depend on.

#### `Domain/`
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
ProgressEvent.cs                  ← flows CLI → CP → Agent → CLI
DiagnosticLogRecord.cs            ← flows CLI → CP → Agent → CLI
OrganisationEndpoint.cs
OrganisationEndpointAuthentication.cs
ScopedOrganisationEndpoint.cs
ResumeDecision.cs
ResumeDecisionStatus.cs
ResumeRejectedException.cs
ValidationContext.cs
FilterOperator.cs
```

#### `Configuration/`
CLI-facing service for loading and validating config files on disk before job submission.
The CLI calls this directly; the Agent never touches it.
```
IConfigurationService.cs
```

#### `ControlPlane/`
The HTTP contracts and payload types the CLI and Agent use to communicate with the
ControlPlane. These live in base `Abstractions` so any component that talks to CP
compiles against them without needing a reference to `Abstractions.ControlPlane`.
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

#### `Options/`
All existing `Options/` files unchanged — config schema is cross-cutting.
```
AuthenticationType.cs
CommentsExtensionOptionsConfig.cs
DiscoveryOptions.cs
EmbeddedImagesExtensionOptionsConfig.cs
EnabledExtensionOptions.cs
EndpointAuthenticationOptions.cs
FilterMode.cs
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
```

#### `Errors/`
```
MigrationErrorCategory.cs
MigrationException.cs
PackageLockConflictException.cs
```

#### `Validation/`
```
ValidationError.cs
ValidationResult.cs
```

#### `Telemetry/`
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

#### `Diagnostics/`
```
DiagnosticLogOptions.cs
```

#### `Polyfills/`
```
IsExternalInit.cs
```

---

### `DevOpsMigrationPlatform.Abstractions.ControlPlane` — CP-internal only

Contracts that only the ControlPlane itself implements internally.
Neither the CLI, TUI, nor the Agent reference this project — all three communicate
with CP exclusively via the HTTP contracts in base `Abstractions`.

#### `Metrics/`
```
IJobLifecycleMetrics.cs           ← OTel instruments emitted inside the CP process
IJobMetricsStore.cs               ← CP persists incoming agent metrics
IJobSnapshotStore.cs              ← CP persists incoming agent snapshots
```

---

### `DevOpsMigrationPlatform.Abstractions.Agent` — Agent-internal only

Contracts used exclusively inside the Migration Agent (and `CLI.TfsMigration` which
is the TFS Export Agent running inline).

#### `Storage/`
```
IArtefactStore.cs
IStateStore.cs
```

#### `Checkpointing/`
```
CursorEntry.cs
CursorStage.cs
JobPhaseRecord.cs
```

#### `Runtime/`
Agent lifecycle singletons — set when a lease is acquired, cleared on release.
```
ActiveLeaseState.cs
ActivePackageState.cs
PackagePaths.cs                   ← package path constants (IArtefactStore paths)
```

#### `WorkItems/`
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

#### `Attachments/`
```
AttachmentMetadata.cs
AttachmentCounters.cs
AttachmentDownloadResult.cs
AttachmentMapEntry.cs
EmbeddedImageMetadata.cs
EmbeddedImageDownloadResult.cs
```

#### `Export/`
```
ExportContext.cs
BatchContinuationToken.cs
RevisionProcessResult.cs
DiscoveryContext.cs
InventoryReport.cs                ← written by Agent to inventory.json; CLI never compiles against this
InventorySummary.cs
InventoryProgressEvent.cs         ← internal Agent progress signal; CLI receives ProgressEvent from CP instead
ProjectDiscoverySummary.cs
DependencyRecord.cs
DependencySummary.cs
DependencyProgressEvent.cs
```

#### `Import/`
```
ImportContext.cs
ImportedWorkItemResult.cs
```

#### `Services/`
```
ICatalogService.cs                ← queries source systems for project/count data; Agent-execution concern
IInventoryService.cs
IInventoryServiceFactory.cs
IProjectDiscoveryService.cs
IRepoDiscoveryService.cs
IWorkItemDiscoveryService.cs
IDependencyDiscoveryService.cs
IDependencyDiscoveryServiceFactory.cs
IAttachmentBinarySource.cs
ICheckpointingService.cs
ICheckpointingServiceFactory.cs
IEmbeddedImageDownloader.cs
IEmbeddedImageExportService.cs
IIdentityMappingService.cs
IIdMapStore.cs
IIdMapStoreFactory.cs
IPackageLockService.cs
IPackageStoreFactory.cs
IPackageValidator.cs
IPhaseTrackingService.cs
IPhaseTrackingServiceFactory.cs
IProgressSink.cs                  ← also used by CLI.TfsMigration (legitimate — it IS the TFS agent)
IQueryFingerprintService.cs
IRevisionFolderProcessor.cs
IRevisionFolderProcessorFactory.cs
IWorkItemCommentSource.cs
IWorkItemCommentSourceFactory.cs
IWorkItemFetchService.cs
IWorkItemImportTarget.cs
IWorkItemImportTargetFactory.cs
IWorkItemLinkAnalysisService.cs
IWorkItemQueryWindowStrategy.cs
WorkItemQueryWindow.cs
IWorkItemResolutionStrategy.cs
IWorkItemResolutionStrategyFactory.cs
IWorkItemRevisionSource.cs
IWorkItemRevisionSourceFactory.cs
```

#### `Telemetry/`
OTel metric interfaces — only emitted from within Agent execution.
```
IDiscoveryMetrics.cs
IWorkItemExportMetrics.cs
IAttachmentDownloadMetrics.cs
IMigrationMetrics.cs
```

#### `Modules/`
```
IModule.cs
IDiscoveryModule.cs
WorkItemsModuleExtensions.cs
CommentsExtensionOptions.cs
EmbeddedImagesExtensionOptions.cs
```

---

## Project reference topology — enforced by compiler

| Project | Abstractions | Abstractions.ControlPlane | Abstractions.Agent |
|---------|:---:|:---:|:---:|
| `CLI.Migration` | ✅ | ❌ | ❌ |
| `TUI` | ✅ | ❌ | ❌ |
| `ControlPlane` / `ControlPlaneHost` | ✅ | ✅ | ❌ |
| `MigrationAgent` | ✅ | ❌ | ✅ |
| `CLI.TfsMigration` (.NET 4.8) | ✅ | ❌ | ✅ |
| `Infrastructure` | ✅ | ❌ | ✅ |

All three communicating components (CLI, Agent, TUI) talk to the ControlPlane exclusively
via HTTP contracts in base `Abstractions` (`IControlPlaneClient`, `IJobRunner`,
`IControlPlaneTelemetryClient`). No component other than `ControlPlane`/`ControlPlaneHost`
needs `Abstractions.ControlPlane`. The CP-internal store and OTel interfaces are invisible
to everything outside the CP process boundary.

---

## Problem 2 — `CLI.Migration` pulls in unused Agent-side execution services

### Current `CLI.Migration.csproj` references (problematic lines)

```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.Simulated\..." />
```

### What the CLI actually needs from these

`InventoryCommand` calls `AddAzureDevOpsInventory(config)`.
`DependencyCommand` calls `AddAzureDevOpsDependencyAnalysis(config)`.

Both commands then resolve **only**:
- `IAnsiConsole`
- `IOptions<DiscoveryOptions>` — config binding
- `ControlPlaneClient` — HTTP submission

They build a `DiscoveryJob` and submit it via HTTP. They **never** resolve the
execution services from the CLI DI container.

### Services registered in the CLI process but never resolved

```
IWorkItemDiscoveryService
IProjectDiscoveryService
IRepoDiscoveryService
IWorkItemFetchService
IWiqlQueryClientFactory
IAzureDevOpsClientFactory
IWorkItemQueryWindowStrategy
IInventoryService
IInventoryServiceFactory
```

`AddSimulatedServices()` (called unconditionally from `MigrationCliServiceCollectionExtensions`)
also registers:
```
IWorkItemRevisionSourceFactory    ← Agent-only
IWorkItemImportTargetFactory      ← Agent-only
```

### Root cause

The `AddAzureDevOpsInventory()` and `AddSimulatedServices()` extension methods bundle
CLI config concerns and Agent execution concerns into a single call. The CLI has no way
to take only config binding without pulling in execution services.

### Proposed fix — split infrastructure registration by audience

```csharp
// CLI-safe — config binding and endpoint type registration only
services.AddAzureDevOpsDiscoveryConfiguration(config);
services.AddSimulatedEndpointTypes();

// Agent-only — execution services (registered only inside MigrationAgent DI setup)
services.AddAzureDevOpsInventoryExecution();
services.AddSimulatedWorkItemExecution();
```

Once split, `CLI.Migration` calls only `*Configuration` / `*EndpointTypes` variants.
Config binding can then move to `Infrastructure` (already referenced by CLI), allowing
the `Infrastructure.AzureDevOps` and `Infrastructure.Simulated` project references to
be **removed from `CLI.Migration.csproj` entirely**.

### Exception — `CLI.TfsMigration` is correct

`CLI.TfsMigration` directly resolves `IWorkItemDiscoveryService` because it **is** the
TFS Export Agent running inline — not a CLI submitting to ControlPlane. This reference
is legitimate and is not a violation.

---

## Problem 3 — `CLI.Migration` is the composition root for ControlPlane and Agent

### Current state

`LocalStackHost` has two modes:
1. **Process-per-component** (correct) — launches `ControlPlaneHost.exe` and `MigrationAgent.exe`
   as child processes via `ChildProcessHost`. No assembly dependencies needed.
2. **In-process fallback** (violation) — when published binaries are not found, `LocalStackHost`
   calls `AddControlPlaneServices()`, `AddMigrationAgentServices()`, and hosts both components
   inside the CLI process. This is why `CLI.Migration.csproj` references `ControlPlane`,
   `MigrationAgent`, `Infrastructure`, `Infrastructure.AzureDevOps`, and `Infrastructure.Simulated`.

### Why the in-process fallback must be deleted

The fallback makes the CLI the composition root for CP and Agent. This:
- **Breaks the three-channel topology.** The CLI holds `IArtefactStore`, `IModule`, every
  Agent-internal contract. A developer can inject anything.
- **Breaks telemetry isolation.** Multiple OTel pipelines in one process produce phantom
  dependency arrows on Application Insights. The existing code already warns about this.
- **Is unnecessary.** The TFS export pattern proves the CLI can launch child processes
  without in-process hosting. `ChildProcessHost` and `ExternalToolRunner` already work.
  The `dotnet run` case should build-then-launch rather than host in-process.

### What the CLI should reference after remediation

```xml
<!-- CLI.Migration.csproj — target state -->
<ProjectReference Include="..\DevOpsMigrationPlatform.Abstractions\DevOpsMigrationPlatform.Abstractions.csproj" />
```

That is it. The CLI should compile against `Abstractions` only. Everything else is
launched as a child process or accessed via HTTP.

### Detailed dependency analysis for remaining usages

| Current usage | File | What it touches | Fix |
|---|---|---|---|
| `AddControlPlaneServices()` | `LocalStackHost.cs` | `ControlPlane` | Delete in-process fallback |
| `AddMigrationAgentServices()` | `LocalStackHost.cs` | `MigrationAgent` | Delete in-process fallback |
| `ControlPlaneServiceExtensions.Assembly` | `LocalStackHost.cs` | `ControlPlane` | Delete in-process fallback |
| `AddEndpointOptionsType("AzureDevOpsServices", ...)` | `LocalStackHost.cs`, `MigrationCliServiceCollectionExtensions.cs` | `Infrastructure.AzureDevOps` (endpoint types) | Move type registration to `Abstractions` or to a thin `Infrastructure.Configuration` package |
| `AddSimulatedServices()` | `MigrationCliServiceCollectionExtensions.cs` | `Infrastructure.Simulated` (Agent factories) | Replace with `AddSimulatedConfigurationTypes()` in thin config package |
| `AddMigrationPlatformPolymorphicSerializers()` | `LocalStackHost.cs`, `MigrationPlatformHost.cs` | `Infrastructure` (serialisation) | Move polymorphic serializer registration to `Abstractions` or thin `Infrastructure.Serialization` package |
| `ConfigurationService` (concrete class) | `QueueCommand.cs`, `ConfigNewCommand.cs`, `ConfigureCommand.cs`, `PrepareCommand.cs` | `Infrastructure.Services` | Register via `IConfigurationService` from a CLI composition-root extension method; move `ConfigurationService` into a thin config package |
| `AzureDevOpsEndpointOptions` | `QueueCommand.cs`, `LocalStackHost.cs`, `InteractiveConfigurationBuilder.cs` | `Infrastructure.AzureDevOps.Options` | Move endpoint option types to `Abstractions/Options/` — they are config schema, not execution |
| `SimulatedEndpointOptions` | `LocalStackHost.cs` | `Infrastructure.Simulated.Options` | Same — move to `Abstractions/Options/` |
| `AzureDevOpsValidation` | `QueueCommand.cs` | `Infrastructure.AzureDevOps.Validation` | Extract `IPreFlightValidator` interface to `Abstractions`; implementation stays in `Infrastructure.AzureDevOps` but is resolved by DI, not direct reference |
| `AddDataClassificationFilter()` | `LocalStackHost.cs` | `Infrastructure.Telemetry` | Delete in-process fallback |
| `PolymorphicEndpointOptionsConverter` | `LocalStackHost.cs` | `Infrastructure.Serialization` | Delete in-process fallback |
| `IProgressSink` + `AnsiProgressSink` | `QueueCommand.cs` | `Abstractions` (interface) + CLI (impl) | Already correct — `IProgressSink` moves to `Abstractions.Agent`, and `QueueCommand` only uses it for TFS subprocess output. The CLI can hold its own `AnsiProgressSink` implementation since the TFS CLI IS an agent. |

### Execution plan (ordered)

1. **Delete `StartControlPlaneInProcessAsync()` and `StartAgentInProcessAsync()` from `LocalStackHost`.** 
   Require published binaries. If not found, fail with an actionable error message telling the
   developer to run `build.ps1 Install`.
2. **Move `AzureDevOpsEndpointOptions`, `SimulatedEndpointOptions`, and related type-registration
   extension methods to `Abstractions/Options/`** — they are config schema, not execution contracts.
3. **Move `ConfigurationService` registration to a `AddMigrationCliConfiguration()` extension
   method** in `Infrastructure` so the CLI resolves only the interface.
4. **Extract `IPreFlightValidator` to `Abstractions`** and remove direct reference to
   `AzureDevOpsValidation` from `QueueCommand`.
5. **Move polymorphic serialiser setup to `Abstractions`** — `PolymorphicEndpointOptionsConverter`
   is needed by anyone who deserialises the config file (CLI, CP, Agent).
6. **Remove all five invalid project references from `CLI.Migration.csproj`.**

---

## `Utilities/` — screaming architecture violation

`PathUtilities` is a package-path helper (Agent concern).
`TokenResolver` resolves config tokens (cross-cutting / CLI concern).

Neither belongs in a folder named `Utilities`. Resolution:
- `PathUtilities` → inline into `PackagePaths` or move to `Abstractions.Agent/Runtime/`
- `TokenResolver` → move to `Abstractions/Options/` or keep at root as `ConfigTokenResolver`

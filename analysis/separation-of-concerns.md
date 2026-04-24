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
ControlPlane. These live in base `Abstractions` so the CLI compiles against them
without needing a reference to `Abstractions.ControlPlane`.
```
IControlPlaneClient.cs            ← CLI reads jobs, streams logs, gets telemetry via HTTP
IJobRunner.cs                     ← CLI submits jobs to CP via HTTP
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

### `DevOpsMigrationPlatform.Abstractions.ControlPlane` — CP-internal + Agent→CP push

Contracts that only the ControlPlane itself implements or receives.
Neither the CLI nor the TUI reference this project — they communicate with CP exclusively
via the `IControlPlaneClient` / `IJobRunner` HTTP contracts in base `Abstractions`.

#### `Contracts/`
```
IControlPlaneTelemetryClient.cs   ← Agent pushes metrics + snapshots to CP over HTTP
```

#### `Metrics/`
```
IJobLifecycleMetrics.cs           ← OTel instruments on WellKnownMeterNames.ControlPlane
IJobMetricsStore.cs               ← CP stores incoming agent metrics
IJobSnapshotStore.cs              ← CP stores incoming agent snapshots
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
| `MigrationAgent` | ✅ | ✅ | ✅ |
| `CLI.TfsMigration` (.NET 4.8) | ✅ | ❌ | ✅ |
| `Infrastructure` | ✅ | ❌ | ❌ |

The CLI communicates with ControlPlane exclusively via the HTTP contracts in base `Abstractions`
(`IControlPlaneClient`, `IJobRunner`). It never compiles against `Abstractions.ControlPlane`.
A CLI developer cannot accidentally call a CP-internal store or OTel instrument because
those types are not in their reference graph.

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

## `Utilities/` — screaming architecture violation

`PathUtilities` is a package-path helper (Agent concern).
`TokenResolver` resolves config tokens (cross-cutting / CLI concern).

Neither belongs in a folder named `Utilities`. Resolution:
- `PathUtilities` → inline into `PackagePaths` or move to `Abstractions.Agent/Runtime/`
- `TokenResolver` → move to `Abstractions/Options/` or keep at root as `ConfigTokenResolver`

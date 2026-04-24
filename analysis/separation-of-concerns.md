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

| Project | Abstractions | Abstractions.ControlPlane | Abstractions.Agent |
|---------|:---:|:---:|:---:|
| `CLI.Migration` | ✅ | ❌ | ❌ |
| `TUI` | ✅ | ❌ | ❌ |
| `ControlPlane` / `ControlPlaneHost` | ✅ | ✅ | ❌ |
| `MigrationAgent` | ✅ | ❌ | ✅ |
| `CLI.TfsMigration` (.NET 4.8) | ✅ | ❌ | ✅ |
| `Infrastructure` | ✅ | ❌ | ✅ |

Every component talks to the ControlPlane exclusively via HTTP contracts in base
`Abstractions`. Only `ControlPlane`/`ControlPlaneHost` references `Abstractions.ControlPlane`.
The CP-internal store and OTel interfaces are invisible to everything outside the CP process.

---

## `DevOpsMigrationPlatform.Abstractions` — truly cross-cutting

Shared types that all three components (CLI, ControlPlane, Agent) legitimately depend on.

### `Domain/`
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
Config schema — cross-cutting. Includes backend-specific endpoint option types
(currently in `Infrastructure.AzureDevOps.Options` and `Infrastructure.Simulated.Options`)
because they are config schema, not execution contracts.
```
AuthenticationType.cs
AzureDevOpsEndpointOptions.cs     ← moved from Infrastructure.AzureDevOps.Options
AzureDevOpsOrganisationEntry.cs   ← moved from Infrastructure.AzureDevOps.Options
SimulatedEndpointOptions.cs       ← moved from Infrastructure.Simulated.Options
SimulatedOrganisationEntry.cs     ← moved from Infrastructure.Simulated.Options
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

### `Serialization/`
Polymorphic JSON converters needed by anyone who deserialises config files or job payloads.
Currently in `Infrastructure.Serialization` — must move here so CLI, CP, and Agent all
have access without referencing `Infrastructure`.
```
PolymorphicEndpointOptionsConverter.cs
MigrationPlatformJsonContext.cs   ← (or equivalent serializer setup)
```

### `Errors/`
```
MigrationErrorCategory.cs
MigrationException.cs
PackageLockConflictException.cs
```

### `Validation/`
```
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

### `Storage/`
```
IArtefactStore.cs
IStateStore.cs
```

### `Checkpointing/`
```
CursorEntry.cs
CursorStage.cs
JobPhaseRecord.cs
```

### `Runtime/`
Agent lifecycle singletons — set when a lease is acquired, cleared on release.
```
ActiveLeaseState.cs
ActivePackageState.cs
PackagePaths.cs
PathUtilities.cs                  ← moved from Utilities/; rename to PackagePathUtilities
```

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

### `Attachments/`
```
AttachmentMetadata.cs
AttachmentCounters.cs
AttachmentDownloadResult.cs
AttachmentMapEntry.cs
EmbeddedImageMetadata.cs
EmbeddedImageDownloadResult.cs
```

### `Export/`
```
ExportContext.cs
BatchContinuationToken.cs
RevisionProcessResult.cs
DiscoveryContext.cs
InventoryReport.cs
InventorySummary.cs
InventoryProgressEvent.cs
ProjectDiscoverySummary.cs
DependencyRecord.cs
DependencySummary.cs
DependencyProgressEvent.cs
```

### `Import/`
```
ImportContext.cs
ImportedWorkItemResult.cs
```

### `Services/`
```
ICatalogService.cs
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
IProgressSink.cs
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

### `Telemetry/`
OTel metric interfaces — only emitted from within Agent execution.
```
IDiscoveryMetrics.cs
IWorkItemExportMetrics.cs
IAttachmentDownloadMetrics.cs
IMigrationMetrics.cs
```

### `Modules/`
```
IModule.cs
IDiscoveryModule.cs
WorkItemsModuleExtensions.cs
CommentsExtensionOptions.cs
EmbeddedImagesExtensionOptions.cs
```

---

## Migration path

### Step 1 — Delete in-process fallback from `LocalStackHost`

Remove `StartControlPlaneInProcessAsync()` and `StartAgentInProcessAsync()`.
When published binaries are not found, fail with an actionable error:
`"ControlPlane/Agent binaries not found. Run 'build.ps1 Install' to publish."`.

This immediately removes the need for three project references:
`ControlPlane`, `MigrationAgent`, and most of `Infrastructure`.

### Step 2 — Move config-schema types to `Abstractions/Options/`

Move `AzureDevOpsEndpointOptions`, `AzureDevOpsOrganisationEntry`,
`SimulatedEndpointOptions`, `SimulatedOrganisationEntry` from their
`Infrastructure.*` projects into `Abstractions/Options/`.

Move endpoint type-registration extension methods (`AddEndpointOptionsType`,
`AddOrganisationEntryType`) to `Abstractions` so any component can register
polymorphic config types without referencing `Infrastructure`.

### Step 3 — Move serialization to `Abstractions/Serialization/`

Move `PolymorphicEndpointOptionsConverter` and `AddMigrationPlatformPolymorphicSerializers()`
from `Infrastructure.Serialization` to `Abstractions/Serialization/`.

### Step 4 — Extract `IPreFlightValidator` to `Abstractions/Configuration/`

`QueueCommand` currently references `AzureDevOpsValidation` (concrete class in
`Infrastructure.AzureDevOps`). Extract `IPreFlightValidator` interface to
`Abstractions/Configuration/`. The implementation stays in `Infrastructure.AzureDevOps`
and is resolved via DI — the CLI never holds a direct reference.

### Step 5 — Remove concrete `ConfigurationService` usage from CLI commands

`QueueCommand`, `ConfigNewCommand`, `ConfigureCommand`, `PrepareCommand` all
`new` up `ConfigurationService` (a concrete class in `Infrastructure.Services`).
Register it via `IConfigurationService` from a CLI composition-root extension method.
The concrete class stays in `Infrastructure`.

### Step 6 — Move `TokenResolver` to `Abstractions/Options/`

Currently in `Utilities/`. It resolves config tokens (`{env:VAR}`) — a cross-cutting
config concern. Rename to `ConfigTokenResolver` for clarity.

### Step 7 — Remove all invalid project references

Delete from `CLI.Migration.csproj`:
```xml
<ProjectReference Include="..\DevOpsMigrationPlatform.ControlPlane\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.MigrationAgent\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\..." />
<ProjectReference Include="..\DevOpsMigrationPlatform.Infrastructure.Simulated\..." />
```

### Step 8 — Create `Abstractions.ControlPlane` and `Abstractions.Agent` projects

Split the files from `DevOpsMigrationPlatform.Abstractions` into the three projects
as specified above. Update all consuming projects to reference the correct subset.

---

## Notes

### `CLI.TfsMigration` is not a violation

`CLI.TfsMigration` IS the TFS Export Agent — it runs work item export inline, not via
ControlPlane. It correctly references `Abstractions` + `Abstractions.Agent` and directly
resolves `IWorkItemDiscoveryService`, `IProgressSink`, etc. This is by design.

### `Infrastructure` references `Abstractions.Agent`

`Infrastructure` implements Agent modules (`InventoryDiscoveryModule`,
`WorkItemExportOrchestrator`, etc.) and uses `IArtefactStore`, `ICheckpointingService`,
and other Agent-internal contracts. It correctly references `Abstractions` +
`Abstractions.Agent`.

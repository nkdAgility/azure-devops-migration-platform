# Infrastructure.Agent folder reorg (draft)

## Reorg policy (for review)

This draft follows the repository’s screaming-architecture and seam guardrails:

1. Folder names should express **business concern first** (for example `Identity`, `Teams`, `Nodes`, `WorkItems`, `Attachments`) rather than phase/mechanics names (`Import`, `Export`, `Discovery`, `Modules`, `Tools`).
2. Keep **one canonical seam per concern**; this is a file/folder organization change, not a seam duplication change.
3. Keep **contracts and behavior stable**; reorg is treated as **Class A** unless any public contract shape or ownership changes.
4. Keep **cross-cutting technical code** out of business folders and isolate it in one bounded platform area (`Platform/*`), while keeping business folders as the primary navigation path.
5. Maintain **adapter parity by concern** between `Infrastructure.AzureDevOps` and `Infrastructure.TfsObjectModel` so equivalent capabilities sit in equivalent folders.
6. Do not violate package/work item invariants; this draft changes naming/placement only, not runtime semantics.
7. Preserve existing lexicon and intent-revealing names; avoid generic buckets that hide ownership.
8. Lease ownership is explicit and consistent: place lease artifacts under `JobLifecycle/Lease`.
9. Folder/file moves are coupled to namespace alignment: moved types must adopt the new concern-based namespace, and all impacted `using`/type references must be updated in the same change.

## Namespace alignment policy (explicit)

- This reorg is **not** folder-only.
- Every moved `.cs` file is expected to move to a matching concern namespace.
- Namespace updates must be applied together with file moves (no mixed old/new concern namespaces left behind).
- All impacted references (`using`, fully qualified names, XML doc cref/type refs, test references) must be updated in the same change set.
- `global using` and DI/composition files must be reviewed for stale namespace imports after each move batch.
- Keep contract namespace intent clear in `Abstractions.Agent`; do not leave phase-first namespaces (`*.Export`, `*.Import`, `*.Discovery`, `*.Modules`, `*.Tools`) after concern migration is complete.

## Accepted naming decision (review outcome)

- We explicitly keep one generic top-level cross-cutting bucket: `Platform`.
- Rationale: these components are genuinely cross-cutting and cannot be owned by a single business concern (`WorkItems`, `Identity`, `Teams`, `Nodes`, `Attachments`).
- Constraint: `Platform` is a strict last-resort bucket for items that are genuinely cross-cutting across multiple business concerns.
- Constraint: if an item can be owned by a business concern, it must live under that concern (not under `Platform`).
- Constraint: `Platform` must not become a second place for business workflow logic.
- Configuration rule: only **cross-cutting configuration** may live under `Platform`.
- Configuration rule: if configuration is specific to a business concern, it must live under `<BusinessConcern>/Configuration` (for example `WorkItems/Configuration`, `Identity/Configuration`, `Teams/Configuration`, `Nodes/Configuration`, `Attachments/Configuration`).

### Platform placement test (mandatory)

Before placing any file under `Platform/*`, the plan must pass all checks:

1. The file is used by at least two business concerns.
2. The file does not encode a single concern workflow.
3. The file cannot be cleanly owned by `WorkItems`, `Identity`, `Teams`, `Nodes`, `Attachments`, `Inventory`, `Dependencies`, or `JobLifecycle`.
4. The reason is written inline in this move plan.

If any check fails, the file must move under the owning business concern.

## Scope

Project: `src/DevOpsMigrationPlatform.Infrastructure.Agent`

Goal: align folder naming to business concerns (screaming architecture), reducing phase/technical top-level buckets.

## Current top-level folders (latest scan)

`Analysis`, `Checkpointing`, `Connectors`, `Context`, `Discovery`, `Export`, `Identity`, `Import`, `Modules`, `Polyfills`, `ProjectLifecycle`, `Storage`, `Teams`, `Telemetry`, `Tools`, `Validation`, `WorkItems`

## Target top-level folders

```text
Attachments
Dependencies
Identity
Inventory
JobLifecycle
Nodes
ProjectLifecycle
Teams
WorkItems
Platform
```

### Capability rationale

- `Inventory` and `Dependencies` are treated here as first-class capabilities, not technical phase buckets.
- They own distinct domain outputs and contracts (inventory cataloguing vs dependency analysis), so they remain explicit top-level concerns in this draft.

## Top-level rename/move plan

| Current top-level | Action | Target |
|---|---|---|
| `Analysis` | Split by business concern | `Inventory` and `Dependencies` |
| `Checkpointing` | Move lifecycle concern | `JobLifecycle/Checkpointing` |
| `Connectors` | Split by concern | `Identity`, `Teams`, `WorkItems` |
| `Context` | Move lifecycle concern | `JobLifecycle/Context` |
| `Discovery` | Move concern-specific files out | `Inventory`, `Dependencies`, `Identity`, `Nodes`, `WorkItems` |
| `Export` | Remove phase bucket | Move to `WorkItems`, `Attachments`, `Platform/PackageState` |
| `Import` | Remove phase bucket | Move to `WorkItems`, `Nodes`, `Attachments` |
| `Modules` | Remove technical wrapper bucket | Move by concern (`Identity`, `Nodes`, `Teams`, `WorkItems`, `Dependencies`) |
| `Tools` | Remove technical bucket | Move by concern (`Identity`, `Nodes`, `WorkItems`) |
| `Storage` | Keep only truly cross-cutting state plumbing | `Platform/PackageState` |
| `Telemetry` | Keep only truly cross-cutting telemetry plumbing | `Platform/Telemetry` |
| `Validation` | Remove top-level cross-cutting bucket | `Platform/Validation` |
| `Polyfills` | Keep but demote from top level | `Platform/Polyfills` |

## Detailed mapping

### Discovery

- `Discovery/Inventory*` -> `Inventory/*`
- `Discovery/CatalogService.cs` -> `Inventory/CatalogService.cs`
- `Discovery/DependencyDiscoveryService.cs` -> `Dependencies/DependencyDiscoveryService.cs`
- `Discovery/DependencyGraph/*` -> `Dependencies/Graph/*`
- `Discovery/FileSystemIdentityMappingService.cs` -> `Identity/FileSystemIdentityMappingService.cs`
- `Discovery/QueryFingerprintService.cs` -> `WorkItems/QueryFingerprintService.cs` (if used by work item query windows)

### Export

- `Export/WorkItemExportOrchestrator*.cs` -> `WorkItems/`
- `Export/CompositeWorkItemRevisionSourceFactory.cs` -> `WorkItems/`
- `Export/EmbeddedImageExportService.cs` -> `Attachments/EmbeddedImageExportService.cs`
- `Export/ExportProgressStoreFactory.cs` + `Export/SqliteExportProgressStore.cs` -> `Platform/PackageState/`

### Import

- `Import/*WorkItem*` -> `WorkItems/WorkItemResolution/`
- `Import/*Node*` -> `Nodes/Import/`
- `Import/*Attachment*` and `Import/*EmbeddedImage*` -> `Attachments/Import/`
- `Import/FailurePatterns/*` -> `WorkItems/FailurePatterns/`
- `Import/Validators/*` -> concern-specific `*/Validation/` (prefer `WorkItems/Validation/` and `Nodes/Validation/`)

### Modules

- `Modules/Identities*` -> `Identity/`
- `Modules/Nodes*` + `Modules/NodeSourceTreeAddress.cs` -> `Nodes/`
- `Modules/Teams*` -> `Teams/`
- `Modules/WorkItemsModule.cs` -> `WorkItems/WorkItemsModule.cs`
- `Modules/Dependency*` -> `Dependencies/`
- `Modules/ModuleBase.cs` + `Modules/ModuleServiceCollectionExtensions.cs` -> `JobLifecycle/ModuleExecution/`

### Analysis, Checkpointing, Connectors, Context, Storage, Telemetry

- `Analysis/*` -> split into `Inventory/*` and `Dependencies/*` based on concern ownership
- `Checkpointing/*` -> `JobLifecycle/Checkpointing/*`
- `Connectors/CompositeIdentitySource.cs` -> `Identity/`
- `Connectors/CompositeTeamSource.cs` + `Connectors/CompositeTeamTarget.cs` -> `Teams/`
- `Connectors/CompositeWorkItemDiscoveryService.cs` -> `WorkItems/`
- `Connectors/FactoryRegistrationExtensions.cs` -> `JobLifecycle/ModuleExecution/FactoryRegistrationExtensions.cs` (or concern-specific registration files if split)
- `Context/*` -> `JobLifecycle/Context/*`
- `Storage/*` -> `Platform/PackageState/*` only when all Platform placement checks pass; otherwise move to owning concern
- `Telemetry/*` -> split by owner:
  - Work item telemetry -> `WorkItems/Telemetry/*`
  - Attachment telemetry -> `Attachments/Telemetry/*`
  - Job/control-plane telemetry orchestration -> `JobLifecycle/Telemetry/*`

### Root runtime files (explicit coverage)

- `AgentControlPlaneClientAdapter.cs` -> `JobLifecycle/ControlPlane/AgentControlPlaneClientAdapter.cs`
- `AgentWorkerBase.cs` -> `JobLifecycle/Execution/AgentWorkerBase.cs`
- `CoreAgentServiceExtensions.cs` -> `JobLifecycle/Composition/CoreAgentServiceExtensions.cs`
- `ModulePipelineWorkerBase.cs` -> `JobLifecycle/Execution/ModulePipelineWorkerBase.cs`
- `PackageConfigServiceCollectionExtensions.cs` -> `JobLifecycle/Configuration/PackageConfigServiceCollectionExtensions.cs`
- `GlobalUsings.cs` -> remains project root (no concern ownership change)

### Tools

- `Tools/IdentityLookup/*` -> `Identity/Lookup/*`
- `Tools/NodeTranslation/*` -> `Nodes/Translation/*`
- `Tools/FieldTransform/*` -> `WorkItems/FieldTransform/*`

### Validation and Polyfills

- `Validation/PackageValidator.cs` -> `Platform/Validation/PackageValidator.cs`
- `Polyfills/RequiredMemberAttribute.cs` -> `Platform/Polyfills/RequiredMemberAttribute.cs`
- `Lease/*` (if added) -> `JobLifecycle/Lease/`

## Keep as-is at top level

- `Identity`
- `JobLifecycle`
- `Nodes`
- `ProjectLifecycle`
- `Teams`
- `WorkItems`
- `Dependencies`
- `Inventory`
- `Attachments`

## Notes

- This reorg is a **Class A internal implementation change** if contract surfaces and behavior remain unchanged.
- No new parallel seams should be introduced; existing canonical seams remain the same.

---

# Abstractions.Agent folder reorg (draft)

## Scope

Project: `src/DevOpsMigrationPlatform.Abstractions.Agent`

Goal: same business-first naming model as Infrastructure.Agent, while keeping abstraction contracts stable.

## Current top-level folders (latest scan)

`Analysis`, `Attachments`, `Checkpointing`, `Context`, `Discovery`, `Export`, `Identity`, `Lease`, `Modules`, `Polyfills`, `ProjectLifecycle`, `Storage`, `Teams`, `Telemetry`, `Tools`, `Validation`, `WorkItems`

## Target top-level folders

```text
Attachments
Dependencies
Identity
Inventory
JobLifecycle
Nodes
ProjectLifecycle
Teams
WorkItems
Platform
```

## Top-level rename/move plan

| Current top-level | Action | Target |
|---|---|---|
| `Analysis` | Split by concern | `Inventory`, `Dependencies` |
| `Checkpointing` | Move lifecycle concern | `JobLifecycle/Checkpointing` |
| `Context` | Move lifecycle concern | `JobLifecycle/Context` |
| `Discovery` | Remove phase bucket | `Inventory`, `Dependencies`, `WorkItems` |
| `Export` | Remove phase bucket | `WorkItems`, `Attachments`, `JobLifecycle` |
| `Modules` | Remove technical wrapper bucket | `Identity`, `Nodes`, `Teams`, `WorkItems`, `Dependencies`, `JobLifecycle` |
| `Storage` | Keep only cross-cutting package/state contracts | `Platform/PackageState` |
| `Telemetry` | Keep only cross-cutting telemetry contracts | `Platform/Telemetry` |
| `Tools` | Remove technical bucket | `Identity`, `Nodes`, `WorkItems`, `Teams` |
| `Validation` | Remove top-level cross-cutting bucket | `Platform/Validation` |
| `Polyfills` | Demote from top level | `Platform/Polyfills` |
| `Lease` | Normalize ownership | `JobLifecycle/Lease` |

## Detailed mapping

### Discovery

- `Discovery/IInventory*`, `Discovery/InventoryProgressEvent.cs` -> `Inventory/*`
- `Discovery/IDependency*`, `Discovery/Dependency*.cs` -> `Dependencies/*`
- `Discovery/IWorkItemDiscoveryService.cs` -> `WorkItems/IWorkItemDiscoveryService.cs`
- `Discovery/DiscoveryContext.cs` -> `Inventory/DiscoveryContext.cs`
- `Discovery/ICatalogService.cs` -> `Inventory/ICatalogService.cs`

### Export

- `Export/IWorkItem*`, `Export/WorkItem*`, `Export/RevisionProcessResult.cs`, `Export/ExportContext.cs` -> `WorkItems/Revisions/*`
- `Export/IQueryFingerprintService.cs`, `Export/BatchContinuationToken.cs` -> `WorkItems/Revisions/*`
- `Export/IWorkItemCommentSource*.cs` -> `WorkItems/Comments/*`

### Modules

- `Modules/IIdentitiesOrchestrator.cs`, `Modules/IdentitiesModuleOptions.cs` -> `Identity/`
- `Modules/INodesOrchestrator.cs`, `Modules/NodesModuleOptions.cs` -> `Nodes/`
- `Modules/ITeamsOrchestrator.cs`, `Modules/TeamsModuleOptions.cs` -> `Teams/`
- `Modules/IWorkItemsOrchestrator.cs`, `Modules/WorkItemsModuleExtensions.cs` -> `WorkItems/`
- `Modules/IDependencyOrchestrator.cs`, `Modules/DependencyPhase.cs` -> `Dependencies/`
- `Modules/IModule.cs`, `Modules/ICapture.cs`, `Modules/PrepareContext.cs`, `Modules/PrepareReport.cs`, `Modules/TaskExecutionResult.cs`, `Modules/ModuleDependency.cs`, `Modules/InventoryContext.cs` -> `JobLifecycle/Execution/`

### Tools

- `Tools/IIdentitySource.cs`, `Tools/IIdentityLookupTool.cs` -> `Identity/Lookup/`
- `Tools/INodeTranslation*`, `Tools/IClassification*`, `Tools/ReferencedPathsArtifact.cs`, `Tools/PathTranslation.cs`, `Tools/ProjectMapping.cs`, `Tools/IterationNodeEntry.cs`, `Tools/UnmappedPathFinding.cs` -> `Nodes/Translation/`
- `Tools/IFieldTransform*`, `Tools/FieldTransform*`, `Tools/IExpressionEvaluator.cs`, `Tools/FieldDefinition.cs`, `Tools/IFieldDefinitionProvider.cs` -> `WorkItems/FieldTransform/`
- `Tools/ITeamSource.cs`, `Tools/ITeamTarget.cs` -> `Teams/`

### Validation, Polyfills, Lease

- `Validation/ValidationContext.cs` -> `Platform/Validation/ValidationContext.cs`
- `Polyfills/IsExternalInit.cs`, `Polyfills/RequiredMemberAttribute.cs` -> `Platform/Polyfills/`
- `Lease/ActiveLeaseState.cs` -> `JobLifecycle/Lease/ActiveLeaseState.cs`
- `Storage/*` -> `Platform/PackageState/*` only when all Platform placement checks pass; otherwise move to owning concern
- `Telemetry/*` -> split by owner:
  - Work item telemetry contracts -> `WorkItems/Telemetry/*`
  - Attachment telemetry contracts -> `Attachments/Telemetry/*`
  - Job lifecycle telemetry contracts -> `JobLifecycle/Telemetry/*`

### Root contract files (explicit coverage)

- `AgentInstanceIdHolder.cs` -> `JobLifecycle/Identity/AgentInstanceIdHolder.cs`

## Keep as-is at top level

- `Attachments`
- `Dependencies`
- `Identity`
- `Inventory`
- `JobLifecycle`
- `Nodes`
- `ProjectLifecycle`
- `Teams`
- `WorkItems`

## Notes

- This remains **Class A** while interface names and behavior stay unchanged (folder/file moves only).
- If any public contract names or seam ownership change, reclassify before implementation.

---

# Infrastructure.AzureDevOps folder reorg (draft)

## Scope

Project: `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps`

Goal: remove phase-first folders (`Discovery`, `Export`, `Import`) and organize by business concern.

## Current top-level folders (latest scan)

`Attachments`, `Discovery`, `Export`, `Factories`, `Import`, `ProjectLifecycle`

## Target top-level folders

```text
Attachments
Dependencies
Identity
Inventory
JobLifecycle
Nodes
ProjectLifecycle
Teams
WorkItems
Platform
```

### Capability rationale

- `Inventory` and `Dependencies` are treated here as first-class capabilities, not technical phase buckets.
- They own distinct domain outputs and contracts (inventory cataloguing vs dependency analysis), so they remain explicit top-level concerns in this draft.

## Top-level rename/move plan

| Current top-level | Action | Target |
|---|---|---|
| `Discovery` | Remove phase bucket | `Dependencies`, `Inventory`, `WorkItems` |
| `Export` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Import` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Factories` | Remove technical bucket | `Inventory`, `Dependencies` |

## Detailed mapping

### Discovery

- `Discovery/AzureDevOpsProjectDiscoveryService.cs` -> `Inventory/AzureDevOpsProjectDiscoveryService.cs`
- `Discovery/AzureDevOpsRepoDiscoveryService.cs` -> `Inventory/AzureDevOpsRepoDiscoveryService.cs`
- `Discovery/AzureDevOpsDependencyAnalysisService.cs` -> `Dependencies/AzureDevOpsDependencyAnalysisService.cs`
- `Discovery/AzureDevOpsWorkItemDiscoveryService.cs` -> `WorkItems/WorkItemResolution/AzureDevOpsWorkItemDiscoveryService.cs`

### Export

- `Export/AzureDevOpsWorkItemRevisionSource.cs` -> `WorkItems/Revisions/AzureDevOpsWorkItemRevisionSource.cs`
- `Export/AzureDevOpsWorkItemRevisionMapper.cs` -> `WorkItems/Revisions/AzureDevOpsWorkItemRevisionMapper.cs`
- `Export/AzureDevOpsWorkItemFetchService.cs` -> `WorkItems/Revisions/AzureDevOpsWorkItemFetchService.cs`
- `Export/AzureDevOpsWorkItemCommentSource*.cs` -> `WorkItems/Comments/`
- `Export/WorkItemQueryWindowStrategy.cs` -> `WorkItems/Revisions/WorkItemQueryWindowStrategy.cs`
- `Export/AzureDevOpsClassificationTreeReader.cs` -> `Nodes/AzureDevOpsClassificationTreeReader.cs`

### Import

- `Import/AzureDevOpsWorkItemTarget*.cs` -> `WorkItems/WorkItemResolution/`
- `Import/AzureDevOpsWorkItemTypeReadinessTarget*.cs` -> `WorkItems/WorkItemResolution/`
- `Import/AzureDevOpsResolutionStrategyFactory.cs` -> `WorkItems/WorkItemResolution/AzureDevOpsResolutionStrategyFactory.cs`
- `Import/TargetFieldResolutionStrategy.cs` + `Import/TargetHyperlinkResolutionStrategy.cs` -> `WorkItems/WorkItemResolution/`
- `Import/AzureDevOpsNodeCreator.cs` -> `Nodes/AzureDevOpsNodeCreator.cs`

### Root files and factories

- `AzureDevOpsTeamSource.cs`, `AzureDevOpsTeamTarget.cs` -> `Teams/`
- `AzureDevOpsIdentitySource.cs` -> `Identity/`
- `AzureDevOpsWorkItemRevisionSourceFactory.cs` -> `WorkItems/Revisions/`
- `AzureDevOpsWiqlQueryClientFactory.cs` -> `WorkItems/Revisions/`
- `Factories/InventoryServiceFactory.cs` -> `Inventory/InventoryServiceFactory.cs`
- `Factories/DependencyDiscoveryServiceFactory.cs` -> `Dependencies/DependencyDiscoveryServiceFactory.cs`
- `Attachments/*` remains in `Attachments/`
- `ProjectLifecycle/*` remains in `ProjectLifecycle/`
- `AzureDevOpsClientFactory.cs`, `IAzureDevOpsClientFactory.cs`, `AzureDevOpsRetryPolicy.cs` -> `Platform/AzureDevOpsAccess/`
- (Platform justification) these are connector access primitives shared across multiple AzureDevOps business concerns and cannot be owned by one concern without duplication.
- `InventoryServiceCollectionExtensions.cs` -> `Inventory/InventoryServiceCollectionExtensions.cs`
- `DependencyServiceCollectionExtensions.cs` -> `Dependencies/DependencyServiceCollectionExtensions.cs`
- `ExportServiceCollectionExtensions.cs` -> `WorkItems/Revisions/ExportServiceCollectionExtensions.cs`
- `ImportServiceCollectionExtensions.cs` -> `WorkItems/WorkItemResolution/ImportServiceCollectionExtensions.cs`
- If a service-collection file truly spans multiple concerns and cannot be split, keep it under `Platform/Composition/`.

## Keep as-is at top level

- `Attachments`
- `Dependencies`
- `Identity`
- `Inventory`
- `Nodes`
- `ProjectLifecycle`
- `Teams`
- `WorkItems`

## Notes

- This remains **Class A** if contract types and behavior do not change.
- Keep canonical seam ownership unchanged while moving files.

---

# Infrastructure.TfsObjectModel folder reorg (draft)

## Scope

Project: `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel`

Goal: same business-first shape as AzureDevOps adapter, replacing phase buckets with concern buckets.

## Current top-level folders (latest scan)

`Attachments`, `Discovery`, `Export`, `Extensions`, `Import`, `Models`, `Options`, `ProjectLifecycle`, `Telemetry`

## Target top-level folders

```text
Attachments
Dependencies
Identity
Inventory
JobLifecycle
Nodes
ProjectLifecycle
Teams
WorkItems
Platform
```

### Capability rationale

- `Inventory` and `Dependencies` are treated here as first-class capabilities, not technical phase buckets.
- They own distinct domain outputs and contracts (inventory cataloguing vs dependency analysis), so they remain explicit top-level concerns in this draft.

## Top-level rename/move plan

| Current top-level | Action | Target |
|---|---|---|
| `Discovery` | Remove phase bucket | `Dependencies`, `Inventory`, `WorkItems` |
| `Export` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Import` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Extensions` | Remove technical bucket | `Platform/TfsObjectModelExtensions` |
| `Options` | Remove technical bucket | `Platform/Configuration` |
| `Telemetry` | Split by owning concern | `WorkItems/Telemetry`, `Attachments/Telemetry`, `JobLifecycle/Telemetry` |
| `Models` | Remove generic bucket | place near owning concern |

## Detailed mapping

### Discovery

- `Discovery/TfsProjectDiscoveryService.cs` -> `Inventory/TfsProjectDiscoveryService.cs`
- `Discovery/TfsObjectModelWorkItemDiscoveryService.cs` -> `WorkItems/WorkItemResolution/TfsObjectModelWorkItemDiscoveryService.cs`

### Export

- `Export/TfsWorkItemRevisionSourceFactory.cs` -> `WorkItems/Revisions/`
- `Export/TfsActiveJobWorkItemRevisionSourceFactory.cs` -> `WorkItems/Revisions/`
- `Export/TfsWorkItemRevisionMapper.cs` -> `WorkItems/Revisions/`
- `Export/TfsWorkItemFetchService.cs` -> `WorkItems/Revisions/`
- `Export/TfsClassificationTreeCapture.cs` -> `Nodes/TfsClassificationTreeCapture.cs`

### Import

- `Import/TfsWorkItemTarget.cs` -> `WorkItems/WorkItemResolution/`
- `Import/TfsWorkItemTypeReadinessTarget.cs` -> `WorkItems/WorkItemResolution/`
- `Import/TfsResolutionStrategyFactory.cs` + `Import/TfsTargetFieldResolutionStrategy.cs` -> `WorkItems/WorkItemResolution/`
- `Import/TfsActiveJobWorkItemTargetFactory.cs` + `Import/TfsActiveJobWorkItemTypeReadinessTargetFactory.cs` -> `WorkItems/WorkItemResolution/`
- `Import/TfsNodeCreator.cs` + `Import/TfsActiveJobNodeCreator.cs` -> `Nodes/`

### Root files and technical support

- `TfsWorkItemRevisionSource.cs` -> `WorkItems/Revisions/TfsWorkItemRevisionSource.cs`
- `TfsWorkItemQueryWindowStrategy.cs` -> `WorkItems/Revisions/TfsWorkItemQueryWindowStrategy.cs`
- `TfsTeamSource.cs`, `TfsActiveJobTeamSource.cs` -> `Teams/`
- `TfsIdentitySource.cs`, `TfsActiveJobIdentitySource.cs` -> `Identity/`
- `TfsClassificationTreeReader.cs` -> `Nodes/`
- `TfsAttachmentBinarySource.cs`, `TfsAttachmentRegistry.cs`, `Attachments/TfsAttachmentDownloader.cs` -> `Attachments/`
- `ProjectLifecycle/*` remains in `ProjectLifecycle/`
- `Telemetry/WorkItemExportMetrics.cs` -> `WorkItems/Telemetry/WorkItemExportMetrics.cs`
- `Telemetry/AttachmentDownloadMetrics.cs` -> `Attachments/Telemetry/AttachmentDownloadMetrics.cs`
- `Telemetry/MigrationPlatformActivitySources.cs` -> `JobLifecycle/Telemetry/MigrationPlatformActivitySources.cs`
- `Options/*` -> `Platform/Configuration/` (cross-cutting connector configuration only)
- `Extensions/WorkItemStoreExtensions.cs` -> `WorkItems/Revisions/Extensions/WorkItemStoreExtensions.cs`
- `Models/WorkItemFromChunk.cs` -> `WorkItems/Revisions/Models/WorkItemFromChunk.cs`
- `ITfsJobServiceFactory.cs`, `TfsJobServiceFactory.cs`, `ActiveTfsJobServices.cs`, `MigrationPlatformHost.cs` -> `JobLifecycle/TfsExecution/`

## Keep as-is at top level

- `Attachments`
- `Dependencies`
- `Identity`
- `Inventory`
- `JobLifecycle`
- `Nodes`
- `ProjectLifecycle`
- `Teams`
- `WorkItems`

## Notes

- This remains **Class A** if contract/surface behavior is unchanged.
- Keep parallel structure with `Infrastructure.AzureDevOps` for the same concerns (Identity, Teams, Nodes, WorkItems, Attachments).

---

# Test impact assessment (required updates + renames)

This section captures what must change in tests when the folder/namespace reorg is implemented.

## Impact summary (latest scan)

- Tests with old `DevOpsMigrationPlatform.Abstractions.Agent.(Import|Export|Discovery|Modules|Tools|Validation|Lease)` references: **112 files / 178 matches**
- Tests with old `DevOpsMigrationPlatform.Infrastructure.Agent.(Import|Export|Discovery|Modules|Tools|Validation)` references: **84 files / 105 matches**
- Tests with old `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.(Import|Export|Discovery|Factories)` references: **8 files / 9 matches**
- Tests with old `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.(Import|Export|Discovery|Extensions|Options|Models|Telemetry)` references: **4 files / 4 matches**
- Reflection/contract tests with hard-coded fully-qualified names that must be renamed explicitly: **2 files**

## Tests that need updates (grouped by location)

### Abstractions-Agent namespace updates

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/*` (47 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/*` (22 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/*` (11 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/*` (8 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Services/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Prepare/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/TestUtilities/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/GlobalUsings.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/GlobalUsings.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DependencyDiscovery/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Import/*` (1 file)
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsNodeCreatorTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/Net481GuardedTypesContractTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TeamsNet481ContractTests.cs`

### Infrastructure-Agent namespace updates

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/*` (47 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/*` (12 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/*` (10 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/*` (3 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Services/*` (3 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Dependencies/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/*` (2 files)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/*` (1 file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Prepare/*` (1 file)
- `tests/DevOpsMigrationPlatform.SchemaGenerator.Tests/SchemaGeneratorHostTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/Net481GuardedTypesContractTests.cs`

### Explicit AzureDevOps adapter test files

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/ResumableBatchingCursorSteps.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/AzureDevOpsNodeCreatorTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/AzureDevOpsResolutionStrategyFactoryTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/AzureDevOpsWorkItemTargetTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/InventoryServiceTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs`

### Explicit TfsObjectModel adapter test files

- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsNodeCreatorTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsResolutionStrategyFactoryTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsWorkItemTargetTests.cs`

### Reflection/contract string rename files

- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/Net481GuardedTypesContractTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TeamsNet481ContractTests.cs`

## Required rename mappings (tests and test references)

Apply these renames in `using` statements, fully-qualified names, and string-based type references:

- `DevOpsMigrationPlatform.Abstractions.Agent.Export` -> concern namespace (`...WorkItems.Export`, `...Attachments.Export`, or `...JobLifecycle.*`) based on ownership
- `DevOpsMigrationPlatform.Abstractions.Agent.Discovery` -> `...Inventory` or `...Dependencies` (or concern-specific destination)
- `DevOpsMigrationPlatform.Abstractions.Agent.Modules` -> concern namespace (`...Identity`, `...Nodes`, `...Teams`, `...WorkItems`) or `...JobLifecycle.Execution` for runtime composition contracts
- `DevOpsMigrationPlatform.Abstractions.Agent.Tools` -> concern namespace (`...Identity.Lookup`, `...Nodes.Translation`, `...WorkItems.FieldTransform`)
- `DevOpsMigrationPlatform.Abstractions.Agent.Validation` -> concern validation namespace first; `...Platform.Validation` only when cross-cutting
- `DevOpsMigrationPlatform.Abstractions.Agent.Lease` -> `...JobLifecycle.Lease`

- `DevOpsMigrationPlatform.Infrastructure.Agent.Export` -> concern-owned namespace (`...WorkItems.Export`, `...Attachments.Export`, etc.)
- `DevOpsMigrationPlatform.Infrastructure.Agent.Import` -> concern-owned namespace (`...WorkItems.Import`, `...Nodes.Import`, `...Attachments.Import`)
- `DevOpsMigrationPlatform.Infrastructure.Agent.Discovery` -> `...Inventory` / `...Dependencies` / concern-owned destination
- `DevOpsMigrationPlatform.Infrastructure.Agent.Modules` -> concern namespace or `...JobLifecycle.Execution` when truly runtime composition
- `DevOpsMigrationPlatform.Infrastructure.Agent.Tools` -> concern namespace
- `DevOpsMigrationPlatform.Infrastructure.Agent.Validation` -> concern validation namespace first; `...Platform.Validation` only when cross-cutting

- `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery` -> `...Inventory` / `...Dependencies` / concern-owned destination
- `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export` -> concern-owned export namespace
- `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import` -> concern-owned import namespace
- `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories` -> concern-owned factories (or `Platform` only if cross-cutting across concerns)

- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Discovery` -> `...Inventory` / concern-owned destination
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export` -> concern-owned export namespace
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import` -> concern-owned import namespace
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Extensions` -> concern-owned extension namespace first (for example `...WorkItems.Export.Extensions`)
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options` -> concern-owned configuration first; `Platform.Configuration` only when truly cross-cutting
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Models` -> concern-owned model namespace
- `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry` -> concern telemetry namespace (`...WorkItems.Telemetry`, `...Attachments.Telemetry`, `...JobLifecycle.Telemetry`) per ownership

## Additional missing rename/impact checks (outside test files)

These impacts were found and must be included in implementation planning.

### 1) Additional source projects affected (missing from initial move scope)

- `src/DevOpsMigrationPlatform.Infrastructure.Simulated/*`
  - still references old `Abstractions.Agent.(Discovery|Export|Tools|Lease)` contracts and must be updated in lockstep.
- `src/DevOpsMigrationPlatform.MigrationAgent/*`
  - `MigrationAgentServiceExtensions.cs`, `JobAgentWorker.cs`, `GlobalUsings.cs` contain references to moved infrastructure/abstraction namespaces.
- `src/DevOpsMigrationPlatform.TfsMigrationAgent/*`
  - `TfsMigrationAgentServiceExtensions.cs`, `TfsJobAgentWorker.cs` contain moved namespace references.
- `src/DevOpsMigrationPlatform.SchemaGenerator/Program.cs`
  - imports moved namespace groups and will break after namespace migration.

### 2) String-based registrations and contract literals

These require explicit string updates (compile will not protect them):

- `src/DevOpsMigrationPlatform.Infrastructure/Serialization/EndpointOptionsTypeRegistry.cs`
- `src/DevOpsMigrationPlatform.Infrastructure/Serialization/EndpointOptionsRegistrationExtensions.cs`
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownActivitySourceNames.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/MigrationPlatformActivitySources.cs`
- Reflection tests:
  - `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/Net481GuardedTypesContractTests.cs`
  - `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TeamsNet481ContractTests.cs`

### 3) Spec/docs/contracts artifacts with old names

Specification artifacts currently referencing old namespace groups and needing sync updates:

- `specs/022-workitem-field-mapping/contracts/interfaces.md`
- `specs/023-workitems-nodestructure-tool/contracts/interfaces.md`
- `specs/029-import-workitems-attachments-nodes/spec.md`
- `specs/029-import-workitems-attachments-nodes/discrepancies.md`
- `specs/032-icapture-interface/spec.md`
- `specs/032-icapture-interface/plan.md`
- `specs/032-icapture-interface/quickstart.md`
- `specs/032-icapture-interface/data-model.md`
- `specs/032-icapture-interface/contracts/ICapture.md`

### 4) GlobalUsings and DI composition fallout

Must be reviewed and updated for moved namespaces in all touched projects and test projects:

- project `GlobalUsings.cs` files
- `*ServiceCollectionExtensions.cs`
- host/service registration files (`*MigrationAgentServiceExtensions.cs`, schema host wiring)

### 5) Missing rename-risk gate (required before completion)

Before declaring rename complete, verify:

1. No remaining references to old namespace groups in `src/`, `tests/`, and `specs/`.
2. No stale string-literal type/namespace references in registration/reflection files.
3. Global usings and DI registrations compile without alias/workaround shims.
4. Connector parity maintained across Simulated, AzureDevOps, and TfsObjectModel after rename.

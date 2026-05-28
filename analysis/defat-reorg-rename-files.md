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

- `Import/*WorkItem*` -> `WorkItems/Import/`
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
- `Storage/*` -> `Platform/PackageState/*` only when cross-cutting; otherwise move to owning concern
- `Telemetry/*` -> `Platform/Telemetry/*`

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

- `Export/IWorkItem*`, `Export/WorkItem*`, `Export/RevisionProcessResult.cs`, `Export/ExportContext.cs` -> `WorkItems/Export/*`
- `Export/IQueryFingerprintService.cs`, `Export/BatchContinuationToken.cs` -> `WorkItems/Export/*`
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
- `Storage/*` -> `Platform/PackageState/*` only when truly cross-cutting; otherwise move to owning concern
- `Telemetry/*` -> `Platform/Telemetry/*`

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
- `Discovery/AzureDevOpsWorkItemDiscoveryService.cs` -> `WorkItems/AzureDevOpsWorkItemDiscoveryService.cs`

### Export

- `Export/AzureDevOpsWorkItemRevisionSource.cs` -> `WorkItems/Export/AzureDevOpsWorkItemRevisionSource.cs`
- `Export/AzureDevOpsWorkItemRevisionMapper.cs` -> `WorkItems/Export/AzureDevOpsWorkItemRevisionMapper.cs`
- `Export/AzureDevOpsWorkItemFetchService.cs` -> `WorkItems/Export/AzureDevOpsWorkItemFetchService.cs`
- `Export/AzureDevOpsWorkItemCommentSource*.cs` -> `WorkItems/Comments/`
- `Export/WorkItemQueryWindowStrategy.cs` -> `WorkItems/Export/WorkItemQueryWindowStrategy.cs`
- `Export/AzureDevOpsClassificationTreeReader.cs` -> `Nodes/AzureDevOpsClassificationTreeReader.cs`

### Import

- `Import/AzureDevOpsWorkItemTarget*.cs` -> `WorkItems/Import/`
- `Import/AzureDevOpsWorkItemTypeReadinessTarget*.cs` -> `WorkItems/Import/`
- `Import/AzureDevOpsResolutionStrategyFactory.cs` -> `WorkItems/Import/AzureDevOpsResolutionStrategyFactory.cs`
- `Import/TargetFieldResolutionStrategy.cs` + `Import/TargetHyperlinkResolutionStrategy.cs` -> `WorkItems/Import/`
- `Import/AzureDevOpsNodeCreator.cs` -> `Nodes/AzureDevOpsNodeCreator.cs`

### Root files and factories

- `AzureDevOpsTeamSource.cs`, `AzureDevOpsTeamTarget.cs` -> `Teams/`
- `AzureDevOpsIdentitySource.cs` -> `Identity/`
- `AzureDevOpsWorkItemRevisionSourceFactory.cs` -> `WorkItems/Export/`
- `AzureDevOpsWiqlQueryClientFactory.cs` -> `WorkItems/Export/`
- `Factories/InventoryServiceFactory.cs` -> `Inventory/InventoryServiceFactory.cs`
- `Factories/DependencyDiscoveryServiceFactory.cs` -> `Dependencies/DependencyDiscoveryServiceFactory.cs`
- `Attachments/*` remains in `Attachments/`
- `ProjectLifecycle/*` remains in `ProjectLifecycle/`
- `AzureDevOpsClientFactory.cs`, `IAzureDevOpsClientFactory.cs`, `AzureDevOpsRetryPolicy.cs` -> `Platform/AzureDevOpsAccess/`
- `InventoryServiceCollectionExtensions.cs` -> `Inventory/InventoryServiceCollectionExtensions.cs`
- `DependencyServiceCollectionExtensions.cs` -> `Dependencies/DependencyServiceCollectionExtensions.cs`
- `ExportServiceCollectionExtensions.cs` -> `WorkItems/Export/ExportServiceCollectionExtensions.cs`
- `ImportServiceCollectionExtensions.cs` -> `WorkItems/Import/ImportServiceCollectionExtensions.cs`
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
Telemetry
WorkItems
Platform
```

## Top-level rename/move plan

| Current top-level | Action | Target |
|---|---|---|
| `Discovery` | Remove phase bucket | `Dependencies`, `Inventory`, `WorkItems` |
| `Export` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Import` | Remove phase bucket | `WorkItems`, `Nodes` |
| `Extensions` | Remove technical bucket | `Platform/TfsObjectModelExtensions` |
| `Options` | Remove technical bucket | `Platform/Configuration` |
| `Models` | Remove generic bucket | place near owning concern |

## Detailed mapping

### Discovery

- `Discovery/TfsProjectDiscoveryService.cs` -> `Inventory/TfsProjectDiscoveryService.cs`
- `Discovery/TfsObjectModelWorkItemDiscoveryService.cs` -> `WorkItems/TfsObjectModelWorkItemDiscoveryService.cs`

### Export

- `Export/TfsWorkItemRevisionSourceFactory.cs` -> `WorkItems/Export/`
- `Export/TfsActiveJobWorkItemRevisionSourceFactory.cs` -> `WorkItems/Export/`
- `Export/TfsWorkItemRevisionMapper.cs` -> `WorkItems/Export/`
- `Export/TfsWorkItemFetchService.cs` -> `WorkItems/Export/`
- `Export/TfsClassificationTreeCapture.cs` -> `Nodes/TfsClassificationTreeCapture.cs`

### Import

- `Import/TfsWorkItemTarget.cs` -> `WorkItems/Import/`
- `Import/TfsWorkItemTypeReadinessTarget.cs` -> `WorkItems/Import/`
- `Import/TfsResolutionStrategyFactory.cs` + `Import/TfsTargetFieldResolutionStrategy.cs` -> `WorkItems/Import/`
- `Import/TfsActiveJobWorkItemTargetFactory.cs` + `Import/TfsActiveJobWorkItemTypeReadinessTargetFactory.cs` -> `WorkItems/Import/`
- `Import/TfsNodeCreator.cs` + `Import/TfsActiveJobNodeCreator.cs` -> `Nodes/`

### Root files and technical support

- `TfsWorkItemRevisionSource.cs` -> `WorkItems/Export/TfsWorkItemRevisionSource.cs`
- `TfsWorkItemQueryWindowStrategy.cs` -> `WorkItems/Export/TfsWorkItemQueryWindowStrategy.cs`
- `TfsTeamSource.cs`, `TfsActiveJobTeamSource.cs` -> `Teams/`
- `TfsIdentitySource.cs`, `TfsActiveJobIdentitySource.cs` -> `Identity/`
- `TfsClassificationTreeReader.cs` -> `Nodes/`
- `TfsAttachmentBinarySource.cs`, `TfsAttachmentRegistry.cs`, `Attachments/TfsAttachmentDownloader.cs` -> `Attachments/`
- `ProjectLifecycle/*` remains in `ProjectLifecycle/`
- `Telemetry/*` remains in `Telemetry/`
- `Options/*` -> `Platform/Configuration/` (cross-cutting connector configuration only)
- `Extensions/WorkItemStoreExtensions.cs` -> `WorkItems/Export/Extensions/WorkItemStoreExtensions.cs`
- `Models/WorkItemFromChunk.cs` -> `WorkItems/Export/Models/WorkItemFromChunk.cs`
- `ITfsJobServiceFactory.cs`, `TfsJobServiceFactory.cs`, `ActiveTfsJobServices.cs`, `MigrationPlatformHost.cs` -> `JobLifecycle/TfsRuntime/`

## Keep as-is at top level

- `Attachments`
- `Dependencies`
- `Identity`
- `Inventory`
- `JobLifecycle`
- `Nodes`
- `ProjectLifecycle`
- `Teams`
- `Telemetry`
- `WorkItems`

## Notes

- This remains **Class A** if contract/surface behavior is unchanged.
- Keep parallel structure with `Infrastructure.AzureDevOps` for the same concerns (Identity, Teams, Nodes, WorkItems, Attachments).

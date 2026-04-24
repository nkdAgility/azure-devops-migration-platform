# Module Architecture

## 7. Module Architecture

Each migration concern is implemented as a module conforming to the `IModule` contract. Modules are the only extension point for adding new capabilities.

### IModule Contract

```csharp
interface IModule
{
    string Name { get; }
    IReadOnlyList<string> DependsOn { get; }

    Task ExportAsync(ExportContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

### Contract Invariants

- `Name` is unique across all registered modules.
- `DependsOn` declares ordering constraints. The orchestrator resolves the dependency graph before execution; circular dependencies are a fatal configuration error.
- `ExportAsync` must write only via `IArtefactStore`.
- `ImportAsync` must read only via `IArtefactStore` and write state only via `IStateStore`.
- `ValidateAsync` must be side-effect free.
- Modules must never call source or target APIs directly — only through injected services.

### Dependency Graph Rules

- Dependencies are resolved topologically before execution begins.
- A module that depends on another module will not execute until the dependency completes successfully.
- Modules with no declared dependencies may execute in any order (or in parallel, if the orchestrator supports it in a future version).
- `IdentitiesModule` has no dependencies (`DependsOn` is empty) but must complete before any module that performs identity mapping. Any module that maps identities must include `"IdentitiesModule"` in its own `DependsOn` list. Failure to do so is a dependency graph error that the orchestrator must detect and reject at startup.

### Storage Rule

> Modules only use `IArtefactStore` and `IStateStore`. Direct filesystem access outside of these interfaces is forbidden.

### Module Responsibilities

| Module | Responsibility |
|---|---|
| `WorkItemsModule` | High-fidelity work item revision export/import. Accepts a `wiql` scope (with `query` parameter) and one or more `filter` scopes (with `mode`, `field`, and `pattern` parameters) to include or exclude work items by field value using a case-insensitive regex. Also accepts five independently-enabled named extensions: `Revisions`, `Links`, `Attachments`, `Comments` (fetches comment versions from the ADO Comments API), and `EmbeddedImages` (downloads and rewrites inline images from HTML/Markdown fields). |
| `IdentitiesModule` | Export user/group descriptors; provide identity mapping service to all other modules |
| `TeamsModule` | Export and import team membership and settings |
| `PermissionsModule` | Export and import project and repository access control lists |
| `BuildsModule` | Export build pipeline definitions |
| `GitModule` | Export Git repository structure and optionally pack contents |

> **Field-projected fetching**: Inventory and dependency analysis modules use `IWorkItemFetchService` for streaming, field-projected work item retrieval. This abstraction handles WIQL windowing, batch API calls, and in-process filtering — modules should not call `GetWorkItemsAsync` directly.

### WorkItemsModule — ADO Export

The Azure DevOps export path uses the following components:

| Component | Role |
|---|---|
| `IWorkItemRevisionSourceFactory` | Creates an `IWorkItemRevisionSource` per job from endpoint options. ADO implementation: `AzureDevOpsWorkItemRevisionSourceFactory`. |
| `AzureDevOpsWorkItemRevisionSource` | Enumerates work item revisions from the REST API using `WorkItemQueryWindowStrategy` for WIQL-windowed iteration. |
| `IAttachmentBinarySource` / `IStreamingAttachmentBinarySource` | Downloads attachment binaries. ADO implementation: `AzureDevOpsAttachmentBinarySource`. Streaming variant computes SHA-256 in-flight via `CryptoStream`. |
| `AzureDevOpsAttachmentRegistry` | Scoped registry mapping (workItemId, revisionIndex, filename) → download URL, populated during revision enumeration. |
| `WorkItemExportOrchestrator` | Drives the export loop: enumerate revisions → write `revision.json` → download attachments (with delta detection) → advance cursor. O(N) calls per revision. |

**Resilience**: Attachment downloads use a named HTTP client (`"AttachmentDownload"`) with 8 retries, exponential back-off, handling transient 5xx, 408, and 429 responses.

**Delta detection**: Adjacent revisions sharing the same attachment URL skip re-download — only new or changed URLs trigger a binary fetch.

### Adding a New Module

See [.agents/guardrails/module-template.md](../.agents/guardrails/module-template.md) for the full checklist.

> **Naming convention**: modules are named by *domain* (`WorkItems`, `Identities`, `Teams`, `Git`), not by operation. One module handles both export and import for its domain. `Scopes` are mandatory selection criteria (e.g. a `wiql` scope for WorkItems). The `Extensions` array controls which sub-data is collected.

### Discovery Modules

Discovery modules implement `IDiscoveryModule` and run pre-migration analysis:

| Module | Responsibility |
|---|---|
| `InventoryDiscoveryModule` | Counts work items and revisions per project; writes `inventory.csv` and `inventory.json`. |
| `DependencyDiscoveryModule` | Analyses cross-project and cross-organisation work item links; writes `dependencies.csv`. |

Discovery modules follow the **delegation pattern**: they orchestrate checkpointing, progress reporting, and artefact writing, while the actual API interaction is delegated to injected services (e.g., `IInventoryService`, `IDependencyDiscoveryService`) created via factories. This keeps modules testable and connector-agnostic.

### Module Registration

Module registrations belong at the **composition root** (`ModuleServiceCollectionExtensions`), not inside connector assemblies. Connector files (e.g., `ExportServiceCollectionExtensions`) only register connector-specific services (factories, HTTP clients, SDK adapters). This ensures connectors are decoupled from module implementations.

### Discovery Utility Namespace

The `Infrastructure.Modules.Discovery` namespace contains **utility types** used by `DependencyDiscoveryModule` for graph analysis:

- `TransitiveDependencyWalker` — walks the transitive closure of project dependencies.
- `UnionFindComponentLabeler` — labels connected components using union-find.
- `MermaidDiagramBuilder` / `MermaidUtilities` — generates Mermaid visualisation output.
- `ProjectDependencyRecord` / `ProjectPairKey` — data records for dependency edges.

> **Important**: These are NOT `IModule` or `IDiscoveryModule` implementations. They are internal utilities consumed only by `DependencyDiscoveryModule`.

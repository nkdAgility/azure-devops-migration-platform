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
    Task PrepareAsync(PrepareContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

### Contract Invariants

- `Name` is unique across all registered modules.
- `DependsOn` declares ordering constraints. The orchestrator resolves the dependency graph before execution; circular dependencies are a fatal configuration error.
- `ExportAsync` must write only via `IArtefactStore`. Reads from the source system via injected services.
- `PrepareAsync` must read from the package via `IArtefactStore`, query the target system via injected services, and write validation/mapping artefacts into the module's own package folder (e.g. `Identities/prepare-report.json`). Prepare artefacts are overwritten on re-run. Operator-edited mapping files (e.g. `mapping.json`) must not be modified by `PrepareAsync`.
- `ImportAsync` must read only via `IArtefactStore` and write state only via `IStateStore`.
- `ValidateAsync` must be side-effect free.
- Modules must never call source or target APIs directly — only through injected services.

### Dependency Graph Rules

- Dependencies are resolved topologically before execution begins.
- A module that depends on another module will not execute until the dependency completes successfully.
- Modules with no declared dependencies may execute in any order (or in parallel, if the orchestrator supports it in a future version).
- `IdentitiesModule` has no dependencies (`DependsOn` is empty) but must complete before any module that performs identity mapping. Any module that maps identities must include `"IdentitiesModule"` in its own `DependsOn` list. Failure to do so is a dependency graph error that the orchestrator must detect and reject at startup.
- `TeamsModule` should be ordered after `IdentitiesModule` and `NodesModule`, and before `WorkItemsModule`. Module execution order is controlled by the operator via configuration — there is no `DependsOn` property on TeamsModule or NodesModule. The operator must ensure prerequisite modules complete before dependent modules run.

### Storage Rule

> Modules only use `IArtefactStore` and `IStateStore`. Direct filesystem access outside of these interfaces is forbidden.

### Module Responsibilities

| Module | Responsibility |
|---|---|
| `WorkItemsModule` | High-fidelity work item revision export/import. **Prepare**: cross-references exported field names with configured `FieldTranslations` and reports unmapped fields; validates all referenced area/iteration paths exist on the target (via `INodeCreator.NodeExistsAsync`) and writes `Nodes/prepare-report.json`. Accepts a `wiql` scope (with `query` parameter) and one or more `filter` scopes (with `mode`, `field`, and `pattern` parameters) to include or exclude work items by field value using a case-insensitive regex. Also accepts five independently-enabled named extensions: `Revisions`, `Links`, `Attachments`, `Comments` (fetches comment versions from the ADO Comments API), and `EmbeddedImages` (downloads and rewrites inline images from HTML/Markdown fields). |
| `IdentitiesModule` | Export user/group descriptors; provide identity mapping service to all other modules. **Prepare**: reads `descriptors.jsonl`, queries the target for matching identities (by UPN/display name), writes `Identities/prepare-report.json` with auto-matched and unresolved identities. |
| `TeamsModule` | Export and import team membership and settings. **Prepare**: verifies target teams/groups exist or can be created; writes `Teams/prepare-report.json`. |
| `PermissionsModule` | Export and import project and repository access control lists. **Prepare**: verifies target ACL structure compatibility; writes `Permissions/prepare-report.json`. |
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

### Tool Resolution

Tools are declared in `MigrationPlatform.Tools.*` as shared singletons at the config root. Extensions load tools by key name at startup; the effective settings equal the singleton tool config merged with any phase-level overrides declared in the extension reference. Tools are pure transformations or lookup services — they perform no I/O and carry no mutable state.

Available tools:

| Tool | Key | Purpose |
|---|---|---|
| `FieldTransformTool` | `FieldTransform` | Applies declared field transformation rules (copy, map, replace, etc.) to each work item revision. |
| `NodeTranslationTool` | `NodeTranslation` | Translates and validates area/iteration classification node paths. Supports regex-based path mappings, localised root-name normalisation, source-tree replication, and auto-creation of missing nodes on the target. |

For the full tool schema and available tool types, see [docs/configuration.md — Tools](configuration.md#tools).

### IdentitiesModule

| Property | Value |
|---|---|
| **Name** | `Identities` |
| **DependsOn** | *(none — runs first)* |
| **Package folder** | `Identities/` |
| **Cursor** | `.migration/Checkpoints/identities.cursor.json` |

**Behaviour:**
- `ExportAsync`: streams all user and group identity descriptors from the source via `IIdentitySource`. Writes one descriptor per line to `Identities/descriptors.jsonl` (JSONL format). Emits `migration.identities.export.count` metric.
- `ImportAsync`: reads `Identities/descriptors.jsonl`. If `Identities/mapping.json` exists, loads explicit overrides. Populates `IIdentityMappingService` singleton so all downstream modules can call `Resolve()`. Writes `Identities/unresolved.json` for identities that could not be matched.
- `ValidateAsync`: checks `Identities/descriptors.jsonl` exists and is valid JSONL. Reports missing required fields as validation errors.

**Configuration section**: `MigrationPlatform:Modules:Identities`

```json
{
  "name": "Identities",
  "enabled": true,
  "defaultIdentity": "migration-service@contoso.com"
}
```

**Cross-cutting service**: `IIdentityMappingService` is a singleton populated during `ImportAsync`. All modules requiring identity resolution inject this service.

---

### NodesModule

| Property | Value |
|---|---|
| **Name** | `Nodes` |
| **DependsOn** | *(none)* |
| **Package folder** | `Nodes/` |
| **Cursor** | `.migration/Checkpoints/nodes.cursor.json` |

**Behaviour:**
- `ExportAsync`: delegates to `IClassificationTreeCapture.CaptureAsync()` — writes `Nodes/source-tree.json` with the full area/iteration tree from the source.
- `ImportAsync`: if `ReplicateSourceTree` is enabled, delegates to `INodeEnsurer.ReplicateSourceTreeAsync()`. If `AutoCreateNodes` is enabled, also calls `INodeEnsurer.EnsureReferencedPathsAsync()` using `Nodes/referenced-paths.json`. Writes `nodes.cursor.json` after completion.
- `ValidateAsync`: delegates to `INodeTranslationValidator.ValidateAsync()`.

**Configuration section**: `MigrationPlatform:Modules:Nodes`

```json
{
  "name": "Nodes",
  "enabled": true,
  "replicateSourceTree": true,
  "autoCreateNodes": true
}
```

---

### TeamsModule

| Property | Value |
|---|---|
| **Name** | `Teams` |
| **DependsOn** | *(none — order is operator-controlled)* |
| **Package folder** | `Teams/` |
| **Cursor** | `.migration/Checkpoints/teams.cursor.json` |

**Recommended execution order**: After `IdentitiesModule` and `NodesModule`, before `WorkItemsModule`.

**Scope types:**
- `"all"` (default) — exports all teams in the project.
- `"teams"` — exports only teams matching the optional `filter` pattern (case-insensitive regex on team name).

**Extensions** (all enabled by default):
| Extension | Description |
|---|---|
| `TeamSettings` | Board configuration, backlog navigation level, bugs behaviour, working days |
| `TeamIterations` | Sprint/iteration assignments including default and backlog iterations |
| `TeamMembers` | Team membership with admin flags; uses `IIdentityMappingService` for identity resolution |
| `TeamCapacity` | Per-member capacity per sprint; uses `INodeTranslationTool` for iteration path translation |
| `NodeTranslation` | Records team area/iteration paths into `IReferencedPathTracker` during export |

**Behaviour:**
- `ExportAsync`: enumerates teams via `ITeamSource`. For each team, writes `Teams/{team-slug}/team.json` (settings, iterations, members, capacity, area paths). Writes `teams.cursor.json` after each team. Supports scope/filter.
- `ImportAsync`: reads team files, creates/updates via `ITeamTarget`. Uses `INodeTranslationTool` for path translation. Uses `IIdentityMappingService` for member mapping.
- `ValidateAsync`: validates `Teams/` folder structure and JSON file integrity.

**Configuration section**: `MigrationPlatform:Modules:Teams`

```json
{
  "name": "Teams",
  "enabled": true,
  "scope": "all",
  "filter": "",
  "extensions": {
    "teamSettings": true,
    "nodeStructure": true,
    "teamIterations": true,
    "teamMembers": true,
    "teamCapacity": true
  }
}
```

**Package layout:**
```
Teams/
  {team-slug}/
    team.json   ← definition, settings, iterations, members, capacity, area paths
```

Team slugs are generated from the team display name: lowercase, spaces → hyphens, special characters stripped.

---

### Module Registration

Module registrations belong at the **composition root** (`ModuleServiceCollectionExtensions`), not inside connector assemblies. Connector files (e.g., `ExportServiceCollectionExtensions`) only register connector-specific services (factories, HTTP clients, SDK adapters). This ensures connectors are decoupled from module implementations.

### Discovery Utility Namespace

The `Infrastructure.Modules.Discovery` namespace contains **utility types** used by `DependencyDiscoveryModule` for graph analysis:

- `TransitiveDependencyWalker` — walks the transitive closure of project dependencies.
- `UnionFindComponentLabeler` — labels connected components using union-find.
- `MermaidDiagramBuilder` / `MermaidUtilities` — generates Mermaid visualisation output.
- `ProjectDependencyRecord` / `ProjectPairKey` — data records for dependency edges.

> **Important**: These are NOT `IModule` or `IDiscoveryModule` implementations. They are internal utilities consumed only by `DependencyDiscoveryModule`.

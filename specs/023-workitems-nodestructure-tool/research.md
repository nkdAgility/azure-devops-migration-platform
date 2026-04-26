# Research — NodeStructure Tool

**Feature**: 023-workitems-nodestructure-tool  
**Date**: 2026-04-26

---

## R-001: Tool Purity vs I/O — Architecture Tension

### Question
`docs/modules.md` states tools are "pure transformations or lookup services — they perform no I/O and carry no mutable state." The NodeStructureTool needs to call the ADO Classification Nodes API (creating nodes) and write to `IStateStore` (node-creation checkpoint). This violates the stated tool contract.

### Decision
Split the NodeStructureTool into two concerns:

1. **Path Mapping (pure)** — `INodeStructureTool` interface exposes a pure method `TranslatePath(field, value, context) → PathTranslation` that applies language override, exact-match mapping, and auto-swap. This is stateless and performs no I/O. It mirrors `IFieldTransformTool.ApplyTransforms()`.

2. **Node Ensurer (I/O)** — `INodeCreator` is a separate abstraction that handles node existence checks, creation via ADO API, checkpointing via `IStateStore`, and retry logic. This is consumed at the orchestration layer (before revision processing begins), not inside the per-revision tool call.

This split preserves the tool purity contract while enabling the I/O-heavy node creation to live in the infrastructure layer where it belongs.

### Rationale
- Tools called inside `RevisionFolderProcessor` must be fast and deterministic (called once per revision per field). ADO API calls inside this hot path would be a performance and reliability disaster.
- FR-024 already requires pre-collection of all paths before revision processing — the node creation naturally belongs in this pre-processing step, not in the per-revision loop.
- The pure path-mapping interface can be tested without mocks for HTTP clients or state stores.

### Alternatives Rejected
- **Single fat interface**: Rejected because it conflates pure transformation with I/O, violates the documented tool contract, and makes the per-revision call path non-deterministic.
- **Amend the tool contract in docs/modules.md**: Rejected because the purity constraint is architecturally sound — the problem is the impl approach, not the constraint.

---

## R-002: INodeStructureTool Interface Design for Dual Consumers

### Question
The tool must serve both `WorkItemsModule` (import-time path remapping on `System.AreaPath`/`System.IterationPath`) and `TeamsModule` (future — team area/iteration path references). How should the interface accommodate both?

### Decision
The `INodeStructureTool` interface operates on individual path values, not on revision-level field dictionaries:

```csharp
public interface INodeStructureTool
{
    PathTranslation TranslatePath(string fieldName, string sourcePathValue, ProjectMapping context);
    bool IsEnabled { get; }
}
```

Where `ProjectMapping` carries:
- `SourceProjectName` (string)
- `TargetProjectName` (string)

Both `WorkItemsModule` and `TeamsModule` can call `TranslatePath()` with the relevant field name and path value. The tool doesn't need to know which module is calling.

### Rationale
- `TeamsModule` references area/iteration paths in team settings (default area, iteration backlog paths). The same mapping logic applies.
- By operating on a single path value (not a field dictionary), the interface is maximally reusable.
- `WorkItemsModule` calls it for `System.AreaPath` and `System.IterationPath` fields from `revision.json`.
- `TeamsModule` calls it for team area/iteration path settings.

---

## R-003: ADO Classification Nodes REST API Contract

### Question
What are the exact API endpoints, request/response shapes, and error codes for the ADO Classification Nodes API?

### Decision
The ADO REST API for classification nodes:

- **GET** `/{org}/{project}/_apis/wit/classificationnodes/{structureGroup}/{path}?api-version=7.1` — Check if a node exists. Returns 200 with node data, or 404 if not found.
- **POST** `/{org}/{project}/_apis/wit/classificationnodes/{structureGroup}/{parentPath}?api-version=7.1` — Create a child node. Body: `{ "name": "NodeName" }`. Returns 201 on success, 409 if already exists.
- **PATCH** `/{org}/{project}/_apis/wit/classificationnodes/{structureGroup}/{path}?api-version=7.1` — Update a node (set dates). Body: `{ "attributes": { "startDate": "...", "finishDate": "..." } }`.

Where `{structureGroup}` is `areas` or `iterations`.

Error codes:
- 200/201: Success
- 400: Invalid path or name (non-retryable)
- 401/403: Auth failure (non-retryable, fatal)
- 404: Node not found (expected for existence check)
- 408/429/5xx: Transient (retryable with back-off)
- 409: Conflict / already exists (treat as success)

### Rationale
Documented in ADO REST API reference. The interface `INodeCreator` wraps these calls behind an abstraction per guardrail rule 12 (SDK calls behind abstractions).

---

## R-004: Pre-Collection Pass Integration

### Question
FR-024 requires collecting all distinct `System.AreaPath`/`System.IterationPath` values from the package before import. Where does this fit in the import pipeline?

### Decision
The pre-collection pass is a dedicated step in the `WorkItemsModule.ImportAsync()` flow, executed **after** `replicateAllExistingNodes` bulk replication (if enabled) and **before** the `RevisionFolderProcessor` loop:

1. `ReplicateSourceTree` — Read `Nodes/source-tree.json`, create nodes (if flag enabled)
2. **Pre-collection** — Read `Nodes/referenced-paths.json` if present; otherwise enumerate all revision folders, extract `System.AreaPath`/`System.IterationPath` from each `revision.json`, apply `INodeStructureTool.TranslatePath()` to each, collect distinct translated paths
3. **Bulk node creation** — For each distinct translated path, call `INodeCreator.EnsureExistsAsync()` (if `AutoCreateNodes: true`)
4. **Revision processing loop** — Standard streaming import with `RevisionFolderProcessor`

The pre-collection pass uses `Nodes/referenced-paths.json` (fast path) or `IArtefactStore.EnumerateAsync()` fallback, and reads each `revision.json` once (streaming — does not buffer all revisions). Only the distinct path strings are collected in memory (bounded set — a project typically has tens to hundreds of distinct paths, not millions).

### Rationale
- Separates the pre-scan (read) from the creation (write) phases cleanly.
- The distinct path set is bounded and small relative to revision count.
- Avoids mid-stream ADO API calls during the revision write loop.

---

## R-005: Export-Side Integration Point

### Question
Where does the `classification-nodes.json` export artifact get written?

### Decision
The export artifact is written by a new `ClassificationTreeCapture` service called from `WorkItemsModule.ExportAsync()` at the beginning of export (before work item revision export). It:

1. Calls the source ADO Classification Nodes API (via `IClassificationTreeReader`) to enumerate area and iteration trees.
2. Writes the `Nodes/source-tree.json` artifact via `IArtefactStore.WriteJsonAsync()`.

This runs on **every** export regardless of configuration flags — the source tree is always captured as package metadata.

Additionally, a `ReferencedPathTracker` maintains the in-memory set of discovered paths during revision export and writes `Nodes/referenced-paths.json` incrementally.

`IClassificationTreeReader` is the export-side abstraction (mirrors the `IWorkItemRevisionSource` pattern). It lives in `Abstractions.Agent`.

### Rationale
- Follows the established export pattern: source abstraction → artefact store write.
- Writing at the start of export ensures the artifact is available even if work item export is interrupted.
- The source API call is permitted in export context per guardrail rule 6 (export reads from source, import reads from package).

---

## R-006: IClassificationNodeService — Node Creation Abstraction

### Question
What abstraction wraps the target ADO Classification Nodes API for node creation at import time?

### Decision
A new `INodeCreator` interface in `Abstractions.Agent`:

```csharp
public interface INodeCreator
{
    Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct);
    Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct);
    Task SetIterationDatesAsync(string path, DateTimeOffset? startDate, DateTimeOffset? finishDate, CancellationToken ct);
}
```

Where `ClassificationNodeType` is an enum: `Area`, `Iteration`.

The implementation (`AzureDevOpsNodeCreator`) in `Infrastructure.Agent` wraps the ADO REST API calls with:
- Idempotent check-before-POST (FR-007)
- Ancestor-first creation for nested paths (FR-008)
- Exponential back-off retry on transient errors (FR-022)
- Fatal fail on 401/403 (FR-022)

### Rationale
- Keeps ADO SDK/REST calls behind an abstraction per guardrail rule 12.
- Testable with mocks in unit tests.
- Separates node creation concerns from path mapping concerns.

---

## R-007: Checkpoint Strategy for Node Replication

### Question
How does the node-creation checkpoint work for `ReplicateSourceTree` resumability?

### Decision
A `NodeReplicationProgress` record is stored in `IStateStore` under the key `nodestructure-nodes-confirmed`. It holds a `HashSet<string>` of node paths (case-insensitive) that have been confirmed present in the target during the current import run.

On resume:
1. Read the checkpoint from `IStateStore`.
2. For each node in `Nodes/source-tree.json`, skip if the path is in the confirmed set.
3. After confirming/creating each node, add its path to the checkpoint and persist.

The checkpoint is persisted after each node to survive crashes.

### Rationale
- Simple, bounded data structure (one string per node, typically hundreds not thousands).
- Fast lookup for skip decisions.
- Consistent with `IStateStore` usage patterns in the codebase.
- The full node list from `Nodes/source-tree.json` is NOT loaded into memory — it's streamed one node at a time. Only the confirmed set is in memory, which is bounded by the number of nodes (not revisions).

---

## R-008: Configuration Schema Versioning

### Question
Does the `NodeStructure` tool introduction require a config schema version bump?

### Decision
Yes. Per guardrail rule 9, the new `NodeStructure` tool type under `MigrationPlatform.Tools` is a schema addition. However, it is **additive** (new key, no breaking changes to existing config), so:

- Config schema version is bumped (minor increment).
- No upgrader is needed for existing configs (the `NodeStructure` key is simply absent, and the tool is inert when absent per FR-018).
- The new `Nodes/source-tree.json` and `Nodes/referenced-paths.json` package artifacts require `schemaVersions` entries in `manifest.json` for the new `Nodes` module folder. Since the artifacts are always written on export, existing packages without them are legacy packages.

### Rationale
- Additive changes don't break existing configs.
- Existing packages without `classification-nodes.json` are handled gracefully (FR-015 edge case — log warning and skip).

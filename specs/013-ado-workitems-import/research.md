# Research — Azure DevOps Work Items Import

**Feature**: 013-ado-workitems-import  
**Date**: 2026-04-15

---

## 1. Azure DevOps SDK for Work Item Creation and Updates

**Decision**: Use `Microsoft.TeamFoundationServer.Client` v20.x (`WorkItemTrackingHttpClient`) for all target write operations.

**Rationale**: This is the official .NET SDK already in use for export (`AzureDevOpsWorkItemRevisionSourceFactory`). It provides typed methods for:
- `CreateWorkItemAsync(JsonPatchDocument, project, type)` — creates a new work item
- `UpdateWorkItemAsync(JsonPatchDocument, id)` — updates an existing work item (fields, links)
- `CreateAttachmentAsync(Stream, fileName)` — uploads an attachment binary, returns a URL reference
- `AddCommentAsync(WorkItemComment, project, workItemId)` — creates a comment via the Comments API

**Alternatives considered**:
- Direct REST calls via `HttpClient` — rejected because the SDK already wraps the REST API with retry, serialization, and auth handling. No benefit to bypassing it.
- Azure DevOps REST v7.1 directly — same logic; the SDK targets v7.1 and adds type safety.

**Key patterns**:
- Work item creation uses `JsonPatchDocument` with `add` operations for each field.
- Work item updates use the same `JsonPatchDocument` with `add` (overwrite) or `replace` operations.
- Link creation is a `JsonPatchDocument` operation with `path = "/relations/-"` and a relation object containing the link type and target URL.
- Attachments are uploaded first to get a URL, then referenced via a link-add patch operation.
- The SDK propagates `CancellationToken` on all async methods.

---

## 2. SQLite for idmap.db

**Decision**: Use `Microsoft.Data.Sqlite` (3.x) for `Checkpoints/idmap.db`.

**Rationale**: The ID map must support O(1) lookup of source→target work item IDs and attachment upload records for 20,000+ entries. SQLite provides:
- Single-file, portable, zero-configuration database
- Indexed tables for fast lookup
- ACID transactions for safe writes during stage processing
- Included in the package zip — no external dependencies
- Cross-platform (Windows, Linux, macOS)

**Alternatives considered**:
- `Checkpoints/idmap.json` — rejected for scale. A 20k-entry JSON file requires full deserialization on every lookup (O(n)). Already documented as "fallback for small packages" in `.agents/30-context/domains/checkpointing-summary.md`.
- LiteDB — rejected because it adds an additional dependency with no significant advantage over SQLite for this use case.
- Dictionary in memory + periodic flush — rejected because it violates the streaming/memory-safety guardrail. The entire map would be in memory.

**Schema**:
```sql
CREATE TABLE work_item_map (
    source_id INTEGER PRIMARY KEY,
    target_id INTEGER NOT NULL
);

CREATE TABLE attachment_map (
    source_work_item_id INTEGER NOT NULL,
    revision_index INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    target_attachment_id TEXT NOT NULL,
    PRIMARY KEY (source_work_item_id, revision_index, relative_path)
);
```

**Note**: The SQLite prohibition in the guardrails applies to the control-plane data store (which must be PostgreSQL). Package-local indexed storage is explicitly permitted per the spec assumptions and `.agents/30-context/domains/checkpointing-summary.md`.

---

## 3. Work Item Resolution Strategies

**Decision**: Implement three resolution strategies behind `IWorkItemResolutionStrategy`.

**Rationale**: Different migration scenarios require different approaches to discover whether a source work item already exists in the target.

### Strategy 1: None (default when no extension configured)
- `idmap.db` is the sole source of truth
- No target querying at startup or per-item
- Suitable for first-time imports to a clean target

### Strategy 2: TargetField
- **Startup**: WIQL query `SELECT [System.Id] FROM WorkItems WHERE [Custom.MigratedSourceId] <> ''` → seeds `idmap.db`
- **Per-item fallback**: If source ID not in `idmap.db`, query `SELECT [System.Id] FROM WorkItems WHERE [Custom.MigratedSourceId] = '{sourceId}'`
- **Post-create**: Write source work item ID into the target's custom field
- **Best for**: Repeat/incremental migrations where a custom field tracks provenance

### Strategy 3: TargetHyperlink
- **Startup**: WIQL query `SELECT [System.Id] FROM WorkItems WHERE [System.HyperLinkCount] > 0` → fetch relations via REST → filter hyperlinks matching configured URL pattern → seed `idmap.db`
- **Per-item**: No live fallback (WIQL cannot filter by hyperlink URL content)
- **Post-create**: Add a hyperlink with the configured URL pattern containing the source work item ID
- **Best for**: Migrations where custom fields cannot be created on the target

**Alternatives considered**:
- Single hardcoded strategy — rejected because different customers have different target project constraints (some can add custom fields, some cannot).
- Strategy pattern with runtime plugin loading — rejected as over-engineering; a simple DI-registered interface with three sealed implementations is sufficient.

---

## 4. Streaming Import Orchestration Pattern

**Decision**: Create `WorkItemImportOrchestrator` mirroring the existing `WorkItemExportOrchestrator` pattern.

**Rationale**: The export side uses `WorkItemExportOrchestrator` to manage the streaming loop, cursor tracking, and progress reporting. The import side needs the same pattern but reads from the package and writes to the target.

**Execution flow**:
1. Read cursor via `ICheckpointingService.ReadCursorAsync("WorkItems")`
2. Run `IWorkItemResolutionStrategy.SeedAsync()` to pre-populate `idmap.db` from target (if configured)
3. Enumerate folders via `IArtefactStore.EnumerateAsync("WorkItems/")` — lexicographic order guaranteed
4. For each folder path:
   a. Skip if lexicographically <= cursor's `lastProcessed`
   b. If cursor stage is mid-folder, resume from next stage
   c. Determine if revision folder or comment folder (by parsing folder name for `c` prefix on third segment)
   d. **Revision folder**: Process stages A→B→C→D, write cursor after each
   e. **Comment folder**: Read `comment.json`, create comment on target via Comments API
5. After all four stages succeed, write cursor with stage `Completed`
6. Emit progress event via `IProgressSink`

**Key design decisions**:
- `RevisionFolderProcessor` handles the 4-stage logic for a single revision folder, keeping the orchestrator focused on enumeration and cursor management.
- The orchestrator does NOT accumulate a list of folders — it processes `IAsyncEnumerable<string>` lazily.
- The `EnumerateAsync` prefix approach means only paths under `WorkItems/` are returned, already sorted.

---

## 5. Comment Import Approach

**Decision**: Process comment sub-folders and inline `comment.json` files as part of the same enumeration loop.

**Rationale**: Comment sub-folders (`<ticks>-<workItemId>-c<commentId>/`) are interleaved with revision sub-folders in lexicographic order. Processing them in the same enumeration guarantees chronological ordering without a separate pass.

**Distinguishing comment vs revision folders**: Parse the folder name. If the third segment starts with `c` (e.g., `c42`), it is a comment folder. Otherwise, it is a revision folder.

**Inline comments** (JSON array in `<rev-folder>/comment.json`): These are processed after Stage D of the revision folder — they are comments associated with that specific revision's timestamp.

**Comment creation API**: `WorkItemTrackingHttpClient.AddCommentAsync()` creates a new comment. The SDK does not support setting `CreatedBy` or `CreatedDate` — these are system-controlled. The import records the original author and date in the comment text as metadata.

**Alternatives considered**:
- Separate comment pass after all revisions — rejected because it breaks chronological ordering and requires a second full enumeration.
- Storing comments as work item history (`System.History` field) — rejected because comments and history are different entities in Azure DevOps.

---

## 6. Embedded Image URL Rewriting

**Decision**: Upload images to target via the Attachments API, then rewrite URLs in field values and comment text before applying them.

**Rationale**: Embedded images in HTML fields reference source system URLs (e.g., `https://dev.azure.com/sourceorg/_apis/wit/attachments/...`). After migration, these URLs would be broken. The export already downloads and stores images beside `revision.json` with metadata in the `embeddedImages` array.

**Import flow**:
1. For each entry in `revision.json.embeddedImages`:
   a. Read image binary from artefact store (path = revision folder + `relativePath`)
   b. Upload to target via `CreateAttachmentAsync()` — returns new target URL
   c. Build a URL map: `originalUrl → targetUrl`
2. Before applying field values (Stage B), scan all field values for `originalUrl` occurrences and replace with `targetUrl`
3. Same replacement for comment text in comment folders and inline comments

**Alternatives considered**:
- Skip rewriting; leave broken URLs — rejected because it degrades migration fidelity.
- Proxy service that redirects old URLs — rejected as operationally complex and requires the source system to remain accessible.

---

## 7. Retry and Throttling

**Decision**: Use Polly retry with exponential back-off for all target API calls, respecting `policies.retries.max` from the job contract.

**Rationale**: The Azure DevOps REST API may rate-limit or return transient errors (429, 503). The existing export infrastructure already uses Polly for retry. Import must follow the same pattern.

**Configuration**: The `policies.retries.max` field in the job contract (default: 3) controls maximum retry attempts. Back-off uses `2^attempt * 1 second` base delay with jitter.

**Alternatives considered**:
- No retry — rejected because network transients would cause unnecessary failures.
- Fixed-interval retry — rejected per constitution (Principle X, category 14: "retry without exponential back-off" is a reject trigger).

---

## 8. Idempotency Across Stages

**Decision**: Each stage checks for prior completion before executing.

**Rationale**: Import must be safe to retry. The cursor tracks which stage completed; `idmap.db` tracks which work items and attachments have been written.

| Stage | Idempotency mechanism |
|-------|----------------------|
| A (CreatedOrUpdated) | Check `idmap.db` for `source_id → target_id`. If found, skip creation. |
| B (AppliedFields) | Re-applying the same field patch is idempotent in the Azure DevOps API. |
| C (AppliedLinks) | Query target work item relations before adding; skip existing links. |
| D (UploadedAttachments) | Check `idmap.db` for `(workItemId, revisionIndex, relativePath)`. If found, skip upload. |

---

## 9. Extension Enable/Disable Flags

**Decision**: Respect the `Extensions` array from the module configuration. Skip disabled stages.

**Rationale**: The job contract's `modules[WorkItems].extensions` array controls which sub-data is imported:
- `Revisions: false` → skip all revision processing (import only creates work items at latest state)
- `Links: false` → skip Stage C entirely
- `Attachments: false` → skip Stage D entirely  
- `Comments: false` → skip comment folder processing and inline `comment.json`
- `EmbeddedImages: false` → skip image upload and URL rewriting

This mirrors the export-side extension flags.

---

## 10. CLI Enablement

**Decision**: Remove the import stub in `QueueCommand` and enable import mode to flow through the existing job submission pipeline.

**Rationale**: The CLI architecture already supports `Import` and `Both` modes — the `QueueCommand` builds a `MigrationJob` and submits it via `ControlPlaneClient`. The import stub (`ExecuteImportStub()`) is the only blocker. The `MigrationAgentWorker` already calls `ImportAsync` on modules.

**Changes required**:
1. Remove `ExecuteImportStub()` — replace with the same `ExecuteExportAsync` flow (which is mode-agnostic: it submits a job and the agent determines export vs import from the `mode` field).
2. Ensure `QueueCommand` validates that `target` is present in config when `mode` is `Import` or `Both`.
3. No `[HideFromChannel]` attribute exists in the current code (the spec's FR-017 reference may be outdated — the stub is in the `switch` statement, not an attribute).


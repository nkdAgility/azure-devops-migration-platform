# Work Item Iteration Pattern

> **MANDATORY**: All implementations that process work items MUST use the patterns documented here.
> Do not create alternative approaches. Reuse existing abstractions and augment them where necessary.

## Mandatory Reuse Principle

**New implementations MUST use existing architecture before building new infrastructure.**

This pattern has been proven to handle 20,000+ work items with bounded memory. Every implementation that touches work item export/import must:

1. Use `WorkItemExportOrchestrator` for export streaming.
2. Use `IWorkItemRevisionSource` as the source abstraction.
3. Use `ICheckpointingService` for cursor-based progress tracking.
4. Use `IArtefactStore.EnumerateAsync()` (in lexicographic order) for import enumeration.
5. Stream attachment binaries directly — never buffer in memory.
6. Use `IWorkItemFetchService` for field-projected, filtered work item fetching in inventory, dependency analysis, and catalog operations. Do not call `GetWorkItemsAsync` directly from these callers.

If an existing pattern does not fit your use case, you MUST:

1. Document why the existing pattern cannot be reused or extended.
2. Propose a new abstraction (in `DevOpsMigrationPlatform.Abstractions` or module-private).
3. Ensure the new abstraction is used by at least two independent modules (no single-use abstractions).
4. Get explicit approval from the architecture team before implementing.

**Violations of the mandatory reuse principle are rejection triggers.** See [.agents/guardrails/architecture-boundaries.md](../.agents/guardrails/architecture-boundaries.md) rule 21 and [agents.md](../agents.md) reject conditions.

---

## 1. Overview

Work item export and import must handle large datasets (20,000+ items) with bounded memory. This is achieved through:

- **Streaming enumeration** via `IAsyncEnumerable<WorkItemRevision>` (no pre-loading of all items)
- **Field-projected fetching** via `IWorkItemFetchService` returning `IAsyncEnumerable<FetchedWorkItem>` (inventory/dependency paths)
- **Chronological ordering** via lexicographic folder paths (no in-memory sorting)
- **Cursor-based checkpointing** via `ICheckpointingService` (resumable and observable)
- **Staged processing** (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments)

---

## 2. Export Pattern: WorkItemExportOrchestrator

### Use Case

You are exporting work items from a source system (Azure DevOps, TFS) to the package.

### Implementation

Use `WorkItemExportOrchestrator` in any module that exports work items:

```csharp
public async Task ExportAsync(ExportContext context, CancellationToken ct)
{
    var source = await _sourceFactory
        .CreateAsync(orgUrl, project, pat, wiqlQuery, ct)
        .ConfigureAwait(false);

    var checkpointingService = new CheckpointingService(context.StateStore);

    var orchestrator = new WorkItemExportOrchestrator(
        context.ArtefactStore,
        checkpointingService,
        attachmentBinarySource,
        context.ProgressSink,
        organisationUrl: orgUrl,
        project: project,
        pat: pat,
        inlineCommentSourceFactory: inlineFactory);

    await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);
}
```

### Guarantees

- **Streaming**: Revisions are processed one at a time via `await foreach`. No revision list is accumulated in memory.
- **Resumable**: The cursor tracks the last processed folder path. On resume, all revisions at or before the cursor are skipped.
- **Observable**: Progress is emitted via `IProgressSink` per revision (configurable batching at the sink level).

### Folder Path Computation

The orchestrator computes folder paths from revision data using the canonical formula:

```
WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/
```

Where:
- `yyyy-MM-dd` is the revision's `ChangedDate` (UTC) formatted as year-month-day.
- `<ticks>` is the revision's `ChangedDate` in .NET `DateTime.Ticks` format (10-million-ths of a second).
- `<workItemId>` is the work item ID in the source system.
- `<revisionIndex>` is the zero-based revision index.

**Do not pre-compute or alter folder paths.** The orchestrator owns path generation.

### Inline Comment Fetching (Comments Extension)

When a revision represents a comment edit or delete — detected by the presence of `System.CommentCount` in changed fields AND absence of `System.History` — the orchestrator can fetch matching comment versions from the source API and write `comment.json` beside `revision.json` in the same revision folder.

This is gated by the **Comments extension `Enabled` flag**. When enabled (default), the orchestrator:

1. Detects comment edit/delete revisions.
2. Fetches all comments for the work item from the Comments API.
3. Filters to those with `ModifiedDate` within ±1 second of the revision's `ChangedDate`.
4. Writes a JSON array of matching comments to `comment.json`.

**Failure handling:** Comment API failures are non-fatal. A `ProgressEvent` warning is emitted and export continues (the revision folder exists without `comment.json`).

---

## 3. Required Interfaces

All work item export/import implementations depend on these interfaces from `DevOpsMigrationPlatform.Abstractions`:

### IWorkItemRevisionSource

```csharp
public interface IWorkItemRevisionSource
{
    IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(CancellationToken cancellationToken);
}
```

**Requirements:**

- Must yield revisions in source chronological order (or allow the source to specify order).
- Must NOT buffer all revisions in a list before yielding.
- Must propagate `CancellationToken` to all underlying async operations.
- Failures are fatal; exceptions halt enumeration.

### IWorkItemFetchService

`IWorkItemFetchService` is the canonical entry point for inventory, dependency analysis, and catalog operations. It returns a field-projected, optionally filtered, async stream of work items. Modules and callers **must not** call `GetWorkItemsAsync` directly — they must use this service.

```csharp
public interface IWorkItemFetchService
{
    IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        WorkItemFetchScope scope,
        CancellationToken cancellationToken);
}

public record WorkItemFetchScope
{
    public required string OrgUrl              { get; init; }
    public required string Project             { get; init; }
    public required string? Wiql               { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public bool ResumeEnabled                  { get; init; } = false;
    public BatchContinuationToken? SavedContinuationToken { get; init; }
    public Action<BatchContinuationToken>? ContinuationCheckpointWriter { get; init; }
}

public record FetchedWorkItem
{
    public required int Id                     { get; init; }
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
}
```

**Scope semantics:**
- `Fields` must be the exact list of ADO field reference names to project. Requesting only the fields you need reduces API payload and memory pressure.
- `Wiql` may be `null` to use the platform default query (`SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.ChangedDate] ASC`).
- `ResumeEnabled = true` activates resumable batching (see section 11). Default is `false`.
- `ContinuationCheckpointWriter` is the caller's persistence callback for `BatchContinuationToken` checkpoints. If `null` and `ResumeEnabled = true`, a warning is logged and checkpoints are silently skipped.

**Usage pattern (inventory):**

```csharp
await foreach (var item in _fetchService.FetchAsync(new WorkItemFetchScope
{
    OrgUrl  = orgUrl,
    Project = projectName,
    Wiql    = null,   // use default
    Fields  = ["System.Id", "System.WorkItemType", "System.ChangedDate"]
}, ct))
{
    // process item.Fields["System.WorkItemType"]
}
```

### IArtefactStore

```csharp
public interface IArtefactStore
{
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
    Task WriteBinaryAsync(string path, Stream content, CancellationToken cancellationToken);
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken);
    Task<string> ReadAsync(string path, CancellationToken cancellationToken);
    Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken);
}
```

**Usage in work item processing:**

- `WriteAsync("WorkItems/2026-04-14/638...json", revisionJson, ct)` — write `revision.json`
- `WriteAsync("WorkItems/2026-04-14/638.../comment.json", commentJson, ct)` — write inline comment array
- `WriteBinaryAsync("WorkItems/2026-04-14/638.../attachment.png", stream, ct)` — write attachment binary
- `EnumerateAsync("WorkItems/", ct)` — stream revision folders in lexicographic order on import

**Implementations:**
- `FileSystemArtefactStore` — local file:/// storage
- `AzureBlobArtefactStore` — Azure Blob Storage (standard HTTPS URLs with `.blob.core.windows.net`)

**Mandatory rule:** Modules must not reference a concrete implementation. Always depend on `IArtefactStore`.

### IStateStore

Provides cursor persistence via `ICheckpointingService`:

```csharp
public interface IStateStore
{
    Task WriteAsync(string key, string value, CancellationToken cancellationToken);
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);
}
```

The `ICheckpointingService` wraps this:

```csharp
public interface ICheckpointingService
{
    Task<Cursor?> ReadCursorAsync(string module, CancellationToken cancellationToken);
    Task WriteCursorAsync(string module, Cursor cursor, CancellationToken cancellationToken);
}
```

**Usage:**

```csharp
var checkpointingService = new CheckpointingService(context.StateStore);
var cursor = await checkpointingService.ReadCursorAsync("WorkItems", ct);
// ... process revisions ...
await checkpointingService.WriteCursorAsync("WorkItems", newCursor, ct);
```

### IProgressSink

Emits progress events:

```csharp
public interface IProgressSink
{
    void Emit(ProgressEvent evt);
}
```

**Usage:**

```csharp
_progressSink?.Emit(new ProgressEvent
{
    Module = "WorkItems",
    Stage = "Export",
    LastProcessed = folderPath,
    WorkItemId = revision.WorkItemId,
    Message = $"[WorkItems] Processed {folderPath}"
});
```

---

## 4. Attachment Binary Sources

Work item attachments are streamed directly from the source to the package:

```csharp
public interface IAttachmentBinarySource
{
    IAsyncEnumerable<AttachmentBinary> GetBinariesAsync(
        int workItemId,
        int revisionIndex,
        IReadOnlyList<AttachmentMetadata> attachments,
        CancellationToken cancellationToken);
}

public record AttachmentBinary(string RelativePath, Stream Content);
```

The orchestrator writes each binary directly via `IArtefactStore.WriteBinaryAsync`. Attachment binaries are **never buffered in memory**.

---

## 5. Comment Source Factory

For fetching inline comments during export:

```csharp
// DevOpsMigrationPlatform.Abstractions
public interface IWorkItemCommentSourceFactory
{
    IWorkItemCommentSource Create(string orgUrl, string project, string pat);
}

// DevOpsMigrationPlatform.Abstractions.Services
public interface IWorkItemCommentSource
{
    IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}
```

Implementations stream comments one at a time; the orchestrator filters by `ModifiedDate` and writes matching comments.

---

## 6. Import Pattern: WorkItemImportOrchestrator

When work item import is performed, it follows the same streaming principle as export.

### Use Case

You are importing work items from a package (written by a previous export) into a target Azure DevOps project.

### Implementation

Use `WorkItemImportOrchestrator` in any module that imports work items:

```csharp
public async Task ImportAsync(ImportContext context, CancellationToken ct)
{
    var target = await _importTargetFactory
        .CreateAsync(orgUrl, project, pat, ct)
        .ConfigureAwait(false);

    var checkpointingService = new CheckpointingService(context.StateStore);
    var idMapStore = new SqliteIdMapStore(ResolveIdMapPath(job.Artefacts.PackageUri));

    var processor = new RevisionFolderProcessor(
        target, idMapStore, checkpointingService,
        _identityMappingService, context.ArtefactStore, processorLogger);

    var orchestrator = new WorkItemImportOrchestrator(
        context.ArtefactStore, checkpointingService,
        context.ProgressSink, _resolutionStrategy,
        idMapStore, processor, target, orchestratorLogger);

    await orchestrator.ImportAsync(ext, resumeMode, ct).ConfigureAwait(false);
}
```

### Guarantees

- **Streaming**: Revision folders are enumerated via `IArtefactStore.EnumerateAsync("WorkItems/", ct)` with `await foreach`. No folder list is accumulated in memory.
- **Resumable**: The cursor tracks the last processed folder path and stage. On resume, completed folders are skipped and partial folders resume from the next stage.
- **Observable**: Progress is emitted via `IProgressSink` per folder.
- **Idempotent**: Each stage checks `IIdMapStore` before creating or uploading. Repeating a stage that already succeeded produces no new side effects.

### 4-Stage Processing

Each revision folder is processed through four sequential stages via `RevisionFolderProcessor`:

| Stage | Name | Description |
|-------|------|-------------|
| A | `CreatedOrUpdated` | Create or resolve target work item; record ID mapping in `.migration/Checkpoints/idmap.db` |
| B | `AppliedFields` | Apply all fields (with identity resolution); rewrite embedded image URLs |
| C | `AppliedLinks` | Add related links, external links, and hyperlinks (skip duplicates) |
| D | `UploadedAttachments` | Stream attachment binaries to the target (skip already-uploaded) |

A cursor is written after each stage. On resume, completed stages are skipped.

### ID Mapping: idmap.db

The `SqliteIdMapStore` maintains `.migration/Checkpoints/idmap.db` (SQLite, package-local):

- `work_item_map (source_id PK, target_id)` — source-to-target work item ID mapping.
- `attachment_map (source_work_item_id, revision_index, relative_path, target_attachment_id)` — idempotency for attachment uploads.

This file is preserved across `--force-fresh` runs. It is separate from the control-plane database.

### Work Item Resolution Strategies

`IWorkItemResolutionStrategy` controls how Stage A discovers existing target work items:

| Strategy | Seed Mechanism | Live Fallback |
|----------|---------------|---------------|
| `NullResolutionStrategy` (default) | None | None — creates new WI if not in idmap.db |
| `TargetFieldResolutionStrategy` | WIQL query on custom field at startup | Single WIQL query per unmapped source ID |
| `TargetHyperlinkResolutionStrategy` | Inspect hyperlinks on all target WIs at startup | None (FR-022) |

### Comment and Embedded Image Handling

- **Comment sub-folders** (name matches `<ticks>-<workItemId>-c<commentId>`): The orchestrator reads `comment.json` and calls `IWorkItemImportTarget.CreateCommentAsync`. Gated by the Comments extension flag.
- **Inline comments** in revision folders: `RevisionFolderProcessor` reads a `comment.json` array after Stage D. Gated by the Comments extension flag.
- **Embedded images**: If the EmbeddedImages extension is enabled and `revision.embeddedImages` is non-empty, images are uploaded via `IWorkItemImportTarget.UploadEmbeddedImageAsync` and source URLs in field values are rewritten before Stage B applies fields.

---

## 7. Handling 20,000+ Items

The pattern is memory-safe because:

| Component | Guarantee |
|-----------|-----------|
| `IWorkItemRevisionSource.GetRevisionsAsync()` | Yields one revision at a time via `async yield return` |
| `WorkItemExportOrchestrator.ExportAsync()` | Processes via `await foreach` (no buffering) |
| `IAttachmentBinarySource.GetBinariesAsync()` | Streams content directly; no byte array accumulation |
| `IArtefactStore.EnumerateAsync()` | Lexicographic order on each call; no pre-loading |
| `IProgressSink.Emit()` | Async write to remote sink (non-blocking) |
| Cursor checkpoint | Single `Cursor` object in memory (constant size, ~100 bytes) |

**No in-memory lists of revisions, attachments, or comments are ever constructed.**

---

## 8. What NOT to Do

| Anti-Pattern | Reason |
|---|---|
| Load all revisions into `List<WorkItemRevision>` before processing | Breaks memory safety for 20k+ items |
| Sort enumerated paths in memory | Defeats the chronological ordering guarantee |
| Implement a custom export loop instead of using `WorkItemExportOrchestrator` | Duplicates logic, introduces bugs, misses resume/checkpoint semantics |
| Write attachment binaries to a temporary file list, then upload | Holds all attachments in memory; use `IArtefactStore.WriteBinaryAsync` directly |
| Use watermark tables or in-memory dictionaries for progress tracking | Use `ICheckpointingService` via `IStateStore` (cursor-based) |
| Bypass `IWorkItemRevisionSource` and call the source API directly from module code | Couples modules to specific source systems; breaks multi-source flexibility |
| Write files outside `IArtefactStore` | Breaks cloud deployment (local filesystem is not portable to blob storage) |

---

## 9. Extension Points

Use these when you need to extend work item processing:

### Custom WIQL Scope

```csharp
var source = await _sourceFactory.CreateAsync(
    orgUrl,
    project,
    pat,
    wiqlQuery: "SELECT [System.Id] FROM WorkItems WHERE [System.State] = 'Active'",
    ct);
```

### Custom Extensions

The `WorkItemsModule` accepts extension configurations (Revisions, Links, Attachments, Comments, EmbeddedImages). Each is independently toggled via the module configuration in the job contract.

### Custom Comment Source

Implement `IWorkItemCommentSourceFactory` and `IWorkItemCommentSource` to fetch comments from a different source (e.g. a custom API).

### Custom Attachment Source

Implement `IAttachmentBinarySource` to fetch attachments from a custom storage system.

---

## 10. Examples

### Example: Export Work Items with Attachments and Comments

```csharp
public async Task ExportAsync(ExportContext context, CancellationToken ct)
{
    var source = await _sourceFactory
        .CreateAsync(orgUrl, project, pat, wiqlQuery, ct)
        .ConfigureAwait(false);

    var checkpointingService = new CheckpointingService(context.StateStore);

    var orchestrator = new WorkItemExportOrchestrator(
        context.ArtefactStore,
        checkpointingService,
        attachmentBinarySource: _attachmentBinarySource,
        progressSink: context.ProgressSink,
        organisationUrl: orgUrl,
        project: project,
        pat: pat,
        inlineCommentSourceFactory: _inlineCommentSourceFactory);

    await orchestrator.ExportAsync(source, ct);
}
```

### Example: Resume with Cursor

```csharp
var checkpointingService = new CheckpointingService(context.StateStore);
var cursor = await checkpointingService.ReadCursorAsync("WorkItems", ct);

if (cursor != null)
{
    _logger.LogInformation($"Resuming from: {cursor.LastProcessed}");
}

// The orchestrator automatically skips all revisions at or before cursor.LastProcessed
await orchestrator.ExportAsync(source, ct);
```

---

## 11. Resumable Batching Contract

### Overview

`IWorkItemFetchService` and `IWorkItemQueryWindowStrategy` support resumable batching for inventory, dependency analysis, and discovery callers. This allows long-running enumeration to resume from a saved `BatchContinuationToken` rather than reprocessing the entire project.

Resume is **opt-in** via `WorkItemFetchScope.ResumeEnabled` (default `false`). Non-resume callers experience zero behavioral change.

### BatchContinuationToken

A sealed record emitted per-batch and once at end-of-stream. Fields:

| Field | Type | Purpose |
|-------|------|---------|
| `StrategyVersion` | `string` | Schema version for token compatibility |
| `ChangedDateUtc` | `DateTime` | Primary resume key — last processed ChangedDate |
| `WorkItemId` | `int` | Secondary resume key — deterministic tie-breaker |
| `QueryFingerprint` | `string` | SHA-256 of (WIQL query text + sorted parameters) |
| `Completed` | `bool` | `true` = end-of-stream; safe to skip entirely on next resume |
| `GeneratedAtUtc` | `DateTime` | Diagnostic timestamp |

Tokens with `Completed = true` allow callers to detect a previously finished enumeration and skip it on the next run.

### ResumeDecision

Evaluated once at the start of `FetchAsync()`:

| Status | Meaning | Delivery |
|--------|---------|----------|
| `Accepted` | Token valid; enumeration continues from saved position | Structured log + OTel metric |
| `RejectedQueryMismatch` | Query fingerprint mismatch | `ResumeRejectedException` thrown (extends `InvalidOperationException`; carries `ResumeDecision` payload) |
| `Unavailable` | No saved token; start fresh | Info-level log; no exception |

### Caller Responsibilities

1. **Checkpoint persistence**: The strategy emits `BatchContinuationToken` via `ContinuationCheckpointWriter` callback. Callers choose persistence cadence (every batch, every N batches, etc.). The strategy does **not** persist state.
2. **Duplicate handling**: Resume with source drift may yield the same work item ID in multiple windows. Callers must use idempotent persistence or explicit dedup. The strategy does **not** deduplicate.
3. **Mismatch recovery**: On `ResumeRejectedException`, callers decide whether to fail, discard the token and start fresh, or log and continue. The strategy does not auto-recover.
4. **Completion checkpoint**: Callers must persist the final `BatchContinuationToken` (with `Completed = true`) emitted at end-of-stream to enable safe skip on next run.

### Query Fingerprint

`IQueryFingerprintService` computes a deterministic SHA-256 hash from the WIQL query text and sorted query parameters. Post-fetch filters (e.g. `WorkItemFieldFilterEvaluator`) are explicitly **excluded** from the fingerprint — they are caller-level post-processing, not part of the enumeration contract.

### Ordering

When resume is enabled, the window strategy uses `ORDER BY [System.ChangedDate] ASC, [System.Id] ASC` (oldest-first). This ordering is drift-tolerant: items edited after the cursor has advanced appear later in sequence and are processed on subsequent continuation.

Non-resume callers retain the existing traversal behavior.

### Backward Compatibility

- `WorkItemFetchScope.ResumeEnabled` defaults to `false` — zero behavioral change for existing callers.
- `WorkItemQueryWindowOptions.SavedContinuationToken` defaults to `null`.
- `ContinuationCheckpointWriter` defaults to `null`. If `null` and `ResumeEnabled = true`, a warning log is emitted and checkpoints are silently skipped.
- New `ICheckpointingService` continuation token methods are additive; existing cursor methods are unchanged.
- `PackagePaths.ContinuationFile()` does not conflict with existing `CursorFile()`.

---

## References

- `DevOpsMigrationPlatform.Infrastructure.Export.WorkItemExportOrchestrator` — the canonical export implementation
- `DevOpsMigrationPlatform.Infrastructure.Modules.WorkItemsModule` — the module that uses the orchestrator
- `DevOpsMigrationPlatform.Abstractions.Services.IWorkItemRevisionSource` — the source interface
- [docs/module-development-guide.md](module-development-guide.md) — module architecture and module contract
- [.agents/context/workitems-format-summary.md](../.agents/context/workitems-format-summary.md) — on-disk format specification
- [.agents/context/import-streaming.md](../.agents/context/import-streaming.md) — import streaming semantics (future)

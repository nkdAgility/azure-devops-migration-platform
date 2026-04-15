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

If an existing pattern does not fit your use case, you MUST:

1. Document why the existing pattern cannot be reused or extended.
2. Propose a new abstraction (in `DevOpsMigrationPlatform.Abstractions` or module-private).
3. Ensure the new abstraction is used by at least two independent modules (no single-use abstractions).
4. Get explicit approval from the architecture team before implementing.

**Violations of the mandatory reuse principle are rejection triggers.** See [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md) rule 21 and [agents.md](../agents.md) reject conditions.

---

## 1. Overview

Work item export and import must handle large datasets (20,000+ items) with bounded memory. This is achieved through:

- **Streaming enumeration** via `IAsyncEnumerable<WorkItemRevision>` (no pre-loading of all items)
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

### IArtefactStore

```csharp
public interface IArtefactStore
{
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
    Task WriteBinaryAsync(string path, Stream content, CancellationToken cancellationToken);
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken);
    Task<string> ReadAsync(string path, CancellationToken cancellationToken);
    Task<Stream> ReadBinaryAsync(string path, CancellationToken cancellationToken);
}
```

**Usage in work item processing:**

- `WriteAsync("WorkItems/2026-04-14/638...json", revisionJson, ct)` — write `revision.json`
- `WriteAsync("WorkItems/2026-04-14/638.../comment.json", commentJson, ct)` — write inline comment array
- `WriteBinaryAsync("WorkItems/2026-04-14/638.../attachment.png", stream, ct)` — write attachment binary
- `EnumerateAsync("WorkItems/", ct)` — stream revision folders in lexicographic order on import

**Implementations:**
- `FileSystemArtefactStore` — local file:/// storage
- `AzureBlobArtefactStore` — remote azureblob:// storage

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
public interface IWorkItemCommentSourceFactory
{
    IWorkItemCommentSource Create(string orgUrl, string project, string pat);
}

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

## 6. Import Pattern (Future: Streaming)

When work item import is implemented, it will follow the same streaming principle:

1. **Enumeration**: `IArtefactStore.EnumerateAsync("WorkItems/", ct)` yields revision folders in lexicographic order.
2. **Cursor-based resume**: The cursor tracks the last successfully completed revision folder (down to the stage level).
3. **Staged processing**: Each revision is processed through stages in order (see [.agents/context/import-streaming.md](../.agents/context/import-streaming.md)).
4. **Idempotency**: Each stage is idempotent; repeating a stage that already succeeded produces no new side effects.

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

## References

- `DevOpsMigrationPlatform.Infrastructure.Export.WorkItemExportOrchestrator` — the canonical export implementation
- `DevOpsMigrationPlatform.Infrastructure.Modules.WorkItemsModule` — the module that uses the orchestrator
- `DevOpsMigrationPlatform.Abstractions.Services.IWorkItemRevisionSource` — the source interface
- [docs/modules.md](modules.md) — module architecture and module contract
- [.agents/context/workitems-format.md](../.agents/context/workitems-format.md) — on-disk format specification
- [.agents/context/import-streaming.md](../.agents/context/import-streaming.md) — import streaming semantics (future)

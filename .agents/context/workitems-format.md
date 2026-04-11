# WorkItems Format

## 4. WorkItems Module (Aggregate Root)

WorkItems is the primary high-fidelity module. Its on-disk layout is canonical and must not be changed.

### On-Disk Layout

```
WorkItems/
  yyyy-MM-dd/
    <workItemId>-comments.json
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      <attachment files>
      <embedded image files>
```

Each date folder may contain a `<workItemId>-comments.json` file holding all comments and discussions for that work item.

Each revision folder corresponds to exactly one revision of one work item. Attachment files and embedded image files belonging to that revision are stored physically beside `revision.json`.

### revision.json Required Fields

```json
{
  "workItemId": 12345,
  "revisionIndex": 17,
  "changedDate": "2026-02-25T18:12:34Z",
  "fields": [...],
  "externalLinks": [...],
  "relatedLinks": [...],
  "hyperlinks": [...],
  "attachments": [
    {
      "originalName": "screenshot.png",
      "relativePath": "638760123456789012-screenshot.png",
      "sha256": "abc123...",
      "size": 102400
    }
  ],
  "embeddedImages": [
    {
      "originalUrl": "https://dev.azure.com/org/_apis/wit/attachments/uuid",
      "relativePath": "image-abc123def456.png",
      "extension": "png",
      "sha256": "abc123...",
      "size": 51200
    }
  ]
}
```

### Required Fields

| Field | Type | Description |
|---|---|---|
| `workItemId` | integer | Work item ID in the source system |
| `revisionIndex` | integer | Zero-based revision index |
| `changedDate` | ISO 8601 string | UTC timestamp of the revision |
| `fields` | array | All field values as of this revision |
| `externalLinks` | array | External links attached to this revision |
| `relatedLinks` | array | Related work item links |
| `hyperlinks` | array | Hyperlinks |
| `attachments` | array | Attachment metadata for files in this folder |
| `embeddedImages` | array | Embedded images discovered and rewritten in field values |

### Attachment Rules

- Attachment files are stored **beside** `revision.json` in the same folder.
- There is **no global attachments module** and no mandatory blob store.
- Each attachment entry in `revision.json` includes:
  - `originalName` — the filename as it appeared in the source system
  - `relativePath` — the actual filename on disk (may be prefixed with attachment ID or SHA)
  - `sha256` — integrity hash
  - `size` — byte count

**Optional integrity improvement:** store files as `<attachmentId>-<sha256>` on disk, preserving the original filename only in metadata.

### No Global Attachments Module

Attachments are scoped to their revision. There is no `Attachments/` root folder in the package. This is a hard rule; see [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md).

### Comments and Discussions

Comments are stored in a `<workItemId>-comments.json` file at the date-folder level (alongside revision folders). This file is created once per work item and contains all comments and discussion threads. The file is updated if the work item receives new comments in a later batch export.

#### comments.json Schema

```json
{
  "workItemId": 12345,
  "comments": [
    {
      "commentId": "comment-uuid",
      "version": 1,
      "text": "This is a comment",
      "format": "html|markdown|plaintext",
      "renderedText": "<p>This is a comment</p>",
      "createdBy": { "id": "user-id", "name": "John Doe", "email": "john@example.com" },
      "createdDate": "2026-02-25T18:12:34Z",
      "modifiedBy": { "id": "user-id", "name": "John Doe", "email": "john@example.com" },
      "modifiedDate": "2026-02-25T18:12:34Z",
      "isDeleted": false
    }
  ]
}
```

### Embedded Images

Embedded images are images referenced inline within HTML or Markdown field values (e.g. `<img src="...">` in HTML or `![](...)` in Markdown). These are a different category from attachments: they are discovered during export, downloaded from the source system, and stored locally.

#### Embedded Image Rules

1. **Discovery**: During export, field values are scanned for HTML `<img src>` tags and Markdown `![](url)` patterns.
2. **Download**: Images with URLs hosted on the source organisation are downloaded; non-ADO URLs are left as-is (and logged as warnings).
3. **Storage**: Downloaded images are stored beside `revision.json` (for revision field images) or beside `comments.json` (for comment images) using a deterministic filename (e.g. `image-<sha256>.<ext>`).
4. **Rewriting**: Field and comment values in the JSON are rewritten to reference the local filename instead of the remote URL (e.g. `image-abc123.png` instead of `https://dev.azure.com/...`).
5. **Metadata**: Each embedded image entry is tracked in an `embeddedImages` array in the revision or comment, recording the original URL, stored filename, size, and integrity hash.

#### Embedded Image Metadata Entry

```json
{
  "originalUrl": "https://dev.azure.com/org/_apis/wit/attachments/uuid",
  "relativePath": "image-abc123def456.png",
  "extension": "png",
  "sha256": "abc123...",
  "size": 51200
}
```

---

## Source Interface Contract

The type serialised to `revision.json` is `WorkItemRevision`, defined in `DevOpsMigrationPlatform.Abstractions`.

### WorkItemRevision

```csharp
public record WorkItemRevision
{
    public int WorkItemId { get; init; }
    public int RevisionIndex { get; init; }
    public DateTimeOffset ChangedDate { get; init; }
    public IReadOnlyList<WorkItemField> Fields { get; init; }
    public IReadOnlyList<WorkItemLink> ExternalLinks { get; init; }
    public IReadOnlyList<WorkItemLink> RelatedLinks { get; init; }
    public IReadOnlyList<WorkItemLink> Hyperlinks { get; init; }
    public IReadOnlyList<AttachmentMetadata> Attachments { get; init; }
    public IReadOnlyList<EmbeddedImageMetadata> EmbeddedImages { get; init; }
}
```

`WorkItemField` carries `ReferenceName` (e.g. `"System.Title"`) and `Value` (string-serialised).
`WorkItemLink` carries `Rel` and `Url`.
`AttachmentMetadata` carries `OriginalName`, `RelativePath`, `Sha256`, and `Size`.
`EmbeddedImageMetadata` carries `OriginalUrl`, `RelativePath`, `Extension`, `Sha256`, and `Size`.

### IWorkItemRevisionSource

Source connectors implement `IWorkItemRevisionSource` in `DevOpsMigrationPlatform.Abstractions`:

```csharp
public interface IWorkItemRevisionSource
{
    IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(CancellationToken cancellationToken);
}
```

The `WorkItemExportOrchestrator` depends on this interface. It calls `GetRevisionsAsync` in a
streaming `await foreach` — no connector may buffer all revisions into a list before yielding.

**The orchestrator computes the folder path** from the revision data using `BuildFolderPath`.
The source does not determine or pre-compute paths.

### Reject These Patterns

| Pattern | Why rejected |
|---------|-------------|
| `ExportAsync(IAsyncEnumerable<RevisionFolder> …)` | Bypasses the source abstraction; caller constructs paths outside the orchestrator |
| `ExportAsync(IAsyncEnumerable<WorkItemRevision> …)` | No source interface — cannot be swapped for AzureDevOps vs TFS connector |
| Serialising `RevisionFolder` as revision.json | `RevisionFolder` is a path-calculation helper only; it has no work item data |

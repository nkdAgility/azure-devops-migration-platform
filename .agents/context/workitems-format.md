# WorkItems Format

## 4. WorkItems Module (Aggregate Root)

WorkItems is the primary high-fidelity module. Its on-disk layout is canonical and must not be changed.

### On-Disk Layout

```
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      <attachment files>
```

Each revision folder corresponds to exactly one revision of one work item. Attachment files belonging to that revision are stored physically beside `revision.json`.

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
}
```

`WorkItemField` carries `ReferenceName` (e.g. `"System.Title"`) and `Value` (string-serialised).
`WorkItemLink` carries `Rel` and `Url`.
`AttachmentMetadata` carries `OriginalName`, `RelativePath`, `Sha256`, and `Size`.

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

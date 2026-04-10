# Data Model: Resumable Export and Import

**Feature**: 009-resumable-export-import  
**Phase**: 1 — Design

---

## New Types

### `MigrationJobResume` (`DevOpsMigrationPlatform.Abstractions/Models/MigrationJobResume.cs`)

```csharp
/// <summary>
/// Resume options for a MigrationJob. Null on MigrationJob means Auto.
/// </summary>
public sealed record MigrationJobResume
{
    public ResumeMode Mode { get; init; } = ResumeMode.Auto;
}
```

**Fields**:

| Field | Type | Default | Description |
|---|---|---|---|
| `Mode` | `ResumeMode` | `Auto` | Controls how the agent handles existing cursor state |

---

### `ResumeMode` (`DevOpsMigrationPlatform.Abstractions/Models/MigrationJobResume.cs`)

```csharp
public enum ResumeMode
{
    /// <summary>
    /// Detect existing cursor and resume from the last processed position.
    /// This is the default behaviour when Resume is null on MigrationJob.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Delete all module cursors and the job phase record before running.
    /// The job starts from the very beginning regardless of prior progress.
    /// </summary>
    ForceFresh = 1
}
```

---

### `JobPhaseRecord` (`DevOpsMigrationPlatform.Abstractions/Checkpointing/JobPhaseRecord.cs`)

Serialised to `Checkpoints/job.phase.json` by `PhaseTrackingService`.

```csharp
public sealed record JobPhaseRecord
{
    public bool ExportCompleted { get; init; }
    public bool ImportCompleted { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

**Fields**:

| Field | Type | Description |
|---|---|---|
| `ExportCompleted` | `bool` | True when all export modules for the job have completed successfully |
| `ImportCompleted` | `bool` | True when all import modules for the job have completed successfully |
| `UpdatedAt` | `DateTimeOffset` | UTC timestamp of the last write |

**File path**: `Checkpoints/job.phase.json` (using `IStateStore` key `"Checkpoints/job.phase.json"`)

---

### `IWorkItemTargetService` (`DevOpsMigrationPlatform.Abstractions/Services/IWorkItemTargetService.cs`)

Called by `WorkItemImportOrchestrator` at each import stage. Implementation in `Infrastructure.AzureDevOps`.

```csharp
public interface IWorkItemTargetService
{
    /// <summary>Stage A: Create work item if absent; return target work item ID.</summary>
    Task<int> CreateOrGetWorkItemAsync(
        int sourceWorkItemId,
        string workItemType,
        CancellationToken cancellationToken);

    /// <summary>Stage B: Apply field values to the target work item at the given revision.</summary>
    Task ApplyFieldsAsync(
        int targetWorkItemId,
        int revisionIndex,
        IReadOnlyDictionary<string, object?> fields,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stage C: Apply links to the target work item.
    /// Implementations MUST be idempotent: links that already exist on the target
    /// MUST be silently skipped, never duplicated.
    /// On failure the method MUST throw, allowing the orchestrator cursor to remain
    /// at <c>AppliedFields</c> so the stage is retried on the next run.
    /// </summary>
    Task ApplyLinksAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemLink> links,
        CancellationToken cancellationToken);

    /// <summary>Stage D: Upload an attachment binary and return the target attachment ID.</summary>
    Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        int revisionIndex,
        string relativePath,
        Stream content,
        CancellationToken cancellationToken);
}
```

---

### Design Note: Primitive `int` for Work Item IDs

`IWorkItemTargetService` uses `int` for `sourceWorkItemId` and `targetWorkItemId`. The coding standards prefer domain-typed wrappers over primitives (`WorkItemId`). This is **accepted as a conscious trade-off**: the existing platform conventions (`WorkItemRevision`, `CursorEntry`, all existing orchestrators) also use `int` for work item IDs. Introducing a wrapper type here would require changing all callers across the platform. If a `WorkItemId` wrapper is ever introduced platform-wide, this interface is the correct place to adopt it.

---

## Modified Types

### `MigrationJob` (add `Resume`)

```csharp
// Added property:
public MigrationJobResume? Resume { get; init; }
```

`null` is treated as `ResumeMode.Auto` throughout the codebase. No default value is assigned on the property — null is the wire-compatible default.

---

### `ICheckpointingService` (add `DeleteCursorAsync`)

```csharp
// Added method:
/// <summary>
/// Deletes the cursor file for the named module.
/// No-op if the cursor does not exist.
/// </summary>
Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken);
```

---

### `IStateStore` (add `DeleteAsync`)

```csharp
// Added method:
/// <summary>
/// Deletes the state entry at the given key.
/// No-op if the key does not exist.
/// </summary>
Task DeleteAsync(string key, CancellationToken cancellationToken);
```

---

## Existing Types (Unchanged)

| Type | Location | Role |
|---|---|---|
| `CursorEntry` | Abstractions/Checkpointing | Holds `LastProcessed`, `Stage`, `UpdatedAt` |
| `CursorStage` | Abstractions/Checkpointing | Canonical stage string constants |
| `ICheckpointingService` | Abstractions/Services | Read/Write cursor; now also Delete |
| `CheckpointingService` | Infrastructure/Checkpointing | Implements `ICheckpointingService` |
| `IStateStore` | Abstractions/Storage | Low-level key-value store for cursor JSON |
| `IArtefactStore` | Abstractions/Storage | Streaming package file read/write |
| `WorkItemRevision` | Abstractions/Models | Revision data read from `revision.json` |
| `WorkItemExportOrchestrator` | Infrastructure/Export | Full export resume — unchanged |

---

## Identity Map Schema (`Checkpoints/idmap.json`)

Stored via `IStateStore` key `"Checkpoints/idmap.json"`. Read and written during import Stage A and Stage D.

```json
{
  "workItems": {
    "42": 1001,
    "99": 1002
  },
  "attachments": {
    "42:0:screenshot.png": "abc123-attachment-id",
    "42:1:design.pdf": "def456-attachment-id"
  }
}
```

**`workItems`**: `sourceWorkItemId (string) → targetWorkItemId (int)`. Written at Stage A on creation; read at Stage A on resume.

**`attachments`**: `"workItemId:revisionIndex:relativePath" → targetAttachmentId`. Written after successful upload at Stage D; read at Stage D on resume to skip re-upload.

The entire file is read on orchestrator startup (once per import run) and held in a local in-memory dictionary for the duration of the run. It is written back to `IStateStore` incrementally after each Stage A or Stage D write to keep it durable. The map is small enough (one entry per work item + one entry per attachment) that in-memory hold is safe; it is not revision-level data.

---

## State Transitions

### Export Cursor

```
No cursor → [first revision written] → cursor { lastProcessed: "WorkItems/…/folder-0/", stage: "Completed" }
                                     → [each subsequent revision] → cursor advanced
                                     → [ForceFresh] → cursor deleted (idmap preserved) → re-enumerates from beginning; skips already-written folders via ExistsAsync
```

### Import Cursor

```
No cursor → [Stage A completes] → cursor { lastProcessed: "…/folder-0/", stage: "CreatedOrUpdated" }
         → [Stage B completes] → cursor { …, stage: "AppliedFields" }
         → [Stage C completes] → cursor { …, stage: "AppliedLinks" }
         → [Stage D completes] → cursor { …, stage: "UploadedAttachments" }
         → [all stages done]  → cursor { …, stage: "Completed" }
         → [next folder]      → cursor moves to next folder, stage resets to "CreatedOrUpdated"
```

### Both-Mode Phase Record

```
No record → [export modules complete] → { exportCompleted: true, importCompleted: false }
          → [import modules complete] → { exportCompleted: true, importCompleted: true }

Re-run (Auto):
  exportCompleted: true  → skip export phase
  importCompleted: false → run import from import cursor

Re-run (ForceFresh):
  Delete job.phase.json → delete all module cursors (idmap preserved) → re-enumerate both phases from the beginning; idmap prevents duplicate target items
```

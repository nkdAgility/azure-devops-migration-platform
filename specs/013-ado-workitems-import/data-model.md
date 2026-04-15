# Data Model — Azure DevOps Work Items Import

**Feature**: 013-ado-workitems-import  
**Date**: 2026-04-15

---

## Existing Entities (consumed as-is)

### WorkItemRevision (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/WorkItemRevision.cs`  
**Role**: Deserialized from `revision.json` during import. Read-only consumption.

| Property | Type | Description |
|----------|------|-------------|
| `WorkItemId` | `int` | Source work item ID |
| `RevisionIndex` | `int` | Zero-based revision index |
| `ChangedDate` | `DateTimeOffset` | UTC timestamp of the revision |
| `Fields` | `IReadOnlyList<WorkItemField>` | Field values as of this revision |
| `ExternalLinks` | `IReadOnlyList<ExternalWorkItemLink>` | External links |
| `RelatedLinks` | `IReadOnlyList<RelatedWorkItemLink>` | Related work item links |
| `Hyperlinks` | `IReadOnlyList<HyperlinkWorkItemLink>` | Hyperlinks |
| `Attachments` | `IReadOnlyList<AttachmentMetadata>` | Attachment metadata |

**Note**: `EmbeddedImages` is referenced in `revision.json` schema but not yet on the C# record. The plan adds `EmbeddedImages` property (see New Entities).

### WorkItemComment (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/WorkItemComment.cs`  
**Role**: Deserialized from `comment.json` during import. Read-only consumption.

| Property | Type | Description |
|----------|------|-------------|
| `CommentId` | `string` | Unique comment identifier |
| `Version` | `int` | Version number (1 = original, 2+ = edits) |
| `Text` | `string` | Comment content (HTML or Markdown) |
| `RenderedText` | `string?` | HTML rendered form |
| `Format` | `string` | `"html"`, `"markdown"`, or `"plaintext"` |
| `IsDeleted` | `bool` | Soft-deletion flag |
| `CreatedBy` | `WorkItemIdentityRef` | Author identity |
| `CreatedDate` | `DateTimeOffset` | Original creation timestamp |
| `ModifiedBy` | `WorkItemIdentityRef` | Last modifier identity |
| `ModifiedDate` | `DateTimeOffset` | Last modification timestamp |

### CursorEntry (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Checkpointing/CursorEntry.cs`  
**Role**: Cursor state for resume. Used as-is.

| Property | Type | Description |
|----------|------|-------------|
| `LastProcessed` | `string` | Relative path of last processed folder |
| `Stage` | `string` | Last completed stage (canonical value) |
| `UpdatedAt` | `DateTimeOffset` | UTC timestamp of cursor update |

### CursorStage (static constants)
**Location**: `DevOpsMigrationPlatform.Abstractions/Checkpointing/CursorStage.cs`  
**Values**: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`

### MigrationJob (class)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/MigrationJob.cs`  
**Role**: Contains `Mode`, `Target`, `Modules` with extension flags. Read-only consumption during import.

### ImportContext (class)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/ImportContext.cs`  
**Role**: Context passed to `IModule.ImportAsync`. Provides `Job`, `ArtefactStore`, `StateStore`, `ProgressSink`.

---

## New Entities

### IdMapEntry (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/IdMapEntry.cs`  
**Role**: Represents a single source→target work item ID mapping.

| Property | Type | Description |
|----------|------|-------------|
| `SourceId` | `int` | Source work item ID |
| `TargetId` | `int` | Target work item ID |

### AttachmentMapEntry (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/AttachmentMapEntry.cs`  
**Role**: Represents a tracked attachment upload for idempotency.

| Property | Type | Description |
|----------|------|-------------|
| `SourceWorkItemId` | `int` | Source work item ID |
| `RevisionIndex` | `int` | Revision index where the attachment appeared |
| `RelativePath` | `string` | Relative path of the attachment file in the revision folder |
| `TargetAttachmentId` | `string` | Target-side attachment identifier (URL or GUID) |

### ImportedWorkItemResult (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/ImportedWorkItemResult.cs`  
**Role**: Result returned by `IWorkItemImportTarget` after creating or updating a work item.

| Property | Type | Description |
|----------|------|-------------|
| `TargetWorkItemId` | `int` | The ID of the work item in the target |
| `IsNewlyCreated` | `bool` | `true` if the work item was created, `false` if updated |

### EmbeddedImageMetadata (record)
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/EmbeddedImageMetadata.cs`  
**Role**: Metadata for an embedded image in a revision or comment. Consumed from `revision.json`.

| Property | Type | Description |
|----------|------|-------------|
| `OriginalUrl` | `string` | Source system URL of the embedded image |
| `RelativePath` | `string` | Local filename in the revision/comment folder |
| `Extension` | `string` | File extension (e.g. `"png"`, `"jpg"`) |
| `Sha256` | `string` | SHA-256 hash of the file content |
| `Size` | `long` | File size in bytes |

---

## New Options

### WorkItemImportOptions (sealed class)
**Location**: `DevOpsMigrationPlatform.Abstractions/Options/WorkItemImportOptions.cs`  
**Role**: Import-specific configuration bound via `IOptions<T>`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SectionName` | `const string` | `"WorkItemImport"` | Configuration section key |

**Note**: Most import behaviour is controlled by the `MigrationJob.Modules[WorkItems].Extensions` flags, not by standalone options. This options class exists as a minimal extension point for future import-specific settings (e.g., batch size, concurrency limits). It may be deferred if no immediate settings are needed.

---

## SQLite Schema (idmap.db)

**Location**: `Checkpoints/idmap.db` (inside the package)  
**Managed by**: `SqliteIdMapStore` in `DevOpsMigrationPlatform.Infrastructure`

### Tables

#### work_item_map
Stores source→target work item ID mappings.

| Column | Type | Constraint | Description |
|--------|------|-----------|-------------|
| `source_id` | `INTEGER` | `PRIMARY KEY` | Source work item ID |
| `target_id` | `INTEGER` | `NOT NULL` | Target work item ID |

#### attachment_map
Tracks uploaded attachments for idempotency during resume.

| Column | Type | Constraint | Description |
|--------|------|-----------|-------------|
| `source_work_item_id` | `INTEGER` | `NOT NULL` | Source work item ID |
| `revision_index` | `INTEGER` | `NOT NULL` | Revision index |
| `relative_path` | `TEXT` | `NOT NULL` | Relative path of the attachment file |
| `target_attachment_id` | `TEXT` | `NOT NULL` | Target attachment URL or GUID |
| — | — | `PRIMARY KEY (source_work_item_id, revision_index, relative_path)` | Composite key |

### Indexes
- `work_item_map.source_id` — primary key index (automatic)
- `attachment_map` — composite primary key index (automatic)

---

## State Transitions

### Revision Folder Processing Stages

```
┌──────────────────┐     ┌──────────────┐     ┌──────────────┐     ┌────────────────────┐     ┌───────────┐
│ CreatedOrUpdated  │ ──→ │ AppliedFields│ ──→ │ AppliedLinks │ ──→ │UploadedAttachments │ ──→ │ Completed │
└──────────────────┘     └──────────────┘     └──────────────┘     └────────────────────┘     └───────────┘
         │                       │                    │                       │
    Write cursor            Write cursor         Write cursor          Write cursor
    Check idmap.db          Apply fields         Query existing        Check idmap.db
    Create if new           via patch doc        links, skip dupes     Upload if new
    Record mapping                                                     Record upload
```

### Import Orchestrator Flow

```
Start
  │
  ▼
Read cursor from ICheckpointingService
  │
  ▼
Run IWorkItemResolutionStrategy.SeedAsync() ──→ seeds idmap.db from target
  │
  ▼
EnumerateAsync("WorkItems/") ──→ lazy IAsyncEnumerable<string>
  │
  ▼
┌─ For each folder path (lexicographic order) ─────────────────────┐
│                                                                    │
│  Skip if path <= cursor.LastProcessed                             │
│                                                                    │
│  Parse folder name:                                                │
│    - Third segment starts with 'c'? → Comment folder              │
│    - Otherwise? → Revision folder                                  │
│                                                                    │
│  Revision folder:                                                  │
│    → RevisionFolderProcessor.ProcessAsync()                        │
│    → Stages A → B → C → D → Completed                             │
│    → Cursor updated after each stage                               │
│                                                                    │
│  Comment folder:                                                   │
│    → Read comment.json                                             │
│    → Create comment on target via IWorkItemImportTarget            │
│    → Cursor updated to Completed                                   │
│                                                                    │
│  Emit ProgressEvent                                                │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
  │
  ▼
Done
```

---

## Validation Rules

### Tier 2 — Pre-flight (before import)
- Package has `WorkItems/` folder
- `manifest.json` exists and `packageVersion` is compatible
- At least one revision folder exists
- Target project specified in job contract
- If `WorkItemResolutionStrategy: TargetField`, the custom field must exist on the target

### Tier 3 — Post-flight (after import)
- Target work item count matches number of unique `workItemId` values in the package
- Sampled link count matches expected
- Sampled attachment accessibility (download test) passes

---

## Relationship Map

```
MigrationJob ──→ ImportContext ──→ WorkItemsModule.ImportAsync()
                                        │
                                        ▼
                              WorkItemImportOrchestrator
                                   │      │       │
                                   ▼      ▼       ▼
                         IArtefactStore  ICheckpointingService  IProgressSink
                         (reads package)  (cursor state)         (progress)
                                   │
                                   ▼
                         RevisionFolderProcessor
                              │      │       │
                              ▼      ▼       ▼
                    IWorkItemImportTarget  IIdMapStore  IIdentityMappingService
                    (writes to target)    (id map)     (identity resolution)
                              │
                              ▼
              IWorkItemResolutionStrategy (seeds idmap from target)
                 ├── NullResolutionStrategy
                 ├── TargetFieldResolutionStrategy
                 └── TargetHyperlinkResolutionStrategy
```

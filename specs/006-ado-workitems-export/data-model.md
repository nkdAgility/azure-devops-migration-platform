# Data Model: Work Items Export — Azure DevOps via REST API

**Feature**: `006-ado-workitems-export`  
**Phase**: 1 — Design  
**Status**: Final

---

## Entity Overview

```
ExportContext
  ├── Job: MigrationJob
  │     ├── Source: MigrationJobEndpoint  (orgUrl, project, Authentication.ResolvedAccessToken)
  │     └── Modules: List<MigrationJobModule>
  │           └── [WorkItems module] Scopes[0].Parameters
  │                 ├── "query" → WIQL filter string (optional)
  │                 └── "includeAttachments" → bool (optional, default true)
  ├── ArtefactStore: IArtefactStore       (file writes)
  ├── StateStore: IStateStore             (cursor / idmap)
  └── ProgressSink: IProgressSink        (progress events)
        │
        ▼
WorkItemsModule
  delegates to ────────────────────►  WorkItemExportOrchestrator
                                             │
                          ┌──────────────────┴─────────────────────┐
                          ▼                                         ▼
            IWorkItemRevisionSource                    IAttachmentDownloader?
                          │                                         │
            AzureDevOpsWorkItemRevisionSource         AzureDevOpsAttachmentDownloader
                          │                                         │
                 yields WorkItemRevision                   writes binary to IArtefactStore
                  (with DownloadUrl on                     returns AttachmentDownloadResult
                   each AttachmentMetadata)                (Sha256, Size)
                          │
                          ▼
                WorkItemRevision (fully populated)
                  serialised to revision.json
                  written to IArtefactStore
```

---

## Entities

### WorkItemRevision *(existing, in Abstractions — unchanged)*

| Field | Type | Notes |
|---|---|---|
| `WorkItemId` | `int` | Source work item ID |
| `RevisionIndex` | `int` | Zero-based; ascending per work item |
| `ChangedDate` | `DateTimeOffset` | UTC; used for folder date and ticks |
| `Fields` | `IReadOnlyList<WorkItemField>` | All fields for this revision |
| `ExternalLinks` | `IReadOnlyList<ExternalWorkItemLink>` | Artefact links, build refs |
| `RelatedLinks` | `IReadOnlyList<RelatedWorkItemLink>` | Work item–to–work item links |
| `Hyperlinks` | `IReadOnlyList<HyperlinkWorkItemLink>` | URL links |
| `Attachments` | `IReadOnlyList<AttachmentMetadata>` | Attachment references + metadata |

---

### AttachmentMetadata *(modified — Abstractions/Models/)*

| Field | Type | Serialised | Notes |
|---|---|---|---|
| `OriginalName` | `string` | ✅ | Filename as it appeared in ADO |
| `RelativePath` | `string` | ✅ | Filename within the revision folder (`<guid>-<originalName>`) |
| `Sha256` | `string` | ✅ | Hex SHA-256; computed during download |
| `Size` | `long` | ✅ | File size in bytes |
| `DownloadUrl` | `string?` | ❌ (`[JsonIgnore]`) | **NEW** — ADO attachment URL (ephemeral); stripped from `revision.json` |

**Key invariant**: `DownloadUrl` is always `null` after JSON round-trip. It is populated only during the active export execution.

---

### AttachmentDownloadResult *(redesigned — Abstractions/Models/)*

| Field | Type | Notes |
|---|---|---|
| `Success` | `bool` | True if downloaded successfully |
| `Sha256` | `string` | **NEW** — Hex SHA-256 computed during streaming |
| `Size` | `long` | **NEW** — File size in bytes |
| `Error` | `Exception?` | Set on failure |

**Removed**: `FilePath` — no temp file path needed; attachment is written directly to `IArtefactStore`.

**Factory methods**:
```csharp
AttachmentDownloadResult.Succeeded(string sha256, long size)
AttachmentDownloadResult.Failed(Exception error)
```

---

### ProgressEvent *(modified — Abstractions/Models/)*

New fields (additive — default value `0`, backward-compatible):

| Field | Type | Notes |
|---|---|---|
| `AttachmentsProcessed` | `int` | **NEW** — Total attachments successfully downloaded |
| `AttachmentsFailed` | `int` | **NEW** — Total attachments that failed after all retries |

Existing fields unchanged: `Module`, `Stage`, `LastProcessed`, `TotalWorkItems`, `WorkItemsProcessed`, `RevisionsProcessed`, `WorkItemId`, `Message`, `Timestamp`, `Metrics`.

---

### IAttachmentDownloader *(new interface — Abstractions/Services/)*

```csharp
public interface IAttachmentDownloader
{
    Task<AttachmentDownloadResult> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        IArtefactStore store,
        CancellationToken cancellationToken);
}
```

- `downloadUrl` — ADO attachment URL (from `AttachmentMetadata.DownloadUrl`)
- `destinationPath` — package-relative path for the binary (e.g. `WorkItems/2026-01-15/000...-42-3/abc123-screenshot.png`)
- `store` — target `IArtefactStore` (writes via `WriteStreamAsync`)
- Returns `AttachmentDownloadResult` with SHA-256 and size on success

---

### IAzureDevOpsAttachmentDownloader *(new — Infrastructure.AzureDevOps)*

```csharp
public interface IAzureDevOpsAttachmentDownloader : IAttachmentDownloader { }
```

Marker interface; enables ADO-specific DI registration while satisfying `IAttachmentDownloader` at the `Infrastructure` layer.

---

### AzureDevOpsWorkItemRevisionSource *(new — Infrastructure.AzureDevOps/Services/)*

| Dependency | Type | Notes |
|---|---|---|
| `_windowStrategy` | `IWorkItemQueryWindowStrategy` | Enumerates WIQL date-window pages |
| `_clientFactory` | `IAzureDevOpsClientFactory` | Creates `WorkItemTrackingHttpClient` |
| `_orgUrl` | `string` | From `ExportContext.Job.Source.Url` |
| `_project` | `string` | From `ExportContext.Job.Source.Project` |
| `_pat` | `string` | From `ExportContext.Job.Source.Authentication.ResolvedAccessToken` |
| `_wiqlQuery` | `string` | From module scope parameters (or default) |

Because `_orgUrl`, `_project`, `_pat`, and `_wiqlQuery` are per-export, the source is constructed inside `WorkItemsModule.ExportAsync` using a factory approach:

```csharp
// WorkItemsModule.ExportAsync
var source = new AzureDevOpsWorkItemRevisionSource(
    _windowStrategy,
    _clientFactory,
    context.Job.Source.Url,
    context.Job.Source.Project,
    context.Job.Source.Authentication?.ResolvedAccessToken ?? string.Empty,
    wiqlQuery);
```

This avoids registering per-export state in the DI container.

---

### AzureDevOpsAttachmentDownloader *(new — Infrastructure.AzureDevOps/Services/)*

| Dependency | Type | Notes |
|---|---|---|
| `_clientFactory` | `IAzureDevOpsClientFactory` | Creates `WorkItemTrackingHttpClient` for the org |
| `_resiliencePipeline` | `ResiliencePipeline` | Injected via `IResiliencePipelineProvider<string>["attachment-download"]` |

The PAT + orgUrl are passed per-call (from `AttachmentMetadata.DownloadUrl`) so the downloader stays stateless and singleton-safe.

---

### WorkItemsModule *(new — Infrastructure/Modules/)*

| Dependency | Type | Notes |
|---|---|---|
| `_orchestrator` | `WorkItemExportOrchestrator` | Drives the export loop |
| `_windowStrategy` | `IWorkItemQueryWindowStrategy` | Passed to source construction |
| `_clientFactory` | `IAzureDevOpsClientFactory` | Passed to source + downloader construction |
| `_attachmentDownloader` | `IAttachmentDownloader` | Used by orchestrator |

Implements `IDataTypeModule`:
- `Name` → `"WorkItems"`
- `DependsOn` → `[]` (no dependencies for export; identity deferred to import)
- `ExportAsync` → constructs `AzureDevOpsWorkItemRevisionSource`, calls orchestrator
- `ImportAsync` → `throw new NotImplementedException("WorkItems import is deferred to a future spec.")`
- `ValidateAsync` → `throw new NotImplementedException("WorkItems validation is deferred to a future spec.")`

---

### WorkItemExportOrchestrator *(extended — Infrastructure/Export/)*

Extended constructor:

```csharp
public WorkItemExportOrchestrator(
    IArtefactStore artefactStore,
    ICheckpointingService checkpointingService,
    IProgressSink progressSink,           // NEW
    IAttachmentDownloader? attachmentDownloader = null)  // NEW (optional; null = skip attachments)
```

Extended `ExportAsync` signature:

```csharp
public async Task ExportAsync(
    IWorkItemRevisionSource source,
    bool includeAttachments,              // NEW
    CancellationToken cancellationToken)
```

---

### IArtefactStore *(interface extension — Abstractions/Storage/)*

```csharp
// New method added to IArtefactStore
Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken);
```

Implementations:
- `FileSystemArtefactStore.WriteStreamAsync`: create parent directory + `Stream.CopyToAsync` (net10) or `Stream.CopyTo` (net481)
- `AzureBlobArtefactStore.WriteStreamAsync`: `BlobClient.UploadAsync(content, overwrite: true, ct)` (net10 only)

---

## State Transitions

Export cursor (`Checkpoints/workitems.cursor.json`) progression per revision:

```
(start)
  ↓
[WIQL date-window enumeration → Get work item IDs]
  ↓
[For each workItemId → GetRevisionsAsync → yields WorkItemRevision]
  ↓
[For each revision folder:]
  ├──► Check cursor: skip if folderPath ≤ cursor.LastProcessed && stage == Completed
  ├──► Delta-detect new attachments vs. previous revision
  ├──► Download new attachment binaries → IArtefactStore.WriteStreamAsync
  ├──► Write revision.json → IArtefactStore.WriteAsync
  └──► Write cursor { lastProcessed: folderPath, stage: "Completed", updatedAt: now }
```

On resume after crash: the cursor holds the last `Completed` folder. The orchestrator skips all folders ≤ `lastProcessed` and resumes from the next folder. Partially-written revision folders that exist on disk but were not cursored as `Completed` are overwritten on resume (idempotent write).

---

## Package Layout (no change)

```
PackageRoot/
├── WorkItems/
│   └── yyyy-MM-dd/
│       └── <ticks>-<workItemId>-<revisionIndex>/
│           ├── revision.json
│           └── <attachmentGuid>-<originalFilename>   (0 or more)
├── Checkpoints/
│   └── workitems.cursor.json
└── manifest.json
```

---

## Schema Reference (revision.json)

```json
{
  "workItemId": 42,
  "revisionIndex": 3,
  "changedDate": "2026-02-25T14:30:00.0000000Z",
  "fields": [
    { "referenceName": "System.Title", "value": "Fix login bug" },
    { "referenceName": "System.State", "value": "Active" }
  ],
  "externalLinks": [],
  "relatedLinks": [
    { "relatedWorkItemId": 7, "linkTypeEnd": "Child" }
  ],
  "hyperlinks": [],
  "attachments": [
    {
      "originalName": "screenshot.png",
      "relativePath": "f1a2b3c4-0000-0000-0000-screenshot.png",
      "sha256": "a3f1e2...",
      "size": 204800
    }
  ]
}
```

Note: `attachments[].relativePath` is the filename within the same revision folder. The `DownloadUrl` field is **absent** (JsonIgnore).

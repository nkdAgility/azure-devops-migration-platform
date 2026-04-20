# Feature Specification: Work Items Export — Azure DevOps via REST API

**Feature Branch**: `006-ado-workitems-export`
**Created**: April 7, 2026
**Status**: Draft
**Input**: User description: "Export work items from Azure DevOps via REST API, working the same way as the TFS POC but using the REST API instead of the TFS Object Model"

## Architecture References

The following documents were read before drafting this spec:

| Document | Status |
|---|---|
| `docs/architecture.md` | Confirmed accurate — CLI is a thin shell; export logic lives in the Job Engine via `IDataTypeModule` |
| `docs/modules.md` | Confirmed accurate — `WorkItemsModule` is the canonical module name; `IDataTypeModule` is the contract |
| `docs/cli.md` | Confirmed accurate — `export` command submits a job via `ControlPlaneClient`; it contains no export logic |
| `.agents/guardrails/system-architecture.md` | Confirmed — Rule 6 (no direct source→target), Rule 7 (`IArtefactStore` only), Rule 14 (lexicographic enumeration), Rule 16 (CLI contains no migration logic) |
| `.agents/guardrails/workitems-rules.md` | Confirmed — canonical folder layout, cursor-based checkpointing, attachments beside `revision.json` |
| `.agents/context/workitems-format.md` | Confirmed — `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json` is canonical |
| `.agents/context/checkpointing.md` | Confirmed — cursor file at `Checkpoints/workitems.cursor.json` |
| `.agents/context/artefact-store.md` | Confirmed — `IArtefactStore` is the only permitted file abstraction |
| `.agents/context/job-contract.md` | Confirmed — `MigrationJob.source.type = AzureDevOpsServices` with WIQL module scope |
| `docs/configuration.md` | Confirmed — module scope config via `MigrationJobModule.scopes` |

**No conflicts found.** This spec implements the first concrete realisation of `WorkItemsModule.ExportAsync` for the `AzureDevOpsServices` source type. Discrepancies (undocumented ADO-specific implementation details) are logged in `discrepancies.md`.

---

## Clarifications

### Session 2026-04-07

- Q: Where does `AzureDevOpsWorkItemRevisionSource` obtain the PAT at runtime? → A: PAT is a field on `ExportContext.Job.source` (the `MigrationJob` already in scope), consistent with how the existing `AzureDevOpsWorkItemDiscoveryService` receives it.
- Q: What is the expected behavior when all attachment download retries are exhausted for a revision? → A: Skip the attachment, record an error-level log entry and increment the `attachmentsFailed` counter in the progress event; continue processing the remaining revisions. Do not fail the entire export.
- Q: How should identity-type fields (e.g. `System.AssignedTo`, `System.CreatedBy`) be stored in `revision.json`? → A: Store as-is (raw string from ADO — display name or UPN). Identity resolution is deferred to the IdentitiesModule at import time; `WorkItemsModule` has no `DependsOn` on `IdentitiesModule` for export.
- Q: When a work item has an attachment present across multiple revisions, should the binary be downloaded for every revision or only for the revision that introduced it? → A: Delta detection — download only attachments that are new in the current revision (not present in the immediately preceding revision). Prevents re-downloading the same binary N times.
- Q: What is the ADO REST API call pattern for fetching all revisions of a work item? → A: Per-work-item calls (`WorkItemTrackingHttpClient.GetRevisionsAsync(int id)`) — no bulk revisions endpoint exists in the ADO REST API. This is O(N) serial calls, one per work item.

---

## User Scenarios & Testing

### User Story 1 — Export All Work Item Revisions to Package (Priority: P1)

As a migration operator, I need to run `devopsmigration export --config migration.json` against an Azure DevOps project and have all work item revisions — complete with fields, links, and attachments — written to the package in the canonical layout so that the package can later be imported into a different project or organisation.

**Why this priority**: This is the primary end-to-end value. Without it, no migration is possible. All other stories are enhancements or reliability improvements.

**Independent Test**: Point the exporter at a known Azure DevOps project, run export, inspect `PackageRoot/WorkItems/` — each revision must appear as `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json` with a correct JSON payload.

**Acceptance Scenarios**:

1. **Given** a valid `migration.json` pointing to an accessible Azure DevOps project, **When** I run `devopsmigration export --config migration.json`, **Then** the package contains a `revision.json` file for every revision of every work item in the project, stored in chronological folder order.
2. **Given** a work item with 5 historical revisions, **When** export completes, **Then** 5 separate revision folders exist under `WorkItems/`, each named with the UTC ticks of that revision's `changedDate`.
3. **Given** the export runs to completion, **When** I examine each `revision.json`, **Then** it contains `workItemId`, `revisionIndex`, `changedDate`, `fields`, `relatedLinks`, `externalLinks`, `hyperlinks`, and `attachments` arrays conforming to the canonical schema.
4. **Given** the operator does not specify a WIQL filter, **When** export runs, **Then** all work items in the configured project are exported.

---

### User Story 2 — Attachments Downloaded Alongside Revision (Priority: P2)

As a migration operator, I need attachment binaries to be physically stored next to their owning `revision.json` so that the package is self-contained and can be imported to any target without any dependency on the source system.

**Why this priority**: Work items with attachments are common. Without attachment export, the package is incomplete and the import will fail to reproduce the original work items faithfully.

**Independent Test**: Run export against a project with at least one work item with a file attachment. Confirm the attachment binary exists in the same folder as `revision.json` and `attachments[].relativePath` in the JSON points to the correct filename.

**Acceptance Scenarios**:

1. **Given** a work item revision that added a file attachment, **When** export runs, **Then** the attachment binary is stored in the same revision folder as `revision.json`.
2. **Given** an attachment on a revision, **When** `revision.json` is written, **Then** the `attachments` array entry includes `originalName`, `relativePath`, `sha256`, and `size`.
3. **Given** a revision with no attachments, **When** export runs, **Then** no binary files are created and `attachments` is an empty array in `revision.json`.
4. **Given** an attachment download fails transiently, **When** the system retries with exponential back-off, **Then** the attachment is eventually written and the revision folder is marked complete.

---

### User Story 3 — Resume After Interruption (Priority: P2)

As a migration operator working with large projects, I need the export to be resumable so that if it is interrupted (network failure, process kill, planned pause) it continues from where it stopped without re-exporting already-processed revisions.

**Why this priority**: Large project exports can run for hours. Without resume, any interruption forces a full restart. This is a reliability requirement for production use.

**Independent Test**: Start an export, kill the process after some work items have been written, restart export — verify that only revisions not yet in the package are fetched and written (total `revision.json` count equals expected total and no file is overwritten unnecessarily).

**Acceptance Scenarios**:

1. **Given** an export that was interrupted mid-run, **When** the export command is run again with the same config, **Then** it reads `Checkpoints/workitems.cursor.json` and skips all revision folders already marked `Completed`.
2. **Given** a cursor pointing to a revision folder whose checkpoint shows `stage: InProgress` (not `Completed`), **When** export resumes, **Then** it re-processes that revision in full — re-downloads any attachments and re-writes `revision.json` — before advancing to subsequent revisions.
3. **Given** a fully completed export that is run again, **When** the cursor shows the last item was `Completed`, **Then** the export skips all revisions and exits cleanly with zero new files written.

---

### User Story 4 — Progress Reported Through IProgressSink (Priority: P3)

As a platform operator, I need the export to emit structured progress events so that the TUI and control plane show live status and I can see throughput metrics.

**Why this priority**: Without progress events the operator cannot tell whether the export is working. Important for production, but the package is still correct without this.

**Independent Test**: Run export and observe `ProgressEvent` records arriving at the SSE stream; each must include the current work item ID and cumulative counters.

**Acceptance Scenarios**:

1. **Given** an export running against a project, **When** each revision folder is completed, **Then** a `ProgressEvent` is emitted with current work item ID, revisions processed, total work items, and attachment count.
2. **Given** the TUI is connected to the control plane, **When** the export is running, **Then** the TUI displays current work item ID, revisions processed, total revisions, and attachment count in real time.
3. **Given** an export that finishes normally, **When** the final event is emitted, **Then** the event type indicates completion with final counters.

---

### Edge Cases

- What happens when WIQL returns more than 20,000 items in a single project? (The existing `WorkItemQueryWindowStrategy` date-window fallback handles this.)
- When all retries are exhausted for an attachment download: the attachment is skipped, an error is logged, `attachmentsFailed` is incremented, and the export continues (see FR-020a).
- What happens when the source project is empty (zero work items)? The export completes immediately with zero files written and a `Completed` progress event.
- What happens if the PAT expires mid-export? The `GetRevisionsAsync` call returns 401; this is a permanent 4xx and is not retried. The export fails with a clear authentication error message.
- What happens when the package URI path does not exist or is not writable? `IArtefactStore.WriteAsync` propagates the failure; the export terminates with an error before writing any partial data.
- What happens when two revisions of different work items have the same `changedDate` ticks? The `<workItemId>` segment in the folder name disambiguates — folder paths remain unique (same ticks, different work item IDs produce different paths).

---

## Requirements

### Functional Requirements

**WorkItemsModule — Export**

- **FR-001**: `WorkItemsModule` MUST implement `IDataTypeModule`. For this feature `ImportAsync` and `ValidateAsync` MUST throw `NotImplementedException` with a message indicating they are deferred.
- **FR-002**: `WorkItemsModule.ExportAsync` MUST use `IWorkItemRevisionSource` to stream revisions. It MUST NOT reference `WorkItemTrackingHttpClient` or any ADO SDK type directly.
- **FR-003**: For each revision, `ExportAsync` MUST write `revision.json` under `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` using `IArtefactStore.WriteAsync`. The date segment MUST be derived from `WorkItemRevision.ChangedDate` in UTC.
- **FR-004**: Attachment binaries MUST be written to `IArtefactStore` in the same revision folder before `revision.json` is written. The binary filename on disk MUST be `<attachmentId>-<originalFilename>`. The `relativePath` field in `AttachmentMetadata` MUST reflect this filename.
- **FR-004a**: **Delta attachment detection** — only attachments that are new in the current revision (i.e. not present in the immediately preceding revision's attachment list) MUST be downloaded. Attachments carried forward from an earlier revision are recorded in `attachments` metadata (with `relativePath` pointing to the folder of the revision where they were first downloaded) but are NOT re-downloaded.
- **FR-005**: After all stages for a revision folder are complete, `ExportAsync` MUST write `Checkpoints/workitems.cursor.json` with `lastProcessed` = the revision folder path, `stage = "Completed"`, and `updatedAt` = current UTC time.
- **FR-006**: On process start, `ExportAsync` MUST read `Checkpoints/workitems.cursor.json` (if present) and skip folders with path ≤ `lastProcessed` when `stage = "Completed"`. If stage is not `Completed`, processing MUST resume from the next incomplete stage within that folder.
- **FR-007**: `ExportAsync` MUST emit a `ProgressEvent` via `IProgressSink` after completing each revision folder, including `workItemId`, `revisionsProcessed`, `totalWorkItems`, `attachmentsProcessed`, and elapsed duration.
- **FR-008**: All write operations MUST be performed through `IArtefactStore`. Direct filesystem calls (`File.WriteAllText`, `Directory.CreateDirectory`, etc.) are forbidden.

**AzureDevOpsWorkItemRevisionSource**

- **FR-009**: `AzureDevOpsWorkItemRevisionSource` MUST implement `IWorkItemRevisionSource` and reside in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`.
- **FR-009a**: The source MUST read `ExportContext.Job.source.Url`, `ExportContext.Job.source.project`, and the PAT from a field on `ExportContext.Job.source` to initialise `AzureDevOpsClientFactory`. No separate credential lookup or `IOptions<T>` binding is needed.
- **FR-010**: The source MUST use `WorkItemQueryWindowStrategy` to enumerate work item IDs in date windows, passing the WIQL query from the module scope and the `Url`, `project`, and PAT from `ExportContext.Job.source` to the strategy. This reuses the proven inventory paging algorithm without modification.
- **FR-011**: For each work item ID yielded by the strategy, the source MUST call `WorkItemTrackingHttpClient.GetRevisionsAsync(int id, expand: WorkItemExpand.All)` to retrieve all revisions for that work item in ascending revision-index order. There is no bulk-revisions endpoint in the ADO REST API; this is an O(N) per-work-item call pattern.
- **FR-012**: Fields MUST be mapped to `WorkItemField` using the reference name key (e.g. `System.Title`) and the field value serialised to a string. Identity-type fields (`System.AssignedTo`, `System.CreatedBy`, `System.ChangedBy`, etc.) MUST be stored as-is (raw display name or UPN string from ADO). No identity resolution is performed at export time; that is deferred to the IdentitiesModule at import time.
- **FR-013**: Work item relations MUST be mapped per relation type: `System.LinkTypes.Related` → `RelatedWorkItemLink`, `ArtifactLink` / external URLs → `ExternalWorkItemLink`, `Hyperlink` → `HyperlinkWorkItemLink`.
- **FR-014**: Attachment metadata MUST be mapped from relations with `rel = "AttachedFile"`: the `url` field provides the download URL; `originalName` is extracted from the relation attributes.
- **FR-015**: The source stream MUST be lazy. No list of all work item IDs or all revisions may be accumulated in memory. Each revision is yielded as it is fetched.
- **FR-016**: All async SDK calls MUST propagate `CancellationToken`.

**Attachment Download**

- **FR-017**: A dedicated `IAzureDevOpsAttachmentDownloader` interface and `AzureDevOpsAttachmentDownloader` implementation MUST reside in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`.
- **FR-018**: The downloader MUST stream the attachment binary directly to `IArtefactStore.WriteStreamAsync` without buffering the entire file into a `byte[]` or `MemoryStream`.
- **FR-019**: The downloader MUST compute the SHA-256 hash of the streamed content and return it as part of the result, to be stored in `AttachmentMetadata.Sha256`.
- **FR-020**: Attachment downloads MUST be retried with exponential back-off (max 8 retries) when a transient HTTP error occurs (5xx, 408, 429). Permanent failures (4xx other than 408/429) MUST not be retried.
- **FR-020a**: When all retries are exhausted for an attachment, the module MUST log an error-level entry, increment the `attachmentsFailed` counter in the `ProgressEvent`, and continue processing subsequent revisions. The export MUST NOT terminate due to a single attachment failure.

**Configuration**

- **FR-021**: The WIQL query MUST be read from `MigrationJobModule.scopes[0].parameters["query"]`. If absent, the default is `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]`.
- **FR-022**: `includeAttachments` MUST be read from `MigrationJobModule.scopes[0].parameters["includeAttachments"]` (default: `true`). When `false`, attachment binaries are skipped but `attachments` metadata is still populated in `revision.json`.

### Key Entities

- **`WorkItemRevision`** (existing, Abstractions): Serialised to `revision.json`. Contains `WorkItemId`, `RevisionIndex`, `ChangedDate`, `Fields`, `ExternalLinks`, `RelatedLinks`, `Hyperlinks`, `Attachments`.
- **`WorkItemsModule`** (new): Implements `IDataTypeModule` with `DependsOn = []` (no dependency on `IdentitiesModule` for export; identity fields are stored raw). Orchestrates streaming, `IArtefactStore` writes, cursor updates, and progress events.
- **`AzureDevOpsWorkItemRevisionSource`** (new): Implements `IWorkItemRevisionSource`. Uses `WorkItemQueryWindowStrategy` + `WorkItemTrackingHttpClient`.
- **`IAzureDevOpsAttachmentDownloader` / `AzureDevOpsAttachmentDownloader`** (new): Downloads attachment binaries as streams; computes SHA-256.
- **`WorkItemCursor`** (new JSON schema): Maps to `Checkpoints/workitems.cursor.json`. Fields: `lastProcessed` (string), `stage` (string), `updatedAt` (ISO 8601).

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: A project with 10,000 work items (any revision count) can be exported to completion without the process exceeding 512 MB of working memory.
- **SC-002**: An interrupted export that is restarted re-processes zero already-completed revision folders (zero duplicate `revision.json` writes).
- **SC-003**: Every `revision.json` in the output package is valid JSON and passes schema validation against the `WorkItemRevision` schema.
- **SC-004**: The total count of `revision.json` files written equals the total revision count reported by `discovery inventory` for the same project and query scope.
- **SC-005**: All downloaded attachment binaries match the source file byte-for-byte (SHA-256 verified for 100% of attachments).
- **SC-006**: Re-running a completed export produces no changes to the package (idempotent when no source changes have occurred).

---

## Assumptions

- The ADO REST API (`GetRevisionsAsync` with `$expand=All`) returns all field values, all relation types, and all attachment metadata for a given revision in a single call per revision batch.
- Attachment download uses the URL embedded in the `AttachedFile` work item relation — no separate attachment resolution endpoint is needed.
- `WorkItemQueryWindowStrategy` is production-ready and is reused without modification (it is already exercised by the inventory path).
- Authentication is PAT only for v1. OAuth and interactive browser auth are out of scope. The PAT is carried on `ExportContext.Job.source` and passed directly to `AzureDevOpsClientFactory`.
- Single-project export only (the WIQL query must be scoped to one `[System.TeamProject]`). Multi-project batching is out of scope.
- The `MigrationAgent` infra (constructing `ExportContext`, providing `IArtefactStore`, `IStateStore`, `IProgressSink`) is already functional. This feature does not include changes to `MigrationAgent`.
- `WorkItemsModule.ImportAsync` and `WorkItemsModule.ValidateAsync` are deferred to a later spec.
- `WorkItemsModule.DependsOn` is empty for export: identity fields are stored as raw strings from ADO (display name / UPN). The `IdentitiesModule` resolves identity mappings at import time only.
- Clock collisions (two revisions with identical `changedDate` ticks for different work items) are handled by the lexicographic folder naming — the `<workItemId>` segment disambiguates.
- Attachment delta detection compares relations of the current revision against the immediately preceding revision in the fetched revision list. If the work item has only one revision, all its attachments are treated as new.
- The ADO REST API (`GetRevisionsAsync`) call pattern is O(N) per work item; there is no bulk-revisions endpoint. This is expected and acceptable for v1.


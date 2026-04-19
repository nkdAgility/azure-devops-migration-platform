# Research: Work Item Comments and Embedded Images Export

**Feature**: `010-workitem-comments-images`  
**Date**: 2026-04-10  
**Status**: Complete — all NEEDS CLARIFICATION items resolved

---

## 1. ADO Comments API — Rate, Pagination, Shape

**Decision:** Use ADO REST v7.1-preview.4 `GET /wit/workItems/{id}/comments` with continuation-token pagination.

**Rationale:**
- Confirmed via live API docs: the Comments API is a separate, paginated endpoint (`continuationToken` based).
- Each comment has `commentId`, `version`, `text`, `format` (markdown | html), `renderedText`, `createdBy`, `createdDate`, `modifiedBy`, `modifiedDate`, `isDeleted`.
- Comment edits are represented as separate versions; the API returns the latest by default. Fetching all versions requires `GET /comments/{commentId}/versions`.
- Supports `$expand=renderedText` to get HTML rendering of Markdown comments in one call.

**Alternatives considered:** Using `System.History` field on the work item revision — this only captures comments for old TFS versions (pre-2018) and does not include editing history or `isDeleted` state.

---

## 2. ADO Comments on TFS (older versions)

**Decision:** TFS < 2018 Update 2 does not expose the Comments API. For those sources, `CommentsSubModule` is a no-op; comments are captured via `System.History` field in revision data (already exported).

**Rationale:** The `WorkItemTrackingHttpClient` Comments API was added in TFS 2018.2. Earlier versions return 404 or MethodNotAllowed. Sub-module must tolerate this gracefully.

**Implementation note:** The `AzureDevOpsWorkItemCommentSource` should catch `VssServiceException` / `ProjectDoesNotExistException` / HTTP 404|405 and log a warning, then return empty.

---

## 3. HTML vs. Markdown Field Detection

**Decision:** Use the `multilineFieldsFormat` dictionary on the `WorkItem` REST response to determine which fields are Markdown vs HTML. Fall back to treating all HTML-like fields as HTML and Markdown-flagged fields as Markdown.

**Known HTML fields** (always HTML in ADO Services):
- `System.Description`
- `Microsoft.VSTS.Common.AcceptanceCriteria`
- `Microsoft.VSTS.TCM.ReproSteps`  
- `Microsoft.VSTS.TCM.SystemInfo`
- Any field of type `Html` in the process template

**Known Markdown fields** (process-dependent, user-configurable):
- Custom fields with `Microsoft.VSTS.Common.MarkdownDescription` type or user-set via the `multilineFieldsFormat` dict

**Research outcome:** The `WorkItem` REST response includes `multilineFieldsFormat` as a `Dictionary<string, string>` where keys are field reference names and values are `"html"` or `"markdown"`. This is the authoritative source.

---

## 4. Embedded Image URL Pattern in ADO

ADO stores pasted/dragged images via a private attachment endpoint. The URL patterns observed in production:

```
https://dev.azure.com/{org}/{project}/_apis/wit/attachments/{guid}?fileName={name}
https://{org}.visualstudio.com/{project}/_apis/wit/attachments/{guid}?fileName={name}
https://dev.azure.com/{org}/{project}/_workitems/edit/{id}/attachments/{guid}/{filename}
```

Sometimes embedded in Tiptap/Monaco editor as relative references like `/_apis/wit/attachments/...` — these must be absolutised using the organisation URL before downloading.

**Decision:** Pattern match using the source organisation hostname (`dev.azure.com/{org}` or `{org}.visualstudio.com`). Relative URLs starting with `/_apis/` are treated as hosted on the source org.

---

## 5. HTTP Client Strategy for Image Download

**Decision:** Reuse the same `VssConnection` / `PersonalAccessToken` credential used for ADO API calls. Images served from `dev.azure.com` and `*.visualstudio.com` require the same PAT.

**Approach:** Inject `IAdoHttpClientFactory` (or reuse `IAzureDevOpsClientFactory`) into `EmbeddedImagesSubModule`. Pass Authorization header `Basic {base64(:{pat})}`.

**Resiliency:** 
- HTTP 429: Respect `Retry-After` header; exponential back-off on 429 and 5xx (`.AddPolicyHandler(Polly retry)` via `Microsoft.Extensions.Http.Polly`).
- HTTP 4xx (except 429): Preserve original URL, emit OTel warning. No retry.
- Timeout: Configurable, default 30 s per image.

---

## 6. SHA-256 Filename Deduplification

**Decision:** Download image to a `MemoryStream`; compute `SHA256.HashData(bytes)`; write to `{folderPath}{sha256hex}.{ext}`. Extension is inferred from the `Content-Type` response header.

**Content-Type → Extension mapping:**
| Content-Type | Extension |
|---|---|
| image/png | .png |
| image/jpeg | .jpg |
| image/gif | .gif |
| image/webp | .webp |
| image/svg+xml | .svg |
| image/bmp | .bmp |
| image/tiff | .tiff |
| (fallback) | .bin |

Deduplication within one parent folder: before writing, check `IArtefactStore.ExistsAsync("{folderPath}{sha256}.{ext}")`. If it exists, skip the write but still rewrite the URL in the content.

---

## 7. Comment Version History Fetching

**Decision:** Every version is fetched. The Comment Versions endpoint is: `GET /wit/workItems/{id}/comments/{commentId}/versions`. Each version response has the same schema as a `Comment` but `version` field increments.

**Storage:** Original version stored in its `createdDate` folder. Each subsequent version stored in a folder at its `modifiedDate` ticks. The `version` integer in `comment.json` identifies which version the folder represents.

**API call sequence per work item:**
1. `GET /comments?$top=200&order=asc` (paginated via `continuationToken`)
2. For each comment: `GET /comments/{commentId}/versions` to get edit history
3. Emit a `CommentFolder` per version

---

## 8. Cursor Strategy for Comments

**Decision:** Extend `Checkpoints/workitems.cursor.json` with a `commentsLastProcessed` field, OR use a dedicated `Checkpoints/workitems-comments.cursor.json`.

**Chosen:** Dedicated `Checkpoints/workitems-comments.cursor.json` to keep the comments sub-module independently resumable without interfering with the revision cursor.

**Cursor schema:**
```json
{
  "lastProcessedWorkItemId": 12345,
  "stage": "Completed",
  "updatedAt": "2026-04-10T12:00:00Z"
}
```

`lastProcessedWorkItemId` is an integer (the work item ID), not a folder path, because comments do not produce folder paths aligned with the revision orderer.

---

## 9. Architecture Fit — Where CommentsSubModule Lives

**Decision:** `CommentsSubModule` and `EmbeddedImagesSubModule` are **not** separate `IDataTypeModule` implementations. They are services called by `WorkItemsModule` (or its orchestrator) during `ExportAsync`. This avoids proliferating top-level modules for what are sub-capabilities of the work items domain.

**Concrete approach:** `WorkItemExportOrchestrator` is extended (or replaced with an extended version) that, after writing `revision.json`, invokes `IEmbeddedImageExportService` to scan fields and download images. After all revisions for a work item are complete, `ICommentExportService.ExportCommentsAsync(workItemId)` is called.

**Interfaces — all in `DevOpsMigrationPlatform.Abstractions`:**
- `IWorkItemCommentSource` — source connector abstraction (mirrors `IWorkItemRevisionSource`)
- `IEmbeddedImageExportService` — scans content and downloads images
- `IWorkItemCommentExportService` — fetches and persists comments for a work item

**Implementations — in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`:**
- `AzureDevOpsWorkItemCommentSource`
- `AzureDevOpsEmbeddedImageDownloader` (HTTP client, SHA-256 naming)

**Orchestration — in `DevOpsMigrationPlatform.Infrastructure`:**
- `WorkItemCommentExportOrchestrator`
- `EmbeddedImageExportService`

---

## 10. OTel Instrumentation

**Decision:** Follow existing `WorkItemsModule` patterns.

**Activity source name:** `DevOpsMigrationPlatform.WorkItems.Comments` and `DevOpsMigrationPlatform.WorkItems.EmbeddedImages`

**Counters:**
- `workitems.comments.fetched` — total comments fetched
- `workitems.comments.folders_written` — total comment folders written
- `workitems.images.downloaded` — total embedded images successfully downloaded
- `workitems.images.skipped` — images not downloaded (inaccessible, external)

**Structured log events:**
- `[CommentsSubModule] {Count} comments fetched for work item {WorkItemId}`
- `[EmbeddedImages] Image {Url} downloaded as {LocalPath} ({Bytes} bytes)`
- `[EmbeddedImages] Image {Url} inaccessible: {StatusCode} — URL preserved`
- `[EmbeddedImages] Image {Url} is external — skipped`

---

## 11. Cursor Key Design — Constitution Principle IV Justification

**Cursor location:** `Checkpoints/workitems-comments.cursor.json`

**Cursor schema:**
```json
{
  "lastProcessedWorkItemId": 12345,
  "stage": "Completed",
  "updatedAt": "2026-04-10T12:00:00Z"
}
```

**Why `int lastProcessedWorkItemId` qualifies as an "equivalent key" (Constitution Principle IV):**

The Constitution requires cursor keys to be "never an ID or timestamp alone" — the phrase "equivalent key" allows for any token that uniquely and monotonically identifies the resumption point. For comments:

1. **Atomic per-work-item processing:** All versions of all comments for a single work item are fetched, processed, and written as a cohesive unit. The next resume point is always the next work item.
2. **Monotonic assignment:** Work item IDs in ADO are assigned sequentially and never reused. Scanning from `lastProcessedWorkItemId + 1` is safe and deterministic.
3. **Uniqueness:** Work item ID is globally unique within an ADO organisation, sufficient to pinpoint the resume work item.
4. **Streaming invariant:** Because comments are processed per-work-item (not per-comment), the work item ID is the natural and minimal resume token.

This design parallels the existing `workitems.cursor.json` which also stores `lastProcessedWorkItemId`. The comments cursor is a separate file to distinguish comment export progress from revision export progress (per Principle IV: "each module has a cursor file under Checkpoints/").

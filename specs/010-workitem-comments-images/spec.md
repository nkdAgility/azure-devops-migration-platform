# Feature Specification: Work Item Comments and Embedded Images Export

**Feature Branch**: `010-workitem-comments-images`  
**Created**: 2026-04-10  
**Status**: Draft  
**Input**: User description: "Export work item comments (separate API) and download embedded images from HTML and Markdown fields in all work item revisions and comments"

## Architecture References

The following documents were read during specification authoring:

| Document | Status |
| -------- | ------ |
| `agents.md` | Confirmed accurate |
| `docs/module-development-guide.md` | Confirmed accurate — module contract applies |
| `.agents/guardrails/architecture-boundaries.md` | Confirmed accurate — attachments-beside-revision rule, streaming, IArtefactStore rules all apply |
| `.agents/context/workitems-format-summary.md` | **Discrepancy** — does not yet describe comments or embedded-image sub-folders; logged in `discrepancies.md` |
| `.agents/context/migration-package-concept.md` | **Discrepancy** — does not yet describe comment sub-folders or embedded-image files beside documents; logged in `discrepancies.md` |
| Azure DevOps REST API — Comments (7.1-preview.4) | External reference — confirmed via live API docs fetch. Comments live at `/wit/workItems/{id}/comments` — a **separate paginated endpoint** from revisions. Supports HTML and Markdown formats with embedded images. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Export Work Item Comments (Priority: P1)

A migration engineer runs an export of a project. Work items in Azure DevOps have a **Comments** panel that is stored separately from the revision history — it has its own API endpoint and its own version history. Today those comments are silently omitted from the migration package. After this feature, every comment on every work item is fetched from the dedicated Comments API and saved in the package alongside the work item's revisions, preserving author, date, edit history, and content format (HTML or Markdown).

**Why this priority**: Comments are the primary collaboration record of a work item. Losing them is a high-visibility data loss that users notice immediately on the target system. All other stories depend on the package having comments present.

**Independent Test**: Run an export of a project containing a work item with at least one comment. Find the date folder matching the comment's creation date. Confirm a sub-folder named `<ticks>-<workItemId>-c<commentId>/` exists and contains `comment.json` with the correct text, author, and date.

**Acceptance Scenarios**:

1. **Given** a work item with three comments exists in the source project, **When** an export runs, **Then** the package contains three comment sub-folders (one per comment) in the date folders matching each comment's `createdDate`, each containing a `comment.json` with `commentId`, `text`, `format`, `createdBy`, and `createdDate`.
2. **Given** a work item with no comments, **When** an export runs, **Then** no comment sub-folders are created for that work item.
3. **Given** a project with more than one page of comments on a single work item (more than the default page size), **When** an export runs, **Then** all pages are fetched and a sub-folder exists for each comment.
4. **Given** an export is interrupted mid-way and then resumed, **When** the resumed export runs, **Then** comments already exported are not re-fetched, and the remaining work items' comments are completed.

---

### User Story 2 - Download Embedded Images from HTML Fields (Priority: P2)

Work item fields such as Description, Acceptance Criteria, and Repro Steps use an HTML editor that allows authors to paste or drag images directly into the content. Those images are stored as ADO-hosted URLs embedded in the HTML. Today those URLs are captured in the field value text, but the actual image files are not downloaded — so on import the images are dead links. After this feature, the exporter scans every HTML field in every revision, identifies embedded image URLs pointing to the source ADO organisation, downloads each image, stores it beside the revision, and rewrites the field value to use a relative path so the package is self-contained and portable.

**Why this priority**: Many organisations use inline images extensively in descriptions and acceptance criteria. Without the images, field content is incomplete and the target work items are difficult to use.

**Independent Test**: Run an export of a project containing a work item whose Description field contains an embedded image. Open the revision folder and confirm the image file is present. Confirm the `revision.json` field value contains a relative reference to the image rather than the original ADO URL.

**Acceptance Scenarios**:

1. **Given** a work item revision has an HTML field containing an `<img src="https://dev.azure.com/...">` tag, **When** an export runs, **Then** the image is downloaded and stored beside `revision.json`, and the `img src` value in the stored field is replaced with the relative filename.
2. **Given** a work item revision has the same image embedded in two different fields, **When** an export runs, **Then** the image is downloaded only once and both field values reference the same file.
3. **Given** an HTML field references an external image URL (not hosted on the source ADO organisation, e.g. a public CDN), **When** an export runs, **Then** the URL is left unchanged and a warning is recorded in the package log.
4. **Given** an embedded image URL is inaccessible (returns 4xx/5xx), **When** an export runs, **Then** the export continues, the URL is preserved as-is in the stored field value, and a warning entry is written to the package log.

---

### User Story 3 - Download Embedded Images from Markdown Fields and Comments (Priority: P3)

Work item fields and comments can use Markdown format. Markdown allows image embeds via `![alt](url)` syntax, pointing to the same ADO-hosted storage as HTML images. This story extends the image scanning and download behaviour of User Story 2 to also handle Markdown-formatted fields and Markdown-formatted comments (from User Story 1).

**Why this priority**: Extends the same guarantee of self-contained packages to Markdown content. Depends on User Stories 1 and 2 being fully functional.

**Independent Test**: Run an export of a project containing a work item where a comment uses Markdown format and includes a `![](https://dev.azure.com/...)` image link. Confirm the image file is present inside the comment's sub-folder beside `comment.json`.

**Acceptance Scenarios**:

1. **Given** a Markdown-format comment contains an `![alt text](https://dev.azure.com/...)` image link, **When** an export runs, **Then** the image is downloaded and stored beside `comment.json` inside the comment sub-folder, and the Markdown image URL is replaced with the relative filename.
2. **Given** a work item revision field uses Markdown format and embeds an image, **When** an export runs, **Then** the image is downloaded and the Markdown reference is rewritten to a relative path.

---

### Edge Cases

- A work item may have comment sub-folders spread across many date folders. The streaming import must enumerate all date folders for a work item to reconstruct its full set of comments and revisions in order.
- A comment edited on the same date as its creation will produce two sub-folders in the same date folder — one at `createdDate` ticks and one at `modifiedDate` ticks. If the ticks are different (even fractionally) they sort correctly; if they are identical the `version` field inside `comment.json` distinguishes them.
- If a comment's `modifiedDate` equals its `createdDate` (never been edited), only one folder is written.
- The same image URL appearing in both a revision field and a comment on the same work item results in two separate copies — the image is downloaded once into the revision folder and once into the comment folder.
- Deleted comments (`isDeleted: true`) are excluded by default; setting `modules.workItems.scopes.comments.includeDeleted = true` in the scenario config includes them.
- ADO-hosted images use the same authentication credential as API calls; the image downloader reuses that credential.
- If an ADO-hosted image cannot be fetched (auth error, deleted resource, network error), the original URL is preserved in the stored content and a warning is written to the log — export continues.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The export MUST fetch all comments for each exported work item from the Azure DevOps Comments API (`/wit/workItems/{id}/comments`), which is a separate, paginated endpoint from the work item revisions API.
- **FR-002**: Each fetched comment MUST be stored in its own sub-folder inside the date folder matching the comment's creation or modification date. The original comment (version 1) is placed in the date folder matching its `createdDate`; each subsequent edit version is placed in the date folder matching its `modifiedDate`. The folder MUST be named `<ticks>-<workItemId>-c<commentId>/` (using the respective date's ticks as the sort key) and MUST contain a file named `comment.json`. Comment sub-folders sort chronologically alongside revision sub-folders within the same `WorkItems/yyyy-MM-dd/` date folders, enabling streaming import to process all entries in the correct order.
- **FR-003**: `comment.json` MUST include: `commentId`, `version`, `text`, `format` (markdown or html), `renderedText` (when available), `createdBy`, `createdDate`, `modifiedBy`, `modifiedDate`, and `isDeleted`.
- **FR-004**: The comments export MUST use cursor-based checkpointing so that a resumed export continues from the last successfully processed work item, not from the beginning.
- **FR-005**: For every HTML-format field in every work item revision, the exporter MUST scan the stored field value for `<img src="...">` tags pointing to the source ADO organisation and download each referenced image.
- **FR-006**: For every Markdown-format field in every work item revision, the exporter MUST scan the stored field value for `![...](url)` image syntax pointing to the source ADO organisation and download each referenced image.
- **FR-007**: For every HTML-format comment, the exporter MUST scan the `text` field in `comment.json` for embedded images and download them.
- **FR-008**: For every Markdown-format comment, the exporter MUST scan the `text` field in `comment.json` for embedded images and download them.
- **FR-009**: Downloaded embedded images MUST be stored **beside their parent document** — inside the revision folder (beside `revision.json`) for revision-field images, and inside the comment folder (beside `comment.json`) for comment images. Each downloaded image MUST be named by the SHA-256 hash of its content with the extension inferred from the HTTP `Content-Type` response header (e.g. `abc123def456.png`).
- **FR-010**: After downloading, the field value or comment text stored in the package MUST have the original ADO-hosted image URL replaced with a relative path to the downloaded file.
- **FR-011**: Image URLs that do not point to the source ADO organisation MUST NOT be downloaded; the original URL MUST be preserved and a warning MUST be written to the package log.
- **FR-012**: If an embedded image URL is inaccessible (HTTP 4xx or 5xx), the export MUST continue, preserve the original URL in the stored content, and write a warning to the package log — it MUST NOT fail the entire export.
- **FR-013**: The same image URL appearing in multiple fields of the same parent document (same revision folder or same comment folder) MUST be downloaded only once; all references within that document are rewritten to the same local filename. Cross-document deduplication (the same URL in a revision field and a comment) results in two separate file copies — one in the revision folder and one in the comment folder.
- **FR-014**: The comments export and embedded-image download behaviour MUST be implemented as focused services (`IWorkItemCommentExportService`, `IEmbeddedImageExportService`) called from within `WorkItemsModule` during export orchestration. These are not top-level `IDataTypeModule` implementations — they are sub-services of the WorkItems module. All file operations MUST go through `IArtefactStore` exclusively.
- **FR-015**: Deleted comments (`isDeleted: true`) MUST be excluded from the export by default. The configuration flag `modules.workItems.scopes.comments.includeDeleted` (boolean, default `false`) enables inclusion of deleted comments.
- **FR-016**: When a comment has been edited, every version MUST be stored as a separate comment sub-folder (always-on, not opt-in). The original comment is placed at its `createdDate` ticks; each subsequent edit is placed at its `modifiedDate` ticks, using the same `<ticks>-<workItemId>-c<commentId>/` naming convention. The `version` field inside `comment.json` identifies which version that folder represents.
- **FR-017**: The image downloader MUST NOT restrict by MIME type or file extension. Any image format served by an ADO-hosted image URL (PNG, JPEG, GIF, animated GIF, WebP, SVG, or other) MUST be downloaded and stored. The local filename is derived from the SHA-256 hash of the content with the extension inferred from the `Content-Type` response header.

### Key Entities

- **WorkItemComment**: A single comment record from the ADO Comments API, including `commentId`, `version`, `text`, `format` (html or markdown), `renderedText`, author identity, and date metadata. Stored as `comment.json` inside its own sub-folder.
- **CommentFolder**: A sub-folder in the date structure (`<ticks>-<workItemId>-c<commentId>/`) containing a `comment.json` and any embedded-image files. Placed in the date folder corresponding to the comment's `createdDate` (original) or `modifiedDate` (each edit).
- **EmbeddedImage**: An image reference found inside an HTML or Markdown field value or comment text, with its resolved download status and local SHA256-based filename after download.
- **ImageScanResult**: The outcome of scanning a single document (revision or comment) — a list of discovered `EmbeddedImage` entries and their URL-to-filename rewrite mappings for that document.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a full export of a project that uses comments, 100% of non-deleted comments for every exported work item are present in the package.
- **SC-002**: After a full export, every ADO-hosted image embedded in any HTML or Markdown revision field or comment across all exported work items is present as a file in the package.
- **SC-003**: Every field value and comment text stored in the package that originally contained an ADO-hosted image URL now contains a relative local path — no original ADO image URLs remain in stored content for successfully downloaded images.
- **SC-004**: Exporting a project with thousands of comments across many work items completes without exhausting available memory — comments for each work item are processed and written before moving to the next.
- **SC-005**: After an interrupted export is resumed, at most one work item's worth of comments is reprocessed — all previously completed work items are not re-fetched.
- **SC-006**: An export that encounters one or more inaccessible embedded images completes successfully; the package log contains a warning entry for each inaccessible image URL.

## Clarifications

### Session 2026-04-10

- Q: Where in the package should comment data live? → A: Each comment gets its own sub-folder inside the date folder that corresponds to the comment's `createdDate`, using the naming convention `<ticks>-<workItemId>-c<commentId>/`, containing a `comment.json` file. This places comments chronologically alongside revision sub-folders in the same date-folder structure, so streaming import processes them in the correct order.
- Q: Should embedded image downloading be always active, or controllable by configuration? → A: Opt-in, enabled by default — a configuration flag can disable it.
- Q: How should the exporter handle HTTP 429 when fetching comments pages or downloading images? → A: Respect `Retry-After` header and retry with exponential back-off.
- Q: What filename should a downloaded embedded image be given on disk? → A: SHA256 hash of the image content, with the extension inferred from the HTTP `Content-Type` header (e.g. `abc123def456.png`). Images live **beside their parent document** — beside `revision.json` in a revision folder, and beside `comment.json` in a comment folder. No shared work-item-level manifest.
- Q: If the same ADO-hosted image URL appears in multiple fields of the same revision, how many times is it downloaded? → A: Only once per parent folder. The same URL in two fields of the same revision produces one file beside `revision.json`, and both field values reference that same local filename.
- Q: Comment folder naming convention? → A: `<ticks>-<workItemId>-c<commentId>/` containing `comment.json`. Both signals are used together: the `c` prefix before the commentId in the folder name, and the inner file being named `comment.json` (vs `revision.json` in revision folders).
- Q: Which date determines a comment folder's placement? → A: The comment's `createdDate` determines the date folder. An edit to a comment is a **separate new entry** — it creates another folder at the edit's `modifiedDate` ticks, using the same commentId. Multiple folders may exist for the same commentId (original at `createdDate` plus each edit at its `modifiedDate`), all sorted chronologically in the date folders alongside revisions.
- Q: What does the package contain for a comment edited multiple times? → A: One folder per version — original at `createdDate`, each edit at its `modifiedDate`. This is always-on behaviour, not an opt-in.

## Assumptions

- Work item comments via this Comments API are only available on Azure DevOps Services and TFS 2018 Update 2 or later. For older TFS versions lacking this endpoint, the comments sub-module is a no-op and comments stored in `System.History` (the legacy comment mechanism) are already captured via revision fields.
- The same authentication credential used to export work item revisions is also valid for fetching comments and for downloading ADO-hosted images from authenticated ADO storage URLs.
- Comments using HTML format have `renderedText` available from the ADO API; the raw `text` field is stored as the canonical value and `renderedText` as a supplementary field.
- Embedded images in work item fields and comments are always referenced by absolute URL. Base64-encoded inline images (`data:image/...`) are not hosted resources and are left as-is.
- The `WorkItemsModule` already exports revisions; this feature adds `CommentsSubModule` and `EmbeddedImagesSubModule` as composable units within the same module pass.
- Comment sub-folders (`<ticks>-<workItemId>-c<commentId>/`) are placed in `WorkItems/yyyy-MM-dd/` date folders alongside revision sub-folders using the same date-folder naming convention. A work item's comment folders may span many different date folders across its lifespan.
- When a comment is edited, the original is stored at the `createdDate` and each edit is stored as an additional folder at its `modifiedDate`. The streaming import reader processes these in chronological order as it enumerates date folders.
- Image deduplication is scoped per parent document folder. The same URL appearing in multiple fields of the same revision is downloaded once to that revision folder. The same URL appearing in a revision field and a comment field results in two separate image file copies (one per folder). Cross-document and cross-work-item deduplication are out of scope.
- OTel instrumentation for sub-modules follows existing `WorkItemsModule` patterns; specific span and metric names are defined in plan.md.
- The docs `.agents/context/workitems-format-summary.md` and `.agents/context/migration-package-concept.md` do not yet describe comment sub-folders or embedded-image handling; these will be updated as part of implementation (see `discrepancies.md`).

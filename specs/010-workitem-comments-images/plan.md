# Implementation Plan: Work Item Comments and Embedded Images Export

**Branch**: `010-workitem-comments-images` | **Date**: 2026-04-10 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/010-workitem-comments-images/spec.md`

## Summary

Work item comments are stored in a **separate, paginated ADO REST API** (`/wit/workItems/{id}/comments`) that is completely independent of the revision history API. Today comments are silently omitted from the migration package. Additionally, HTML and Markdown fields in both revisions and comments embed images as ADO-hosted URLs — those images resolve on the source organisation but are dead links on any target tenant.

This feature adds two focused sub-services to the existing `WorkItemsModule`:

1. **`WorkItemCommentExportService`** — streams every comment version for a work item from the Comments API and writes one `comment.json` per version into a `<ticks>-<workItemId>-c<commentId>/` sub-folder, placed chronologically alongside revision sub-folders inside `WorkItems/yyyy-MM-dd/`.
2. **`EmbeddedImageExportService`** — scans HTML (`<img src>`) and Markdown (`![](url)`) field values and comment text fields, downloads each ADO-hosted image via an authenticated HTTP client with Polly back-off, writes the bytes named `<sha256>.<ext>` beside the parent document, and rewrites field values to relative local paths.

Both services use `IArtefactStore` exclusively for all package I/O and integrate cursor-based checkpointing for resumability.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`net10.0`); Abstractions project multi-targets `net481;net10.0`  
**Primary Dependencies**: `Microsoft.TeamFoundation.WorkItemTracking.WebApi` (ADO Comments API v7.1-preview.4), `System.Net.Http` + `Microsoft.Extensions.Http` + Polly (HTTP image download), `HtmlAgilityPack` (HTML img scanning), `System.Text.RegularExpressions` (Markdown img scanning), `System.Security.Cryptography` (SHA-256 naming)  
**Storage**: `IArtefactStore` abstraction (FileSystem or AzureBlob implementations); cursor files under `Checkpoints/`  
**Testing**: Reqnroll + MSTest v3 (`[TestCategory("BDD")]`); MsTest for unit tests  
**Target Platform**: Windows / Linux via .NET 10  
**Project Type**: Library sub-services added to existing worker module  
**Performance Goals**: Memory-constant — one work item's comments processed before the next; no in-memory accumulation of all comments  
**Constraints**: Image download with 30s per-request timeout; retry 3× with Polly exponential back-off + Retry-After respect; inaccessible images must not abort export  
**Scale/Scope**: Projects with up to 100k work items, each with O(100) comments; images up to ~50 MB each

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Confirmed — all files in `/.agents/guardrails/`, all files in `/.agents/context/`, and all relevant `/docs/` files read in this session.

- [x] **Package-First (I):** No direct source-to-target migration. Comments API results are written to `IArtefactStore`. Image bytes are written to `IArtefactStore`. No direct filesystem access inside any module class.
- [x] **Streaming (II):** `IAsyncEnumerable<WorkItemComment>` is consumed one record at a time. Image downloads happen per-record. No in-memory lists of all comments accumulated, no sort of enumeration results in memory.
- [x] **WorkItems Layout (III):** Comment folders follow `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-c<commentId>/` — the same `yyyy-MM-dd` date folder convention and `<ticks>-<workItemId>` prefix rule. The `c` infix before `commentId` distinguishes them from revision folders. Embedded images are placed **beside** their parent document (same folder), mirroring the attachment-beside-revision rule.
- [x] **Checkpointing (IV):** A dedicated `Checkpoints/workitems-comments.cursor.json` stores `lastProcessedWorkItemId`. No in-memory progress counters. Stage values: `FetchedComments`, `Completed`.
- [x] **Module Isolation (V):** All new interfaces (`IWorkItemCommentSource`, `IWorkItemCommentExportService`, `IEmbeddedImageExportService`, `IEmbeddedImageDownloader`) are defined in `DevOpsMigrationPlatform.Abstractions`. Module code depends on those abstractions only. No concrete store or HTTP client references inside module classes.
- [x] **Separation of Planes (VI):** Comment export and image download logic lives in `DevOpsMigrationPlatform.Infrastructure` and `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`. No migration logic enters the control plane, TUI, or CLI.
- [x] **Determinism (VII):** SHA-256 filename derivation is deterministic. Folder naming uses ticks from the ADO API date — same input, same output. No randomness.
- [x] **ATDD-First (VIII):** All three user stories have acceptance scenarios (10 total). Each scenario will drive one ATDD loop iteration via `/speckit.specify` → test generation → implementation → review. Gherkin feature files are listed in the project structure below.
- [x] **SOLID & DI (IX):** All services receive their dependencies via constructor injection. `WorkItemsScopeParameters` is a sealed options class with `SectionName`. New registrations are in `Add*Services` extension methods. All interfaces in `DevOpsMigrationPlatform.Abstractions`.

## Project Structure

### Documentation (this feature)

```text
specs/010-workitem-comments-images/
├── spec.md
├── plan.md              ← this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── IWorkItemCommentSource.cs
│   ├── IWorkItemCommentExportService.cs
│   ├── IEmbeddedImageDownloader.cs
│   ├── IEmbeddedImageExportService.cs
│   └── WorkItemComment.cs
├── checklists/
│   └── requirements.md
├── discrepancies.md
└── tasks.md             ← Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   ├── WorkItemComment.cs             ← NEW record
│   │   └── WorkItemIdentityRef.cs         ← NEW record (or nested in WorkItemComment.cs)
│   └── Services/
│       ├── IWorkItemCommentSource.cs      ← NEW interface
│       ├── IWorkItemCommentExportService.cs ← NEW interface
│       ├── IEmbeddedImageDownloader.cs    ← NEW interface
│       └── IEmbeddedImageExportService.cs ← NEW interface
│
├── DevOpsMigrationPlatform.Infrastructure/
│   ├── Export/
│   │   ├── WorkItemExportOrchestrator.cs  ← MODIFY (call IEmbeddedImageExportService + IWorkItemCommentExportService)
│   │   ├── WorkItemCommentExportService.cs ← NEW
│   │   └── EmbeddedImageExportService.cs  ← NEW
│   └── Modules/
│       ├── WorkItemsModule.cs             ← MODIFY (wire IWorkItemCommentExportService + IEmbeddedImageExportService)
│       └── WorkItemsScopeParameters.cs    ← MODIFY (add IncludeComments, IncludeEmbeddedImages, IncludeDeletedComments)
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── Services/
│   │   ├── AzureDevOpsWorkItemCommentSource.cs  ← NEW (paginated Comments API + Versions API)
│   │   └── AzureDevOpsEmbeddedImageDownloader.cs ← NEW (PAT auth, Polly retry)
│   └── ExportServiceCollectionExtensions.cs     ← MODIFY (register new services)
│
└── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
    └── (no new files — TFS < 2018 = no-op handled in Infrastructure layer)

tests/
├── DevOpsMigrationPlatform.Infrastructure.Tests/
│   ├── Export/
│   │   ├── WorkItemCommentExportServiceTests.cs
│   │   └── EmbeddedImageExportServiceTests.cs
│   └── Modules/
│       └── WorkItemsScopeParametersTests.cs
│
└── DevOpsMigrationPlatform.Integration.Tests/
    └── (existing integration test project — new step definitions added)

features/
├── export/
│   └── work-items/
│       ├── comments/
│       │   └── export-comments.feature       ← NEW (P1 scenarios)
│       └── embedded-images/
│           └── export-embedded-images.feature ← NEW (P2+P3 scenarios)
```

**Structure Decision**: Single-project additions to the existing 3-tier structure (`Abstractions → Infrastructure → Infrastructure.AzureDevOps`). No new top-level projects required. All changes are in-place within existing assemblies.

## Complexity Tracking

> No constitution violations. No complexity justifications required.

---

## ATDD Implementation Phases

Each scenario below drives one complete ATDD inner loop: one Gherkin scenario → `speckit.specify` → step definitions → implementation → build + tests pass → one commit. Phases build on each other; P2 requires P1 complete.

---

### Phase P1 — Comments Export

**Feature file**: `features/export/work-items/comments/export-comments.feature`

#### P1-1: Three comments → three comment folders

```gherkin
Feature: Work Item Comments Export

  Scenario: Export creates one comment folder per comment
    Given a work item with ID 12345 has 3 comments in the source project
    When an export runs with includeComments enabled
    Then the package contains 3 comment sub-folders for work item 12345
    And each sub-folder is named "<ticks>-12345-c<commentId>"
    And each sub-folder contains a comment.json with the correct commentId, text, format, createdBy, and createdDate
```

**Implementation units**:
- `IWorkItemCommentSource` interface in Abstractions
- `WorkItemComment` record in Abstractions
- `IWorkItemCommentExportService` interface in Abstractions
- `WorkItemCommentExportService` in Infrastructure
- Stub `AzureDevOpsWorkItemCommentSource` (returns hard-coded test comments from an in-memory fake)
- `WorkItemExportOrchestrator` calls `IWorkItemCommentExportService.ExportAsync` after all revisions for a work item
- `WorkItemsModule` wires the new service

#### P1-2: Work item with no comments → no comment folders

```gherkin
  Scenario: No comment folders created when work item has no comments
    Given a work item with ID 99 has no comments in the source project
    When an export runs with includeComments enabled
    Then the package contains no comment sub-folders for work item 99
```

**Implementation units**:
- No new types — ensure `IWorkItemCommentSource` returning empty sequence produces no artefact writes

#### P1-3: Comment pagination — more than one page

```gherkin
  Scenario: All comment pages are fetched for a work item
    Given a work item with ID 500 has 150 comments (spanning multiple pages)
    When an export runs with includeComments enabled
    Then the package contains 150 comment sub-folders for work item 500
```

**Implementation units**:
- Pagination logic in `AzureDevOpsWorkItemCommentSource` (continuation-token loop)
- Unit test with a fake that returns 3 pages × 50 comments

#### P1-4: Resume after interruption

```gherkin
  Scenario: Resumed export continues from the last unprocessed work item
    Given work items 100, 200, 300 exist with comments in the source project
    And the previous export completed comments for work items 100 and 200
    And the comments cursor records lastProcessedWorkItemId = 200
    When the export is resumed
    Then comments for work item 300 are exported
    And comments for work items 100 and 200 are not re-fetched
```

**Implementation units**:
- `Checkpoints/workitems-comments.cursor.json` read/write logic
- Skip condition in `WorkItemCommentExportService` based on cursor
- `WorkItemsScopeParameters` additions: `IncludeComments`, `IncludeDeletedComments`

---

### Phase P2 — HTML Embedded Image Download

**Feature file**: `features/export/work-items/embedded-images/export-embedded-images.feature`

#### P2-1: HTML field image downloaded beside revision.json

```gherkin
Feature: Embedded Image Export

  Scenario: HTML field image is downloaded and URL rewritten
    Given a work item revision has a System.Description HTML field containing an <img src="https://dev.azure.com/..."> tag
    When an export runs with includeEmbeddedImages enabled
    Then the image file is present beside revision.json in the revision sub-folder
    And the img src attribute in the stored revision.json field value references the local filename
    And the local filename is the SHA-256 hash of the image content with the correct extension
```

**Implementation units**:
- `IEmbeddedImageDownloader` interface in Abstractions
- `IEmbeddedImageExportService` interface in Abstractions
- `EmbeddedImageExportService.ProcessHtmlAsync` (HtmlAgilityPack scan + download + SHA-256 naming + rewrite)
- `AzureDevOpsEmbeddedImageDownloader` with PAT auth + Polly retry (3× exp back-off, 30s timeout)
- `WorkItemExportOrchestrator` calls `IEmbeddedImageExportService.ProcessHtmlAsync` for each HTML field after writing `revision.json`
- `WorkItemsScopeParameters.IncludeEmbeddedImages` flag
- `ExportServiceCollectionExtensions` registers new services

#### P2-2: Same image in two fields → one copy

```gherkin
  Scenario: Same image URL in two fields of the same revision is downloaded once
    Given a work item revision has the same ADO-hosted image URL in System.Description and Microsoft.VSTS.TCM.ReproSteps
    When an export runs with includeEmbeddedImages enabled
    Then exactly one image file exists in the revision sub-folder
    And both field values in revision.json reference that same local filename
```

**Implementation units**:
- Per-call URL deduplication dictionary in `EmbeddedImageExportService.ProcessHtmlAsync`

#### P2-3: External image URL preserved unchanged

```gherkin
  Scenario: External image URLs are not downloaded
    Given a work item HTML field contains an <img src="https://example.com/external.png"> tag
    When an export runs
    Then no image file named after that URL is written to the revision sub-folder
    And the src attribute remains "https://example.com/external.png" in revision.json
    And a warning entry is recorded in the export log
```

**Implementation units**:
- Organisation URL prefix check in `AzureDevOpsEmbeddedImageDownloader.TryDownloadAsync` (returns null for non-ADO URLs)
- Warning log via `ILogger`

#### P2-4: Inaccessible image URL — export continues

```gherkin
  Scenario: Inaccessible image URL does not abort export
    Given a work item HTML field contains an <img src="https://dev.azure.com/deleted-image"> tag
    And the URL returns HTTP 404 when requested
    When an export runs
    Then the field value in revision.json preserves the original URL
    And a warning entry is recorded
    And the export completes successfully
```

**Implementation units**:
- 404/4xx branch in `AzureDevOpsEmbeddedImageDownloader.TryDownloadAsync` returns null + warning
- `EmbeddedImageExportService` preserves original URL when downloader returns null

---

### Phase P3 — Markdown Image Download

#### P3-1: Markdown comment image downloaded beside comment.json

```gherkin
  Scenario: Markdown comment image downloaded beside comment.json
    Given a work item has a comment in Markdown format containing "![diagram](https://dev.azure.com/...)"
    When an export runs with includeComments and includeEmbeddedImages enabled
    Then the image file is present in the comment sub-folder beside comment.json
    And the stored comment.json text field replaces the ADO URL with the local filename
```

**Implementation units**:
- `EmbeddedImageExportService.ProcessMarkdownAsync` (regex scan + same download/write/rewrite pipeline)
- `WorkItemCommentExportService` calls `IEmbeddedImageExportService.ProcessMarkdownAsync` for `format: markdown` comments
- `WorkItemCommentExportService` calls `IEmbeddedImageExportService.ProcessHtmlAsync` for `format: html` comments

#### P3-2: Markdown revision field image downloaded

```gherkin
  Scenario: Markdown revision field image downloaded beside revision.json
    Given a work item revision has a Markdown-format field containing "![](https://dev.azure.com/...)"
    When an export runs
    Then the image file is present beside revision.json
    And the Markdown image reference in the stored field value uses the local filename
```

**Implementation units**:
- `WorkItemExportOrchestrator` calls `ProcessMarkdownAsync` for Markdown-format fields (determined by `multilineFieldsFormat` dictionary from ADO API)

---

## OTel Instrumentation Plan

Activity source: `DevOpsMigrationPlatform.WorkItems.Comments`  
Activity source: `DevOpsMigrationPlatform.WorkItems.EmbeddedImages`

| Metric | Unit | Description |
|---|---|---|
| `workitems.comments.fetched` | count | Comments fetched from API per work item |
| `workitems.comments.folders_written` | count | comment.json files written |
| `workitems.comments.pages_fetched` | count | API pages fetched (pagination tracking) |
| `workitems.comments.skipped_tfs_not_supported` | count | Comment fetches skipped for TFS < 2018 |
| `workitems.images.downloaded` | count | Images successfully downloaded |
| `workitems.images.skipped_external` | count | External URLs left unchanged |
| `workitems.images.failed` | count | Inaccessible URLs (with warning log) |
| `workitems.images.bytes_downloaded` | bytes | Total bytes written for embedded images |

Each activity spans one work item's comment export; child activities span each comment version write and each image download.

---

## Architectural Discrepancies to Resolve During Implementation

See `discrepancies.md`. Two docs must be updated as part of implementation:

1. **`.agents/context/workitems-format.md`** — add `<ticks>-<workItemId>-c<commentId>/comment.json` sub-folder description
2. **`.agents/context/package-format.md`** — add embedded image files beside documents; add `workitems-comments.cursor.json` cursor description

These must be updated before the final task in `tasks.md` is marked complete.

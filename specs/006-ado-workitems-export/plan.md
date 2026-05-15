# Implementation Plan: Work Items Export — Azure DevOps via REST API

**Branch**: `006-ado-workitems-export` | **Date**: 2026-04-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-ado-workitems-export/spec.md`

## Summary

Export all work item revisions from an Azure DevOps project via the REST API, writing each revision as `revision.json` in the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` package layout. Attachment binaries are streamed beside their revision, SHA-256 verified, and delta-deduplicated. The export is resumable via cursor-based checkpointing. Progress flows via `IProgressSink` to the TUI and control plane.

The implementation extends the existing `WorkItemExportOrchestrator` (streaming loop + cursor) by wiring in an abstract `IAttachmentDownloader`, an ADO-specific `AzureDevOpsWorkItemRevisionSource`, and a binary-write extension to `IArtefactStore`. A new `WorkItemsModule` wraps the orchestrator behind `IDataTypeModule`, making the ADO export path a first-class export module.

## Technical Context

**Language/Version**: C# 12 / .NET 10.0 (multi-targeted `net481;net10.0` for `Abstractions` and `Infrastructure` only; `Infrastructure.AzureDevOps` targets `net10.0` only)  
**Primary Dependencies**: `Microsoft.TeamFoundationServer.Client` 20.256.2 (ADO SDK); `Microsoft.Extensions.DependencyInjection`; `Microsoft.Extensions.Http.Resilience` (retry pipeline); `OpenTelemetry.Api`  
**Storage**: On-disk package via `IArtefactStore` (FileSystemArtefactStore for local; AzureBlobArtefactStore for cloud)  
**Testing**: MSTest 3 + Reqnroll 2 (BDD); Moq 4 (mocking); existing Infrastructure.Tests project  
**Target Platform**: Linux/Windows server (.NET 10); also net481 for shared Abstractions/Infrastructure types  
**Project Type**: Library modules within a CLI/agent migration platform  
**Performance Goals**: ≤ 512 MB working set for a 10,000 work item project; streaming I/O for attachments; no in-memory accumulation of revisions  
**Constraints**: `IArtefactStore` is the only file abstraction; no direct filesystem calls; no direct ADO SDK calls from module code; `CancellationToken` propagated everywhere; no `.Result`/`.Wait()`  
**Scale/Scope**: Single ADO project export; up to ~20,000 work items per date window (handled by `WorkItemQueryWindowStrategy`); unlimited revisions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirm that ALL files in
> `/.agents/20-guardrails/`, ALL files in `/.agents/30-context/`, and relevant `/docs/` files
> have been read. Skipping either `.agents/` subdirectory is a constitution violation.

- [x] **Package-First (I):** `WorkItemsModule.ExportAsync` writes only via `IArtefactStore`. No ADO API calls from within module code — all ADO interaction is behind `IWorkItemRevisionSource` and `IAttachmentDownloader` abstractions.
- [x] **Streaming (II):** `AzureDevOpsWorkItemRevisionSource.GetRevisionsAsync` returns `IAsyncEnumerable<WorkItemRevision>`; consumed lazily one-at-a-time in `WorkItemExportOrchestrator`. No list-of-all-revisions is ever materialised.
- [x] **WorkItems Layout (III):** Folder path `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` is produced by `WorkItemExportOrchestrator.BuildFolderPath` (existing). No changes to this format.
- [x] **Checkpointing (IV):** `WorkItemExportOrchestrator` writes `Checkpoints/workitems.cursor.json` via `ICheckpointingService` after each revision completes. No watermark tables or in-memory counters.
- [x] **Module Isolation (V):** `WorkItemsModule` depends only on `IWorkItemRevisionSource`, `IAttachmentDownloader`, `ICheckpointingService`, `IProgressSink` — all interfaces in Abstractions. No `FileSystemArtefactStore` or concrete ADO type reference in module code.
- [x] **Separation of Planes (VI):** CLI submits a `MigrationJob` to the control plane. The Job Engine runs `WorkItemsModule.ExportAsync`. No migration logic in `ControlPlane` or TUI. No `Console.Write` anywhere in the export path.
- [x] **Determinism (VII):** Same ADO project + same date range → identical folder set in identical order. `DownloadUrl` is `[JsonIgnore]` — not persisted. Attachment filename is stable: `<attachmentId>-<originalFilename>`. No upgrader required: `revision.json` schema gains no breaking change; `ProgressEvent` gains new additive fields.
- [x] **ATDD-First (VIII):** All 4 user stories in `spec.md` have Given/When/Then acceptance scenarios in `features/export/work-items/`. User stories 1 and 2 have existing feature files (`export-work-item-revisions.feature`, `export-attachments.feature`) that need ADO-tagged scenarios appended. User stories 3 (resume) and 4 (progress) are covered by existing scenarios in the revisions feature file. Each scenario session follows the ATDD inner loop.
- [x] **SOLID & DI (IX):** All new classes receive dependencies via constructor. Module config comes from `ExportContext.Job` (not `IOptions<T>`) because it is per-job, not per-deployment config. Interfaces declared in `DevOpsMigrationPlatform.Abstractions`. Service registration in `AddAzureDevOpsWorkItemExportServices` extension method.

---

**Pre-design Gate: PASS.** All nine principles are satisfied by the proposed design. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
# Modified files (existing)
src/DevOpsMigrationPlatform.Abstractions/
├── Storage/
│   └── IArtefactStore.cs                        ← ADD WriteStreamAsync(string, Stream, CancellationToken)
├── Models/
│   ├── AttachmentMetadata.cs                    ← ADD [JsonIgnore] DownloadUrl property
│   ├── AttachmentDownloadResult.cs              ← REDESIGN: remove FilePath, add Sha256 + Size
│   └── ProgressEvent.cs                         ← ADD AttachmentsProcessed + AttachmentsFailed
└── Services/
    └── IAttachmentDownloader.cs                 ← NEW interface

src/DevOpsMigrationPlatform.Infrastructure/
├── Export/
│   └── WorkItemExportOrchestrator.cs            ← EXTEND: attachments + progress sink + delta detection
├── Modules/
│   └── WorkItemsModule.cs                       ← NEW IDataTypeModule implementation
└── Storage/
    └── FileSystemArtefactStore.cs               ← ADD WriteStreamAsync implementation

src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
├── Services/
│   ├── AzureDevOpsWorkItemRevisionSource.cs     ← NEW IWorkItemRevisionSource implementation
│   └── AzureDevOpsAttachmentDownloader.cs       ← NEW IAttachmentDownloader implementation
├── IAzureDevOpsAttachmentDownloader.cs          ← NEW (interface extending IAttachmentDownloader)
└── WorkItemExportServiceCollectionExtensions.cs ← NEW Add*Services extension

# New feature files
features/export/work-items/revisions/
└── export-work-item-revisions.feature           ← ADD @azure-devops-rest tagged scenarios for US1+US3

features/export/work-items/attachments/
└── export-attachments.feature                   ← ADD @azure-devops-rest tagged scenarios for US2

# Test files
tests/DevOpsMigrationPlatform.Infrastructure.Tests/
├── Export/
│   ├── ExportWorkItemRevisionsSteps.cs          ← EXTEND: ADO source scenarios + attachment scenarios
│   ├── ExportWorkItemRevisionsContext.cs        ← EXTEND: attachment and progress mock wiring
│   └── WorkItemExportOrchestratorTests.cs       ← EXTEND: attachment + progress + delta tests
└── DevOpsMigrationPlatform.Infrastructure.Tests.csproj ← ADD new feature file references
```

**Structure Decision**: Existing 3-project layout (`Abstractions` → `Infrastructure` → `Infrastructure.AzureDevOps`). `WorkItemsModule` lives in `Infrastructure` (source-agnostic; reusable for TFS). ADO-specific source and downloader live in `Infrastructure.AzureDevOps`. No new projects needed.

## Complexity Tracking

No constitution violations in this design — no justifications required.

---

## Phase 0: Research

All NEEDS CLARIFICATION items from the Technical Context were resolved during specification (see [spec.md](spec.md) §Clarifications). Findings are consolidated in [research.md](research.md).

**Summary of key decisions:**

| Decision | Choice | Rationale |
|---|---|---|
| ADO revision fetch strategy | Per-work-item `GetRevisionsAsync(id, expand: All)` — O(N) serial calls | No bulk-revisions endpoint exists; work item IDs paged by reused `WorkItemQueryWindowStrategy` |
| Attachment download transport | `WorkItemTrackingHttpClient.GetAttachmentContentAsync(Guid id)` returns `Stream`; piped to `IArtefactStore.WriteStreamAsync` | SDK handles auth; no raw HttpClient needed; avoids buffering |
| SHA-256 computation | `CryptoStream` wrapping the download response stream, pass-through mode | Hash computed in-flight; zero additional memory allocation |
| `IArtefactStore` binary write | Add `WriteStreamAsync(string path, Stream content, CancellationToken ct)` | Required to stream attachment binaries without buffering into `byte[]` or `MemoryStream` |
| `AttachmentMetadata.DownloadUrl` transport | `[JsonIgnore] public string? DownloadUrl` on `AttachmentMetadata` | Passes URL from source to orchestrator without persisting the ADO-scoped URL in revision.json |
| Delta attachment detection | Compare current revision's `DownloadUrl` set against previous revision's set (adjacent-pair comparison in-memory) | O(1) memory overhead; no cumulative accumulation; handles first-revision case (all new) |
| Retry policy | Polly `ResiliencePipeline` via `Microsoft.Extensions.Resilience`; max 8 retries, exponential back-off (2^n seconds + jitter), retry on 5xx/408/429, no retry on other 4xx | Satisfies FR-020; `Microsoft.Extensions.Resilience` is already ecosystem-aligned for .NET 10 |
| `WorkItemsModule` location | `DevOpsMigrationPlatform.Infrastructure` | Source-agnostic (uses `IWorkItemRevisionSource`); reusable for TFS path without code change |
| `IAttachmentDownloader` location | `DevOpsMigrationPlatform.Abstractions/Services/` | Shared abstraction used by `WorkItemExportOrchestrator` in `Infrastructure`; ADO impl in `Infrastructure.AzureDevOps` |

---

## Phase 1: Design & Contracts

See [data-model.md](data-model.md) for entity diagrams and field-level changes.
See [contracts/work-items-scope-parameters.schema.json](contracts/work-items-scope-parameters.schema.json) for the module scope parameters schema.
See [quickstart.md](quickstart.md) for end-to-end usage instructions.

### Design walkthrough

#### Execution flow

```
CLI (export command)
  └─► ControlPlanClient.SubmitJobAsync(MigrationJob)
        └─► MigrationAgent receives job lease
              └─► IJobRunner.RunAsync(ExportContext)
                    └─► WorkItemsModule.ExportAsync(context, ct)
                          ├─► reads WIQL query + includeAttachments from context.Job.Modules["WorkItems"].Scopes[0].Parameters
                          └─► WorkItemExportOrchestrator.ExportAsync(source, attachmentDownloader?, progressSink, ct)
                                └─► foreach revision in AzureDevOpsWorkItemRevisionSource.GetRevisionsAsync(ct)
                                      ├─► builds folderPath (BuildFolderPath)
                                      ├─► checks cursor → skip if already Completed
                                      ├─► if includeAttachments → for each NEW attachment (delta vs prev):
                                      │     AzureDevOpsAttachmentDownloader.DownloadAsync(url, destPath, artefactStore, ct)
                                      │       ├─► WorkItemTrackingHttpClient.GetAttachmentContentAsync(attachmentGuid)
                                      │       ├─► CryptoStream SHA-256 + WriteStreamAsync to IArtefactStore
                                      │       └─► returns AttachmentDownloadResult(Sha256, Size)
                                      ├─► updates revision.Attachments with final Sha256 + Size + RelativePath
                                      ├─► JsonSerializer.Serialize(revision) → IArtefactStore.WriteAsync(folderPath + "revision.json")
                                      ├─► ICheckpointingService.WriteCursorAsync("WorkItems", Completed)
                                      └─► IProgressSink.Emit(ProgressEvent { WorkItemId, RevisionsProcessed, ... })
```

#### IArtefactStore extension

```csharp
// In Abstractions/Storage/IArtefactStore.cs — new method
Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken);
```

- `FileSystemArtefactStore`: create parent dir, `CopyToAsync` (net10) / `CopyTo` (net481)
- `AzureBlobArtefactStore`: `BlobClient.UploadAsync(stream)` with overwrite

#### AttachmentMetadata transport field

```csharp
// Added to AttachmentMetadata record (Abstractions/Models/)
[System.Text.Json.Serialization.JsonIgnore]
public string? DownloadUrl { get; init; }
```

- Populated by `AzureDevOpsWorkItemRevisionSource` from the `url` attribute on `AttachedFile` relations
- Stripped from `revision.json` by `JsonSerializer`; never stored in the package

#### AttachmentDownloadResult redesign

```csharp
// Updated factory signatures
public static AttachmentDownloadResult Succeeded(string sha256, long size) => ...
public static AttachmentDownloadResult Failed(Exception error) => ...
// Additive change only — FilePath property and Succeeded(string filePath) factory preserved for TFS (.NET 4.8) compatibility
// Added: Sha256, Size, and new Succeeded(string sha256, long size) factory overload
```

#### ProgressEvent additions

```csharp
// New fields on ProgressEvent record (additive, no breaking change)
public int AttachmentsProcessed { get; init; }
public int AttachmentsFailed { get; init; }
```

#### WorkItemExportOrchestrator changes

Constructor gains two optional parameters: `IAttachmentDownloader? attachmentDownloader` and `IProgressSink progressSink`. The existing constructor (no attachment, no progress) becomes an overload for backward compatibility with existing tests.

New parameter tracking within the export loop:
- `previousRevisionAttachmentUrls` — `HashSet<string>` updated per-revision (delta detection); re-initialised on each new `workItemId`
- `revisionsProcessed`, `attachmentsProcessed`, `attachmentsFailed` — counters emitted in `ProgressEvent`

#### AzureDevOpsWorkItemRevisionSource

```csharp
public sealed class AzureDevOpsWorkItemRevisionSource : IWorkItemRevisionSource
{
    // Constructor injects: IWorkItemQueryWindowStrategy, IAzureDevOpsClientFactory
    // GetRevisionsAsync:
    //   1. Reads orgUrl, project, pat from ExportContext (passed via factory method / ctor)
    //   2. foreach workItemId in windowStrategy.EnumerateWindowsAsync(...)
    //      3. witClient.GetRevisionsAsync(id, expand: WorkItemExpand.All)
    //      4. foreach revision in apiRevisions (ascending revisionIndex)
    //         5. Map to WorkItemRevision (fields, links, attachments incl. DownloadUrl)
    //         6. yield return revision
}
```

`AzureDevOpsWorkItemRevisionSource` is **transient** (per export) because it carries the export PAT and project from `ExportContext.Job.Source`. It is constructed by the `WorkItemsModule` (which receives `IAzureDevOpsClientFactory` and `IWorkItemQueryWindowStrategy` via DI) and passes the PAT from context — not from `IOptions<T>`.

#### AzureDevOpsAttachmentDownloader

```csharp
public sealed class AzureDevOpsAttachmentDownloader : IAzureDevOpsAttachmentDownloader
{
    // Constructor injects: IAzureDevOpsClientFactory, ResiliencePipeline (or IResiliencePipelineProvider)
    // DownloadAsync(url, destPath, store, ct):
    //   1. Parse attachment GUID from url
    //   2. Execute with resilience pipeline (retry on transient errors)
    //   3. witClient.GetAttachmentContentAsync(guid, ct)
    //   4. CryptoStream(SHA256.Create(), stream, CryptoStreamMode.Read)
    //   5. store.WriteStreamAsync(destPath, cryptoStream, ct)
    //   6. Flush; read SHA256 hash bytes → hex string
    //   7. return AttachmentDownloadResult.Succeeded(sha256Hex, fileSize)
}
```

#### Service registration

```csharp
// WorkItemExportServiceCollectionExtensions.cs (Infrastructure.AzureDevOps)
public static IServiceCollection AddAzureDevOpsWorkItemExportServices(
    this IServiceCollection services)
{
    services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();     // idempotent
    services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>(); // idempotent
    services.AddSingleton<IAzureDevOpsAttachmentDownloader, AzureDevOpsAttachmentDownloader>();
    services.AddResiliencePipeline("attachment-download", builder =>
        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 8,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(2),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(e =>
                IsTransient(e.StatusCode))
        }));
    services.AddSingleton<IWorkItemRevisionSourceFactory, AzureDevOpsWorkItemRevisionSourceFactory>();
    services.AddTransient<WorkItemExportOrchestrator>();
    services.AddTransient<WorkItemsModule>();
    return services;
}
```

**Note**: `IWorkItemRevisionSourceFactory` is singleton — it holds no per-export state. `WorkItemsModule` calls `_sourceFactory.Create(url, project, pat, query)` at export time, injecting credentials from `ExportContext` at the point of use. This avoids a circular project reference (`Infrastructure` must not reference `Infrastructure.AzureDevOps`).

---

## Post-design Constitution Re-check

All nine principles re-verified against the Phase 1 design above:

- **I (Package-First)**: ✅ `IArtefactStore.WriteStreamAsync` extends the same abstraction. No direct filesystem ops in modules.
- **II (Streaming)**: ✅ `GetRevisionsAsync` is `IAsyncEnumerable`; attachment download streams response directly to store; no accumulation.
- **III (WorkItems Layout)**: ✅ `BuildFolderPath` unchanged. Attachment filenames use `<attachmentId>-<originalFilename>` within the revision folder.
- **IV (Checkpointing)**: ✅ `WriteCursorAsync` called once per revision after all stages complete. `lastProcessed` = revision folder path.
- **V (Module Isolation)**: ✅ `WorkItemsModule` depends only on abstract interfaces. `IAttachmentDownloader` is in Abstractions.
- **VI (Separation of Planes)**: ✅ CLI not modified. No migration logic outside Job Engine boundary.
- **VII (Determinism)**: ✅ `DownloadUrl` never serialised. Attachment filenames are deterministic. `AttachmentDownloadResult` changes are internal (no schema impact on package).
- **VIII (ATDD-First)**: ✅ Existing feature files carry the acceptance scenarios. ADO-specific scenarios to be added during ATDD sessions.
- **IX (SOLID & DI)**: ✅ Constructor injection throughout. `IAzureDevOpsAttachmentDownloader` in Infrastructure.AzureDevOps extends `IAttachmentDownloader` from Abstractions. Dedicated `AddAzureDevOpsWorkItemExportServices` extension.

**Post-design Gate: PASS.**

---

*Next step: run `/speckit.tasks` to generate the dependency-ordered task list for implementation.*



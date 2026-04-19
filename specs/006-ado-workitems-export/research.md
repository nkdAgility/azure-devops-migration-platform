# Research: Work Items Export — Azure DevOps via REST API

**Feature**: `006-ado-workitems-export`  
**Status**: Complete — all unknowns resolved. No NEEDS CLARIFICATION remain.  
**Sources**: ADO REST API docs, spec clarifications (session 2026-04-07), existing codebase patterns.

---

## 1. ADO REST API: Revision Retrieval Strategy

### Decision
Use `WorkItemTrackingHttpClient.GetRevisionsAsync(int id, expand: WorkItemExpand.All)` — one call per work item, returning all revisions in ascending index order.

### Rationale
The ADO REST API has no bulk-revisions endpoint (`$batch` for revisions does not exist). The closest alternative — `GetWorkItemsBatchAsync` — fetches the **current** state only, not historical revisions. Therefore O(N) calls (one per work item) is the only correct approach.

### Alternatives Considered
- `GET _apis/wit/workItems?ids=...&$expand=all` — fetches current state only, no revision history.
- `GET _apis/wit/reporting/workItemRevisions` — returns all revisions across the project but lacks the structured relations and attachments metadata needed.

### Mitigation of O(N) Cost
Work item IDs are paged via the existing `WorkItemQueryWindowStrategy` (date-window WIQL), which constrains each WIQL result to ≤ 20,000 items. The network cost is proportional to total work items (unavoidable), but is not multiplied by revision count.

---

## 2. Attachment Download: Streaming + SHA-256

### Decision
Use `WorkItemTrackingHttpClient.GetAttachmentContentAsync(Guid resourceId, CancellationToken ct)` to obtain a `Stream`. Wrap it in a `CryptoStream` (SHA256, read-through mode) and pipe directly to `IArtefactStore.WriteStreamAsync`. Return the computed SHA-256 hex and file size.

### Rationale
- SDK handles authentication automatically (VssConnection + PAT).
- `CryptoStream` in `CryptoStreamMode.Read` computes the hash in-flight while the data flows to the store.
- Zero additional memory allocation; satisfies FR-018 (no buffering into `MemoryStream` or `byte[]`).

### Attachment GUID Extraction
ADO attachment URLs have the form `https://dev.azure.com/{org}/_apis/wit/attachments/{guid}`. The GUID is extracted from the URL path using `new Uri(url).Segments.Last()`.

### Alternatives Considered
- Raw `HttpClient` with `Authorization: Basic` header — works but bypasses the SDK's connection caching and retry hooks.
- Download to temp file, then copy to `IArtefactStore` — buffers the full file to disk; violates streaming requirement.
- `MemoryStream` buffer — violates FR-018 directly.

---

## 3. IArtefactStore Binary Write Extension

### Decision
Add `Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)` to `IArtefactStore`.

```csharp
// IArtefactStore.cs (Abstractions)
Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken);
```

Implementations:
- `FileSystemArtefactStore`: create parent directory, then `await content.CopyToAsync(fileStream, ct)` (net10) / blocking `content.CopyTo(fileStream)` (net481).
- `AzureBlobArtefactStore`: `await blobClient.UploadAsync(content, overwrite: true, ct)`.

### Rationale
`WriteAsync(string path, string content, ct)` accepts a `string`, which requires materialising all binary data in memory (Base64 encode or verbatim). A `Stream` overload cleanly separates binary from text semantics and enables true streaming.

### Implementation Impact
- `IArtefactStore` interface gains one new method → all implementations must be updated.
- Existing `InMemoryArtefactStore` (used in tests) must also implement it.
- This is an additive interface change. No callers of the existing `WriteAsync(string, string, ct)` are broken.

---

## 4. AttachmentMetadata: DownloadUrl Transport

### Decision
Add `[JsonIgnore] public string? DownloadUrl { get; init; }` to `AttachmentMetadata`.

```csharp
// AttachmentMetadata.cs (Abstractions/Models)
[System.Text.Json.Serialization.JsonIgnore]
public string? DownloadUrl { get; init; }
```

### Rationale
`IWorkItemRevisionSource.GetRevisionsAsync` returns `WorkItemRevision` objects — the contract is fixed. To pass the ADO download URL from the source to the orchestrator without persisting it in `revision.json`, the cleanest mechanism is a `[JsonIgnore]` field. The URL is ephemeral (ADO-scoped, potentially expiring) and must not appear in the package.

### Alternatives Considered
- Separate `IAsyncEnumerable<(WorkItemRevision, IReadOnlyList<AttachmentDownloadRef>)>` return type — breaks `IWorkItemRevisionSource` contract; requires a new interface.
- Pass a parallel dictionary from source to orchestrator — two separate enumerables that must stay in sync; fragile.
- Store URL in `AttachmentMetadata.RelativePath` and strip during serialization — misuses an existing field; semantically wrong.

---

## 5. Delta Attachment Detection

### Decision
Track the attachment `DownloadUrl` set from the previous revision; for each new revision compute the set difference. Only download attachments whose URL is not in the previous set.

```text
prevAttachmentUrls := Set<string>(empty)
foreach revision in source:
    if revision.workItemId != currentWorkItemId:
        prevAttachmentUrls = empty        // reset on new work item
        currentWorkItemId = revision.workItemId
    newAttachments = [a for a in revision.Attachments if a.DownloadUrl not in prevAttachmentUrls]
    // download newAttachments
    prevAttachmentUrls = Set{a.DownloadUrl for a in revision.Attachments}
```

For carry-forward attachments (not new in this revision): populate `AttachmentMetadata.RelativePath` pointing to the folder where the binary was first written; `Sha256` and `Size` copied from the first-seen result.

### Rationale
Prevents downloading the same binary N times for N revisions. Keeps a `HashSet<string>` of at most the attachment count for one work item — O(1) memory.

### Edge Case: Single-Revision Work Item
No previous revision → `prevAttachmentUrls` is empty → all attachments treated as new. Correct behaviour.

---

## 6. Retry Policy

### Decision
Use `Microsoft.Extensions.Resilience` (the official .NET 9/10 resilience package) with a `ResiliencePipeline` configured as:
- 8 retry attempts
- Exponential back-off starting at 2 seconds with jitter
- Retry on: 5xx, HTTP 408, HTTP 429, `HttpRequestException`, `TaskCanceledException` (transient timeout)
- No retry on: 4xx (except 408/429), including 401 (PAT expired → abort export)

### Rationale
`Microsoft.Extensions.Resilience` wraps Polly v8 and integrates with `IResiliencePipelineProvider<TKey>` for named pipelines. This is the idiomatic approach for .NET 10 services without adding a direct Polly dependency.

### Package Addition Required
Add `Microsoft.Extensions.Resilience` to `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.csproj` (net10.0 only).

---

## 7. WorkItemsModule Placement

### Decision
`WorkItemsModule` lives in `DevOpsMigrationPlatform.Infrastructure` (the multi-targeted project).

### Rationale
`WorkItemsModule` depends only on:
- `IWorkItemRevisionSource` (Abstractions)
- `IAttachmentDownloader` (Abstractions)
- `WorkItemExportOrchestrator` (Infrastructure — already there)

It has zero ADO-specific dependencies. Placing it in `Infrastructure` means it is reusable when the TFS exporter path eventually implements `IWorkItemRevisionSource`.

---

## 8. IAttachmentDownloader Placement

### Decision
`IAttachmentDownloader` is defined in `DevOpsMigrationPlatform.Abstractions/Services/`.  
`IAzureDevOpsAttachmentDownloader` (extends `IAttachmentDownloader`) and `AzureDevOpsAttachmentDownloader` live in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`.

### Rationale
`WorkItemExportOrchestrator` (in `Infrastructure`) injects `IAttachmentDownloader` — it must not reference `Infrastructure.AzureDevOps` (that would create a circular dependency: Infrastructure → Infrastructure.AzureDevOps → Infrastructure via Abstractions).  
`IAzureDevOpsAttachmentDownloader` in Infrastructure.AzureDevOps satisfies FR-017 (named ADO interface + impl in that project) while still being injectable as `IAttachmentDownloader` from Abstractions.

---

## 9. ProgressEvent Additions

### Decision
Add two new `int` properties to `ProgressEvent` (record): `AttachmentsProcessed` and `AttachmentsFailed`.

### Rationale
Additive change to a record (new `init`-only properties with default values of `0`) is non-breaking. Existing serialized `ProgressEvent` JSON without these fields will deserialize correctly (defaults to 0). No upgrader needed.

---

*All unknowns resolved. Proceed to Phase 1: data-model.md, contracts/, quickstart.md.*

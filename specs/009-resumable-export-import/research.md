# Research: Resumable Export and Import

**Feature**: 009-resumable-export-import  
**Phase**: 0 — all unknowns resolved from codebase analysis

## Summary

No external research was required. All architectural decisions were resolved by reading existing source code, guardrails, and context files. The codebase already implements export resume; import is the primary gap.

---

## Finding 1 — Export Resume is Already Complete

**Decision**: No changes needed to `WorkItemExportOrchestrator`.

**Evidence**: `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` contains:

```csharp
var cursor = await _checkpointingService.ReadCursorAsync("WorkItems", cancellationToken);

await foreach (var revision in source.GetRevisionsAsync(cancellationToken))
{
    var folderPath = BuildFolderPath(...);
    if (cursor != null &&
        string.Compare(folderPath, cursor.LastProcessed, StringComparison.Ordinal) <= 0)
    {
        continue;  // skip already-exported revisions
    }
    // ... write and advance cursor
}
```

The resume logic is fully implemented. Feature scenarios exist in `features/export/work-items/revisions/export-work-item-revisions.feature` and step definitions in `tests/.../Export/ExportWorkItemRevisionsSteps.cs`.

**Gap**: No `DeleteCursorAsync` on `ICheckpointingService` — needed for forced fresh-start.

---

## Finding 2 — Import is Unimplemented

**Decision**: Build `WorkItemImportOrchestrator` from scratch in `src/DevOpsMigrationPlatform.Infrastructure/Import/`.

**Evidence**: `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`:

```csharp
public Task ImportAsync(ImportContext context, CancellationToken ct) =>
    throw new NotImplementedException("WorkItems import is deferred to a future spec.");
```

`IArtefactStore.EnumerateAsync` exists and returns paths in lexicographic ascending order — the exact requirement for streaming chronological import.

**Stage contract** (from `.agents/context/checkpointing-summary.md` and `.agents/context/import-streaming.md`):

| Stage | Label | Action |
|---|---|---|
| A | `CreatedOrUpdated` | Create work item if absent; else use existing `idmap` target ID |
| B | `AppliedFields` | Deserialise `revision.json`; patch field values on target |
| C | `AppliedLinks` | Apply related, external, and hyperlinks — skip existing |
| D | `UploadedAttachments` | Upload binary files from revision folder; record in `idmap.json` |
| — | `Completed` | Cursor advanced to this folder with stage `Completed` |

**Resume at stage level**: cursor records `lastProcessed` (folder path) + `stage` (last completed stage). On resume, when the folder path equals `lastProcessed`, processing jumps to the stage *after* the recorded stage. For all folders after `lastProcessed`, run all stages.

---

## Finding 3 — `ICheckpointingService` Needs `DeleteCursorAsync`

**Decision**: Add `Task DeleteCursorAsync(string moduleName, CancellationToken)` to `ICheckpointingService` interface and implement in `CheckpointingService`.

**Rationale**: Forced fresh-start requires deleting `Checkpoints/<moduleName>.cursor.json`. Deleting via `IStateStore` directly from the agent worker would leak infrastructure knowledge into the coordination layer. A dedicated method on `ICheckpointingService` is symmetric, testable, and consistent with the existing Read/Write API.

**Implementation**: `CheckpointingService.DeleteCursorAsync` calls `IStateStore.DeleteAsync` (which must be added to `IStateStore` as well — see Finding 7).

---

## Finding 4 — `IWorkItemTargetService` Interface Needed

**Decision**: Define `IWorkItemTargetService` in `DevOpsMigrationPlatform.Abstractions/Services/` with methods called by `WorkItemImportOrchestrator` at each import stage.

**Rationale**: Constitution Principle V requires module code to call target APIs only through injected abstractions. `WorkItemImportOrchestrator` must not reference `Infrastructure.AzureDevOps` types. `AzureDevOpsWorkItemTargetService` in `Infrastructure.AzureDevOps` implements the interface.

**Minimum interface surface** (derived from the four import stages):

```csharp
Task<int> CreateOrGetWorkItemAsync(int sourceWorkItemId, string workItemType, ...);
Task ApplyFieldsAsync(int targetWorkItemId, IDictionary<string, object?> fields, ...);
Task ApplyLinksAsync(int targetWorkItemId, IReadOnlyList<WorkItemLink> links, ...);
Task<string> UploadAttachmentAsync(int targetWorkItemId, int revisionIndex, string relativePath, Stream content, ...);
```

Initial implementation is a stub (`NotImplementedException`) — it provides the shape for the orchestrator to compile against; full ADO REST implementation is a follow-on task within this feature.

---

## Finding 5 — Phase Tracking for Both-Mode

**Decision**: Write a `JobPhaseRecord` to `Checkpoints/job.phase.json` via `IStateStore` after each phase completes in `MigrationAgentWorker`.

**Rationale**: Neither `MigrationJob.Mode` nor any cursor file currently tracks whether the export phase of a Both-mode job completed. Without this, a re-run always re-runs export before resuming import.

**Schema**:

```json
{
  "exportCompleted": true,
  "importCompleted": false,
  "updatedAt": "2026-04-10T12:00:00Z"
}
```

**Read logic in `MigrationAgentWorker`**:

1. If `Mode == Both` and a `PhaseTrackingService.ReadPhaseRecordAsync()` returns `{ exportCompleted: true }`, skip the export module loop.
2. After export modules finish → write `exportCompleted: true`.
3. After import modules finish → write `importCompleted: true`.

A `PhaseTrackingService` class wraps this logic (reads/writes via `IStateStore`). `MigrationAgentWorker` is injected with it.

---

## Finding 6 — `MigrationJob.Resume` Field

**Decision**: Add `MigrationJobResume? Resume` to `MigrationJob`. `ResumeMode` enum: `Auto` (default) and `ForceFresh`.

**Rationale**: The forced fresh-start option must travel from the CLI through the control plane to the agent. The `MigrationJob` is the only permitted inter-component work handoff (guardrail Rule #15). Adding a nullable `Resume` field with `null` = `Auto` is additive and backwards-compatible.

**CLI surface**: `--force-fresh` flag on `export`, `import`, `migrate` commands. CLI sets `Resume = new MigrationJobResume { Mode = ResumeMode.ForceFresh }` before job submission.

**Agent behaviour on `ForceFresh`**:
1. Call `ICheckpointingService.DeleteCursorAsync` for each registered module name.
2. Delete `Checkpoints/job.phase.json` via `IStateStore`.
3. Continue as a fresh run.

---

## Finding 7 — `IStateStore` Needs `DeleteAsync`

**Decision**: Add `Task DeleteAsync(string key, CancellationToken)` to `IStateStore`.

**Rationale**: `CheckpointingService.DeleteCursorAsync` needs to delete the cursor file via `IStateStore`, but `IStateStore` currently has no delete method.

**Scope**: Both `FileSystemArtefactStore`-backed `IStateStore` and any future `AzureBlobArtefactStore`-backed implementation must support delete.

---

## Finding 8 — Attachment Idempotency Uses `idmap.json`

**Decision**: At Stage D (`UploadedAttachments`), check `Checkpoints/idmap.json` for an existing `(workItemId, revisionIndex, relativePath) → targetAttachmentId` entry before uploading. If present, skip upload.

**Rationale**: ADO REST does not expose SHA256 in attachment list responses (from `.agents/context/import-streaming.md`). The only reliable idempotency check is a local record of what was already uploaded.

**Implementation**: A simple JSON dictionary keyed by `"workItemId:revisionIndex:relativePath"` stored at `Checkpoints/idmap.json`. Read at Stage D start; write new entries after each successful upload. Read/write via `IStateStore`.

---

## Finding 9 — No New NuGet Packages Required

All required types (`IAsyncEnumerable<T>`, `System.Text.Json`, `Microsoft.Extensions.DependencyInjection`, `Reqnroll.MSTest`) are already referenced in the affected projects.

---

## Open Items (Tracked in discrepancies.md)

Three documentation gaps were identified during research. They will be rectified in task T17:

1. `.agents/context/checkpointing-summary.md` — Add export cursor behaviour description
2. `.agents/context/job-lifecycle.md` — Add `resume` block to schema
3. `.agents/context/checkpointing-summary.md` — Add Both-mode phase tracking section

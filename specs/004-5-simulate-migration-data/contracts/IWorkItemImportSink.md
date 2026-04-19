# Contract: `IWorkItemImportSink` Interface

**Version**: 1.0  
**Location**: `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportSink.cs`  
**Implemented by**:
- `SimulatedWorkItemImportSink` (`Infrastructure.Simulated`) — for `target.type: Simulated`
- `AzureDevOpsWorkItemImportSink` (`Infrastructure.AzureDevOps`) — for `target.type: AzureDevOpsServices`

---

## Purpose

Decouples `WorkItemsModule.ImportAsync` from any specific target system. By injecting `IWorkItemImportSink`, the module can stream work item revisions from the package to any target without knowing whether the destination is Azure DevOps, a simulated sink, or any future target type.

This follows the same pattern as `IWorkItemRevisionSource` on the export side.

---

## Interface Definition

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Receives work item revisions during streaming import and writes them to a target system.
/// Implementations must process one revision at a time — no batching or buffering.
/// Called by <see cref="WorkItemsModule.ImportAsync"/> for each revision folder enumerated
/// from the package.
/// </summary>
public interface IWorkItemImportSink
{
    /// <summary>
    /// Writes a single work item revision to the target system.
    /// Called once per revision folder during streaming import, in lexicographic order.
    /// Implementations must not buffer revisions — process and release each revision before
    /// the next call arrives.
    /// </summary>
    /// <param name="revision">
    ///   The <see cref="WorkItemRevision"/> deserialized from <c>revision.json</c> in
    ///   <paramref name="revisionFolderPath"/>.
    /// </param>
    /// <param name="packageStore">
    ///   The <see cref="IArtefactStore"/> for reading attachment binaries from the package.
    ///   Implementations must stream attachment bytes via
    ///   <see cref="IArtefactStore.ReadBinaryAsync"/> rather than loading the entire
    ///   package into memory.
    /// </param>
    /// <param name="revisionFolderPath">
    ///   Relative path to the revision folder (e.g.
    ///   <c>WorkItems/2026-02-25/638760123456789012-12345-17</c>).
    ///   Used to construct attachment file paths within the package.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteRevisionAsync(
        WorkItemRevision revision,
        IArtefactStore packageStore,
        string revisionFolderPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called after all revisions have been written successfully.
    /// Implementations should flush any pending state, emit a final summary, or
    /// clean up resources here.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(CancellationToken cancellationToken);
}
```

---

## Contract Invariants

| Invariant | Description |
|-----------|-------------|
| **Sequential, not concurrent** | `WriteRevisionAsync` is called sequentially — never in parallel. The orchestrator awaits each call before advancing to the next revision folder. |
| **Lexicographic order** | Revision folders are enumerated in lexicographic order (guaranteed by `IArtefactStore.EnumerateAsync`). Implementations may rely on this ordering; they must not re-sort. |
| **One revision per call** | Each call corresponds to exactly one revision folder. The sink never receives a batch. |
| **No return value** | The sink does not return a mapped target ID from `WriteRevisionAsync`. ID mapping is managed via `IStateStore` and `Checkpoints/idmap.db` — this is the responsibility of `WorkItemsModule`, not the sink. |
| **Cancellation respected** | Implementations must propagate `cancellationToken` to any async I/O operations. |
| **`CompleteAsync` called exactly once** | The orchestrator calls `CompleteAsync` once after all revisions have been processed, regardless of whether any revisions were written. If the import is cancelled or fails, `CompleteAsync` is not called. |

---

## Usage by `WorkItemsModule.ImportAsync`

```csharp
// Pseudocode — not the final implementation
public async Task ImportAsync(ImportContext context, CancellationToken ct)
{
    var checkpointingService = new CheckpointingService(context.StateStore);
    var cursor = await checkpointingService.LoadAsync(ct);

    await foreach (var folderPath in context.ArtefactStore.EnumerateAsync("WorkItems/", ct))
    {
        if (cursor.IsBefore(folderPath)) continue;  // resume skip

        var revision = await context.ArtefactStore.ReadJsonAsync<WorkItemRevision>(
            $"{folderPath}/revision.json", ct);

        await _importSink.WriteRevisionAsync(revision, context.ArtefactStore, folderPath, ct);
        await checkpointingService.SaveAsync(folderPath, "Completed", ct);
        context.ProgressSink.Emit(new ProgressEvent { /* ... */ });
    }

    await _importSink.CompleteAsync(ct);
}
```

---

## DI Registration (per source type)

```csharp
// In MigrationAgent DI setup — simulated target
if (job.Target?.Type == "Simulated")
    services.AddSimulatedWorkItemImport(job.Target);
// In MigrationAgent DI setup — ADO target
else if (job.Target?.Type == "AzureDevOpsServices")
    services.AddAzureDevOpsWorkItemImport(job.Target);
```

`AddSimulatedWorkItemImport` registers `SimulatedWorkItemImportSink` as `IWorkItemImportSink`.  
`AddAzureDevOpsWorkItemImport` registers `AzureDevOpsWorkItemImportSink` as `IWorkItemImportSink`.

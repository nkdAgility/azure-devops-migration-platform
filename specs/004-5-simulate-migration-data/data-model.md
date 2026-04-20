# Data Model: Simulated Data Source for End-to-End Migration Testing

**Date**: 2026-04-09  
**Branch**: `copilot/simulate-migration-data`

---

## Entities

### 1. `SimulatedSourceOptions`

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedSourceOptions.cs`  
**Section name**: `"source"` (when `source.type == "Simulated"`)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `Seed` | `int?` | No | *(auto-generated)* | Random seed for deterministic generation. If null, a seed is chosen by `System.Random.Shared.Next()`, logged at Information, and written to `manifest.json`. |
| `WorkItemCount` | `int` | Yes | — | Total number of work items to generate. Minimum 1. Verified to complete at 25,000. |
| `ProjectCount` | `int` | No | `1` | Number of simulated projects. Work items are distributed evenly across projects. |
| `WorkItemTypeDistribution` | `Dictionary<string, int>?` | No | `{"Bug": 40, "Task": 40, "Feature": 20}` | Percentage distribution of work item types. Values must sum to 100. |
| `AvgRevisionsPerItem` | `int` | No | `3` | Average number of revisions per work item. Actual count is `seed`-derived and varies ±50% around this value (minimum 1). |
| `IncludeAttachments` | `bool` | No | `false` | Whether to generate and write attachment binary files to the package. |
| `IncludeLinks` | `bool` | No | `true` | Whether to generate `relatedLinks`, `externalLinks`, and `hyperlinks` in revisions. |
| `AttachmentSizeBytes` | `int` | No | `4096` | Size of each generated attachment binary in bytes. Only used when `IncludeAttachments = true`. |

**Validation rules**:
- `[Required, Range(1, int.MaxValue)]` on `WorkItemCount`
- `[Range(1, int.MaxValue)]` on `ProjectCount`
- `[Range(1, 100)]` on `AvgRevisionsPerItem`
- Custom validator: `WorkItemTypeDistribution` values must sum to exactly 100 if provided
- `[Range(1, 104857600)]` on `AttachmentSizeBytes` (max 100 MB)

---

### 2. `SimulatedTargetOptions`

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedTargetOptions.cs`  
**Section name**: `"target"` (when `target.type == "Simulated"`)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `ValidateOnWrite` | `bool` | No | `true` | Validate each `WorkItemRevision` against the package schema before accepting it. Writes validation errors to `Logs/simulated-import-validation.jsonl`. |
| `FailOnFirstError` | `bool` | No | `true` | If `ValidateOnWrite` is true and a revision fails validation, fail the import immediately. When false, all errors are collected and reported at the end. |

---

### 3. `SimulatedWorkItem` *(internal, never serialised)*

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Generation/` (internal class)

Represents the in-memory descriptor used during generation. Not serialised to disk; instead, each revision is yielded as a `WorkItemRevision` (defined in `Abstractions`) to the orchestrator.

| Field | Type | Description |
|-------|------|-------------|
| `WorkItemId` | `int` | Unique ID, 1-based sequential |
| `Type` | `string` | e.g., `"Bug"`, `"Task"`, `"Feature"` — derived from type distribution |
| `ProjectName` | `string` | e.g., `"[SIMULATED] Project 1"` |
| `RevisionCount` | `int` | Total revisions for this item (seed-derived, AvgRevisionsPerItem ± variance) |
| `BaseDate` | `DateTimeOffset` | Seed-derived creation date spanning a realistic 2-year range |

---

### 4. `SimulatedRevisionStream`

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Generation/SimulatedRevisionStream.cs`

The `IAsyncEnumerable<WorkItemRevision>` implementation. Generates revisions lazily on demand. Never materialises all revisions.

**Generation algorithm**:
1. Initialise `System.Random(seed)` once.
2. For each work item `i` in `[1..WorkItemCount]`:
   a. Determine `type`, `projectName`, `revisionCount`, `baseDate` from `Random`.
   b. For each revision `r` in `[0..revisionCount-1]`:
      - Compute `changedDate = baseDate + Random.Next(1, 86400) * r seconds`
      - Compute `ticks = changedDate.UtcTicks`
      - Build `fields` array with `[SIMULATED]`-prefixed values
      - Build `relatedLinks` (to items with lower IDs, if `IncludeLinks`)
      - Build `attachments` metadata (if `IncludeAttachments`)
      - `yield return new WorkItemRevision { WorkItemId = i, RevisionIndex = r, ChangedDate = changedDate, ... }`
3. Total revisions yielded = `WorkItemCount × AvgRevisionsPerItem` ± variance.

**Memory guarantee**: Only the current `WorkItemRevision` is in memory at any time. No `List<WorkItemRevision>` is ever created.

---

### 5. `SimulatedIdentitySet`

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Generation/SimulatedIdentitySet.cs`

A fixed set of 10 synthetic user identities used in `System.AssignedTo` and other identity fields. The same set is used for every simulated run (seed-independent), so identity mapping exercises the `IdentitiesModule` consistently.

| Field | Example Value |
|-------|---------------|
| `DisplayName` | `"[SIMULATED] Alice Turing"` |
| `UniqueName` | `"alice.turing@simulated.invalid"` |
| `Descriptor` | `"simulated:alice-turing-uuid"` |

All display names are prefixed with `[SIMULATED]` to ensure simulated packages are visually distinguishable from real exports.

---

### 6. `IWorkItemImportSink` *(new abstraction)*

**Location**: `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportSink.cs`

```csharp
public interface IWorkItemImportSink
{
    /// <summary>
    /// Writes a single revision from the package to the target system.
    /// Called once per revision folder during streaming import.
    /// </summary>
    /// <param name="revision">The revision to write.</param>
    /// <param name="packageStore">The artefact store for reading attachment binaries.</param>
    /// <param name="revisionFolderPath">Relative path to the revision folder (for attachment streaming).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteRevisionAsync(
        WorkItemRevision revision,
        IArtefactStore packageStore,
        string revisionFolderPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called after all revisions have been written. Used for final counts/summary.
    /// </summary>
    Task CompleteAsync(CancellationToken cancellationToken);
}
```

---

### 7. `SimulatedWorkItemImportSink`

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemImportSink.cs`

Implements `IWorkItemImportSink`. On each call to `WriteRevisionAsync`:
1. If `ValidateOnWrite = true`, validates the `WorkItemRevision` against required schema fields.
2. Increments an internal revision counter.
3. If validation fails and `FailOnFirstError = true`, throws `SimulatedImportValidationException`.
4. Otherwise, appends the error to an internal error list.
5. On `CompleteAsync`, writes a summary line to `Logs/simulated-import-summary.jsonl` via `IProgressSink`.

Does not write to any external system.

---

### 8. `WorkItemRevision` field conventions for simulated data

All synthetic `WorkItemRevision` objects produced by the simulated source conform to the existing `WorkItemRevision` record defined in `Abstractions`. Key field conventions:

| Field (`ReferenceName`) | Simulated value pattern |
|------------------------|------------------------|
| `System.Title` | `"[SIMULATED] {Type} #{WorkItemId} rev {RevisionIndex}"` |
| `System.WorkItemType` | Type from distribution (e.g. `"Bug"`) |
| `System.TeamProject` | `"[SIMULATED] Project {N}"` |
| `System.AssignedTo` | Cycles through `SimulatedIdentitySet` |
| `System.State` | State appropriate for type and revision index (progresses: `New → Active → Closed`) |
| `System.CreatedDate` | ISO 8601 UTC; seed-derived |
| `System.ChangedDate` | ISO 8601 UTC; seed-derived per revision |
| `System.Description` | `"[SIMULATED] Generated description #{seed}-{id}-{rev}"` |

---

### 9. `manifest.json` Extension for Simulated Source

When `source.type == "Simulated"`, the `source` block in `manifest.json` is extended:

```json
"source": {
  "type": "Simulated",
  "orgOrCollection": "simulated://localhost",
  "project": "[SIMULATED] Project 1",
  "apiVersion": "0.0",
  "simulatedSeed": 42,
  "simulatedWorkItemCount": 25000
}
```

`simulatedSeed` and `simulatedWorkItemCount` are written by `SimulatedWorkItemRevisionSourceFactory` after the seed is resolved. These fields are ignored by non-simulated consumers and are backward-compatible (manifest `packageVersion` does not change).

---

## State Transitions

### Work Item State Machine (per type)

| Type | Revision 0 | Revision 1 | Revision 2+ |
|------|-----------|-----------|-------------|
| `Bug` | `New` | `Active` | `Resolved` / `Closed` (alternates) |
| `Task` | `New` | `Active` | `Closed` |
| `Feature` | `New` | `Active` | `Active` (last revision = `Closed`) |

State values are deterministic: derived from `(revisionIndex % stateCount)` with seed-derived variation.

---

## Validation Rules Summary

| Rule | Where enforced |
|------|---------------|
| `SimulatedSourceOptions.WorkItemCount ≥ 1` | `[Required, Range]` attribute on options class; validated by ASP.NET Options validation at startup |
| `WorkItemTypeDistribution` values sum to 100 | Custom `IValidateOptions<SimulatedSourceOptions>` |
| `SimulatedRevisionStream` never buffers all revisions | Code contract + unit tests |
| All `WorkItemRevision` required fields populated | `SimulatedRevisionStream` generation logic |
| Revision JSON passes platform validation | SC-005 verified by `[TestCategory("SystemTest")]` test |

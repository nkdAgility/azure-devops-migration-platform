# Checkpointing

## Where to Read Next

- Architecture subsystem index: [.agents/context/architecture/agent-checkpoint-phase-tracking.md](architecture/agent-checkpoint-phase-tracking.md)
- Decision record: [docs/adr/0003-cursor-based-checkpointing.md](../../docs/adr/0003-cursor-based-checkpointing.md)
- Complementary task-level resume: [docs/adr/0010-plan-driven-dag-execution.md](../../docs/adr/0010-plan-driven-dag-execution.md)
- Enforced rules: [.agents/guardrails/migration-rules.md](../guardrails/migration-rules.md), [.agents/guardrails/module-rules.md](../guardrails/module-rules.md), [.agents/guardrails/package-rules.md](../guardrails/package-rules.md)

This file remains the canonical context summary for cursor schema, phase records, and resume semantics.

## 6. Cursor-Based Checkpointing

Instead of per-work-item watermark tables, the system uses forward-only project cursors stored as JSON files. This requires no database and makes resume O(1).

> **Package split:** Root `.migration/` contains authoritative package-level orchestration state. Project-level resume state lives in `/{org}/{project}/.migration/` so analytics-style runs across multiple orgs/projects do not collide. Run-scoped audit copies and logs live under `.migration/runs/<runId>/` and are never authoritative for resume.

### Cursor File Location

```
/{org}/{project}/.migration/{action}.{module}.cursor.json
```

### Schema

```json
{
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "Completed",
  "updatedAt": "2026-02-25T18:12:34Z"
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `lastProcessed` | string | Relative path within the project subtree to the last successfully processed revision folder |
| `stage` | string | Last completed stage — must be one of the canonical values below |
| `updatedAt` | ISO 8601 string | UTC timestamp of the last cursor update |

### Canonical Stage Values

All modules must use these exact string values. Deviation is a schema violation.

| Value | Meaning |
|---|---|
| `CreatedOrUpdated` | Target work item was created or identified |
| `AppliedFields` | Revision field values were written to the target |
| `AppliedLinks` | Related, external, and hyperlinks were applied |
| `UploadedAttachments` | Binary files were uploaded and attached |
| `Completed` | All stages for this revision folder succeeded |

### Cursor is a Folder Path

The cursor value is the relative path of the last processed revision folder — the folder itself, not a row ID or sequence number. This makes it directly usable as a filesystem seek position.

### Resume Logic

1. Begin enumerating `WorkItems/` as normal.
2. Skip all folders whose path is lexicographically less than or equal to `lastProcessed`.
3. If `stage` is not `Completed`, resume processing within the `lastProcessed` folder starting from the next stage.
4. Continue forward.

### Stage Progression

```
CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed
```

The cursor is written after each stage completes. A crash between stages leaves the cursor at the last completed stage, enabling fine-grained resume.

### Query Fingerprint Compatibility (Resumable Batching)

When `IWorkItemFetchService` callers opt in to resumable batching (`ResumeEnabled = true`), a query fingerprint is computed and stored alongside the `BatchContinuationToken`. This fingerprint gates resume safety.

#### Fingerprint Scope

`IQueryFingerprintService` computes a deterministic SHA-256 hash from:
- The WIQL query text (normalised)
- Lexicographically sorted query parameters

Post-fetch filters (e.g. `WorkItemFieldFilterEvaluator`) are explicitly **excluded** from the fingerprint. They are caller-level post-processing, not part of the enumeration contract.

#### Mismatch Decision Behavior

On resume, the fetch service compares the current fingerprint with the one stored in the saved token:

| Condition | Outcome |
|-----------|---------|
| Fingerprints match | `ResumeDecision.Accepted` — enumeration continues from saved position |
| Fingerprints differ | `ResumeDecision.RejectedQueryMismatch` — `ResumeRejectedException` thrown with both fingerprints in payload |
| Token missing/malformed | `ResumeDecision.Unavailable` — start from beginning (no exception) |
| Unknown `StrategyVersion` | `ResumeDecision.RejectedQueryMismatch` with reason `"incompatible_strategy_version"` |

The strategy never auto-recovers from mismatch. Callers own the recovery decision (fail, discard + fresh start, or log and continue).

#### Continuation Token Storage

Continuation tokens are stored under `.migration/Checkpoints/` via `ICheckpointingService` methods (`ReadContinuationTokenAsync`, `WriteContinuationTokenAsync`, `DeleteContinuationTokenAsync`). The path is resolved by `PackagePaths.ContinuationFile()`. These paths do not conflict with existing cursor files.

### Project Cursors

Each project maintains its own cursor files under its project-local `.migration/` folder:

```
/{org}/{project}/.migration/
  inventory.workitems.cursor.json    ← Inventory phase, WorkItems module
  export.workitems.cursor.json       ← Export phase, WorkItems module
  import.workitems.cursor.json       ← Import phase, WorkItems module
  export.identities.cursor.json      ← Export phase, Identities module
  import.teams.cursor.json           ← Import phase, Teams module
```

The convention is `{action}.{module}.cursor.json` beneath the project subtree. Modules and phases must not share cursor files.

Action-qualified cursor identity is mandatory. Legacy non-action keys are compatibility fallback only and are never authoritative when a project-scoped action-qualified cursor exists.

`ForceFresh` mode (via `--force-fresh` CLI flag or `Job.Resume.Mode = ForceFresh`) deletes project cursor files and root phase marker files before execution begins. Shared ID mapping state is preserved.

### Root Package State

Root `.migration/` remains authoritative for package-level orchestration state:

```
/.migration/
  migration-config.json
  plan.json
  inventory.complete.json
  prepare.complete.json
  validate.complete.json
  runs/
    <runId>/
      job.json
      plan.json
      config.json
      logs/
```

Completion markers stay at the root because they gate package-wide phases, not project-local item enumeration. Files under `.migration/runs/<runId>/` are audit copies for a single execution and must not be used as the authoritative source for later runs.

### ID Map

The root `.migration/idmap.db` (or `idmap.json`) file tracks source-to-target work item ID mappings and uploaded attachment records. It is written during Stage `CreatedOrUpdated` (work item ID) and Stage `UploadedAttachments` (attachment ID per revision). It is the sole mechanism for idempotency checks during resume. See [.agents/context/identity-and-mapping.md](identity-and-mapping.md) for the identity mapping counterpart.

---

## Export Cursor Behaviour

Export modules use the same cursor schema as import. The key difference is that export has no intra-item stages — a revision folder is either fully written or not written at all.

- Export modules write `stage: "Completed"` after each revision folder is successfully written to the package.
- The `lastProcessed` field holds the relative path of the last revision folder written (e.g. `WorkItems/2026-04-10/638760123456789012-42-17/`).
- The cursor is updated after every individual revision folder so that an interruption results in at most one revision folder of re-work on resume.

### Two-Tier Resume Skip Strategy

On resume the orchestrator applies two checks in order:

**Tier 1 — Progress store (fast-forward by revision index)**

`export_progress.db` records the `RevisionIndex` of the last successfully written revision for each work item. The orchestrator queries this store **once per work item** on first encounter, then skips any revision whose `RevisionIndex ≤ storedRev` in O(1) memory without touching the filesystem.

- A fully-exported work item (all revisions ≤ `storedRev`) is skipped in its entirety.
- A partially-exported work item (crashed mid-item) resumes from the first unwritten revision automatically.
- The store is absent on packages created before this feature — those fall through to Tier 2.

**Tier 2 — ExistsAsync fallback**

`IArtefactStore.ExistsAsync("{folderPath}revision.json")` is called for any revision not covered by the progress store. If the file already exists the revision was fully exported and is skipped.

A lexicographic path comparison is **not used** for export because `AzureDevOpsWorkItemRevisionSource` delivers work items in reverse-chronological creation-date window order (newest first); folder paths from older windows sort below the cursor even when those revisions were never exported.

---

## Migrate-Mode Phase Tracking

When a job runs in `Migrate` mode (export → prepare → import), a top-level phase record tracks whether each phase has completed. This allows a re-run to skip completed phases entirely.

### Phase Record Location

```
/.migration/job.phase.json
```

### Schema

```json
{
  "exportCompleted": true,
  "prepareCompleted": true,
  "importCompleted": false,
  "updatedAt": "2026-04-10T12:34:56Z"
}
```

### Resume Logic (Migrate Mode)

1. Read root `.migration/job.phase.json` before running any module.
2. If `exportCompleted: true` → skip all export-phase modules; jump to prepare phase.
3. If `prepareCompleted: true` → skip prepare-phase modules; jump to import phase (but abort if blocking issues exist in prepare reports).
4. If `importCompleted: true` → skip import-phase modules too; job is already complete.
5. Otherwise run from the first incomplete phase, with each module resuming from its own cursor.

The phase record is absent for `Export`-only, `Prepare`-only, or `Import`-only jobs. `PhaseTrackingService` returns a default record (all flags `false`) when the file is missing.

### Prepare Checkpoint

In addition to the phase record, a standalone marker file records successful Prepare completion:

```
/.migration/prepare.complete.json
```

Import mode checks for this marker. If absent, Import auto-runs Prepare first and aborts on any blocking issues.

### Run Scope

Each job execution also gets a run folder:

```
/.migration/runs/<runId>/
  job.json
  plan.json
  config.json
  logs/
```

This run scope is audit-only. `job.json`, `plan.json`, and `config.json` here are copies of what was executed for traceability. Resume, phase gates, and package orchestration must continue to read the authoritative state from root `.migration/` and project-local cursor files.

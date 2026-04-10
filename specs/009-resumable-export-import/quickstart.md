# Quickstart: Resumable Export and Import

**Feature**: 009-resumable-export-import

This guide explains the resume behaviour introduced by this feature and how to use it.

---

## Default Behaviour — Automatic Resume

No configuration change is needed to benefit from resume. When you re-run an export or import command pointing to the same package path, the system automatically reads the existing cursor and skips items already processed.

### Export

```json
// scenarios/export-ado-workitems-single-project.json
{
  "Mode": "Export",
  "Artefacts": {
    "Path": "%TEMP%\\MyPackage"
  }
}
```

```
devopsmigration export --config scenarios/export-ado-workitems-single-project.json
```

If the previous run exported 20,000 of 25,000 work items and was interrupted:
- On re-run, the export reads `%TEMP%\MyPackage\Checkpoints\workitems.cursor.json`
- Skips revisions at or before the cursor position
- Continues from the next unprocessed revision
- Emits a progress event: `"[WorkItems] Resuming from WorkItems/2026-04-10/…-42-5/ (20,000 items skipped)"`

### Import

```
devopsmigration import --config scenarios/import-ado-workitems-single-project.json
```

If the previous import was interrupted mid-way through a work item (e.g. fields applied but links not yet applied):
- On re-run, reads `Checkpoints/workitems.cursor.json`
- Skips fully completed revision folders
- For the partially processed folder, resumes at the next incomplete stage
- Stages already completed are not repeated (idempotency via idmap)

---

## Forced Fresh Start

To discard all existing progress and re-run from scratch, use `--force-fresh`:

```
devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh
devopsmigration import --config scenarios/import-ado-workitems-single-project.json --force-fresh
devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json --force-fresh
```

This deletes all module cursor files under `Checkpoints/` and the phase record `Checkpoints/job.phase.json`, then re-processes all items from the beginning.

> **Note**: `--force-fresh` for export does **not** delete existing revision files from the package. It only resets the progress tracking. Previously exported revision folders will be overwritten as the exporterpasses through them again.

---

## Check Resume State Before Running

To see what the current package state contains without submitting a job:

```
devopsmigration export --config scenarios/export-ado-workitems-single-project.json --dry-run
```

Sample output:

```
Resume state for package: C:\Temp\MyPackage

  Module: WorkItems (Export)
    Cursor:            WorkItems/2026-04-10/00638500000000000-42-17/ (Completed)
    Items past cursor: ~20,000 (will be skipped)
    Items remaining:   unknown without querying source

No job submitted. Use without --dry-run to execute.
```

---

## Both-Mode Phase Resume

When running a job with `Mode: Both` and the export phase completes but the import phase is interrupted:

```
devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json
```

On re-run, the agent reads `Checkpoints/job.phase.json`:
- If `exportCompleted: true` → the export phase is skipped entirely
- Import resumes from its cursor position

To re-run the export as well:

```
devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json --force-fresh
```

---

## How It Works (Internals)

### Cursor files

Each module writes a cursor to `Checkpoints/<modulename>.cursor.json` after each successfully processed unit:

```json
{
  "lastProcessed": "WorkItems/2026-04-10/00638500000000000-42-17/",
  "stage": "Completed",
  "updatedAt": "2026-04-10T12:34:56Z"
}
```

On the next run, the module reads this file, discards all items at or before `lastProcessed`, and processes only the remainder.

### Stage-level resume (import only)

Import processes each revision folder through four stages. If interrupted between stages, the cursor records the last completed stage. On resume, processing skips completed stages and starts from the next incomplete stage:

| `stage` in cursor | Resume starts from |
|---|---|
| `CreatedOrUpdated` | Stage B (AppliedFields) |
| `AppliedFields` | Stage C (AppliedLinks) |
| `AppliedLinks` | Stage D (UploadedAttachments) |
| `UploadedAttachments` | Stage — (Completed) |
| `Completed` | Move to next folder |

### Idempotency

- **Work item creation (Stage A)**: `Checkpoints/idmap.json` maps source IDs to target IDs. If a mapping exists, creation is skipped and the existing target ID is used.
- **Attachment upload (Stage D)**: `Checkpoints/idmap.json` also records `(workItemId, revisionIndex, relativePath) → targetAttachmentId`. If present, upload is skipped.

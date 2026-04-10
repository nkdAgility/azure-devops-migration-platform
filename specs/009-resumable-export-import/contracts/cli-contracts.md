# CLI Contract: Resume Mode Options

**Feature**: 009-resumable-export-import  
**Affected commands**: `export`, `import`, `migrate`

---

## `--force-fresh` Flag

Added to the three migration commands. When supplied, the agent deletes all cursor files and the job phase record, then re-enumerates from the beginning. The identity map (`Checkpoints/idmap.json`) is **preserved** so that already-created target work items and already-uploaded attachments are not duplicated.

`migrate` supports this flag because it is used as an incremental sync command — re-running it continues export from where it left off and import from where it left off. `--force-fresh` resets that position to the beginning while still being idempotent.

### Usage

```
devopsmigration export  --config <path> [--force-fresh]
devopsmigration import  --config <path> [--force-fresh]
devopsmigration migrate --config <path> [--force-fresh]
```

### Semantics

| Flag present | Behaviour |
|---|---|
| Absent (default) | `ResumeMode = Auto` — detect existing cursor and resume from the last recorded position, skipping all items before it without re-checking them |
| `--force-fresh` | `ResumeMode = ForceFresh` — delete all module cursor files and `Checkpoints/job.phase.json`; re-enumerate all items from the beginning; use idmap and per-item existence checks for idempotency |

The key distinction: **Auto** trusts the cursor and skips everything before it without inspection. **ForceFresh** discards the cursor and re-examines every item from the start, but remains idempotent (no duplicate work items or re-uploaded attachments) because the identity map is preserved.

The flag is encoded as `MigrationJob.Resume.Mode = ForceFresh` before job submission. The control plane forwards it unchanged to the agent. The agent applies force-fresh logic before invoking any module.

---

---

## `launch.json` Entries Required

Per coding standards, every new or changed CLI command must have a corresponding entry in `.vscode/launch.json`.

New entries needed:

| Profile name | Command |
|---|---|
| `export: force-fresh (export-ado-workitems-single-project)` | `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh` |
| `import: force-fresh` | `devopsmigration import --config scenarios/import-ado-workitems-single-project.json --force-fresh` |
| `migrate: force-fresh` | `devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json --force-fresh` |

---

## `MigrationJob` Wire Format Addition

The `resume` block is optional. Absence or `null` means `Auto`.

```json
{
  "jobId": "...",
  "mode": "Export",
  "resume": {
    "mode": "Auto"
  }
}
```

```json
{
  "jobId": "...",
  "mode": "Export",
  "resume": {
    "mode": "ForceFresh"
  }
}
```

No other fields on `MigrationJob` are changed. This is a backwards-compatible additive change.

---

## `Checkpoints/job.phase.json` File Contract

Written and read by `PhaseTrackingService` via `IStateStore`. Not exposed externally. Stored at key `"Checkpoints/job.phase.json"` in the package.

```json
{
  "exportCompleted": true,
  "importCompleted": false,
  "updatedAt": "2026-04-10T12:34:56Z"
}
```

This file is only present for jobs that have run in Both mode. For Export-only or Import-only jobs, it is absent and `PhaseTrackingService` returns a default record with both flags `false`.

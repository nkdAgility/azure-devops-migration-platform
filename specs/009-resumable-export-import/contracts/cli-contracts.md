# CLI Contract: Resume Mode Options

**Feature**: 009-resumable-export-import  
**Affected commands**: `export`, `import`, `migrate`

---

## `--force-fresh` Flag

Added to the three migration commands. When supplied, the agent discards all existing cursor state and phase records before starting, re-processing all items from scratch.

### Usage

```
devopsmigration export  --config <path> [--force-fresh]
devopsmigration import  --config <path> [--force-fresh]
devopsmigration migrate --config <path> [--force-fresh]
```

### Semantics

| Flag present | Behaviour |
|---|---|
| Absent (default) | `ResumeMode = Auto` — detect existing cursor and resume from last position |
| `--force-fresh` | `ResumeMode = ForceFresh` — delete all module cursors and `Checkpoints/job.phase.json` before running |

The flag is encoded as `MigrationJob.Resume.Mode = ForceFresh` before job submission. The control plane forwards it unchanged to the agent. The agent applies force-fresh logic before invoking any module.

---

## `--dry-run` Flag (Operator Visibility)

Added to `export`, `import`, and `migrate` commands. When supplied, the CLI reads the cursor state from the package and reports what would be skipped/re-run without actually running the job.

### Usage

```
devopsmigration export  --config <path> --dry-run
devopsmigration import  --config <path> --dry-run
devopsmigration migrate --config <path> --dry-run
```

### Observable Output

```
Resume state for package: %TEMP%\SystemTests\export-ado-workitems-single-project

  Module: WorkItems (Export)
    Cursor:        WorkItems/2026-04-10/00638500000000000-42-17/ (Completed)
    Items past cursor: 20,000 estimated (will be skipped)
    Items remaining:   ~5,000 (will be exported)

  Phase record:   Export = Completed, Import = NotStarted

No job submitted. Use without --dry-run to execute.
```

The output must include:
- Module name and operation (Export or Import)
- Cursor `lastProcessed` path and `stage`
- A human-readable estimate of items past and remaining (best-effort; may say "unknown" if count cannot be determined without querying source)
- Phase record status (Both-mode only)
- A clear statement that no job was submitted

### System Test Requirement

A `[TestCategory("SystemTest")]` test must assert that running with `--dry-run` produces output containing at minimum:
- The word `"skipped"` or similar
- The cursor path
- `"No job submitted"`

---

## `launch.json` Entries Required

Per coding standards, every new or changed CLI command must have a corresponding entry in `.vscode/launch.json`.

New entries needed:

| Profile name | Command |
|---|---|
| `export: force-fresh (export-ado-workitems-single-project)` | `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh` |
| `import: force-fresh` | `devopsmigration import --config scenarios/import-ado-workitems-single-project.json --force-fresh` |
| `migrate: force-fresh` | `devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json --force-fresh` |
| `export: dry-run` | `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --dry-run` |
| `import: dry-run` | `devopsmigration import --config scenarios/import-ado-workitems-single-project.json --dry-run` |
| `migrate: dry-run` | `devopsmigration migrate --config scenarios/migrate-ado-workitems-single-project.json --dry-run` |

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

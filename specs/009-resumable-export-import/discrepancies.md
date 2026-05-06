# Architecture Discrepancies

**Feature**: Resumable Export and Import  
**Flagged by**: speckit.specify  
**Status**: Resolved

## Discrepancies

### Export cursor schema not specified in checkpointing-summary.md

- **Source doc**: `.agents/context/checkpointing-summary.md`
- **Section**: "Schema" and "Canonical Stage Values"
- **Issue**: The checkpointing doc defines cursor schema and stage values but frames them entirely around import (the four import stages: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`). It does not address how export uses the cursor — specifically, that export always writes `stage: "Completed"` after each item since export has no intra-item stages. The export cursor's `lastProcessed` field also needs a definition of what path it references during export (the revision folder last written to the package).
- **Suggested update**: Add an "Export Cursor Behaviour" subsection to the checkpointing doc stating: (1) export modules write `stage: "Completed"` after each revision folder is successfully written; (2) the `lastProcessed` field holds the relative path of the last revision folder written to the package; (3) the cursor is updated after each item to bound re-work on resume.
- **Status**: ✓ Resolved in speckit.implement

### MigrationJob schema does not include a resume mode or fresh-start flag

- **Source doc**: `.agents/context/job-lifecycle.md`
- **Section**: "Schema" and "Fields"
- **Issue**: The `MigrationJob` schema has no field to signal forced fresh-start behaviour. The spec (FR-007) requires operators to be able to trigger a fresh start that discards the existing cursor. This must either be a flag in the `MigrationJob` schema or a pre-job action the CLI takes before submitting. Neither is documented.
- **Suggested update**: Add a `resume` block to the `MigrationJob` schema (or equivalent CLI-level option). Example: `"resume": { "mode": "Auto | ForceFresh" }`. Document that `Auto` (the default) detects and uses an existing cursor; `ForceFresh` deletes cursors for all modules before the job begins.
- **Status**: ✓ Resolved in speckit.implement

### Checkpointing doc does not describe Both-mode phase-level resume

- **Source doc**: `.agents/context/checkpointing-summary.md`
- **Section**: "Resume Logic"
- **Issue**: The doc describes resume logic only within a single phase (import). It does not describe how a Both-mode job tracks that the export phase is complete so that a re-run skips export and only resumes import. Phase completion is a superset of module cursor state.
- **Suggested update**: Add a "Both-Mode Phase Tracking" section describing: (1) that Both-mode jobs maintain a top-level phase cursor (e.g. `Checkpoints/job.phase.json`) recording `export: Completed | InProgress` and `import: Completed | InProgress | NotStarted`; (2) resume logic checks this file first; if `export: Completed`, the export phase is skipped.
- **Status**: ✓ Resolved in speckit.implement

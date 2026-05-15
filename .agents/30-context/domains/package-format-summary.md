# Package Format Summary

Short reference for agents. See `docs/package-format-reference.md` for the canonical human-readable reference and `migration-package-concept.md` for the fuller agent context.

## Scope Model

- Root `.migration/` holds authoritative package-wide orchestration state shared across runs.
- `/{org}/{project}/.migration/` holds authoritative project-scoped cursors.
- `.migration/runs/<runId>/` holds run-scoped audit copies and logs only.
- Cursor keys are action-qualified (`<action>.<module>`) to isolate inventory/export/import resume identity.

## Top-Level Layout

```text
<WorkingDirectory>/
  .migration/
    migration-config.json
    plan.json
    *.complete.json
    Checkpoints/
      idmap.db
      export_progress.db
    runs/
      <runId>/
        job.json
        plan.json
        config.json
        logs/
          progress.ndjson
          diagnostics.ndjson
  <org>/
    <project>/
      manifest.json
      .migration/
        inventory.<module>.cursor.json
        export.<module>.cursor.json
        import.<module>.cursor.json
      Identities/
      WorkItems/
      Teams/
      Nodes/
      Permissions/
```

## Key Files

| File | Contents |
| --- | --- |
| `.migration/migration-config.json` | Resolved config shared by the package |
| `manifest.json` | Package metadata: version, modules run, timestamps |
| `/{org}/{project}/.migration/<action>.<module>.cursor.json` | Resume cursor for one project, action, and module |
| `.migration/runs/<runId>/logs/progress.ndjson` | Per-event structured progress log for one run |
| `.migration/runs/<runId>/logs/diagnostics.ndjson` | Structured diagnostic log for one run |
| `.migration/runs/<runId>/job.json` | Audit copy of the leased job |
| `Identities/mapping.json` | Operator-editable identity map |

## WorkItems Layout

```text
WorkItems/
  <yyyy-MM-dd>/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      attachments/
        <filename>
```

- Folders are lexicographically chronological.
- Attachment files are beside the revision data.
- Each revision folder is one cursor position.

## Determinism Rules

- All folder names are deterministic given source data.
- No random IDs or timestamps in path components.
- Work item revision folders sort lexicographically in chronological order.
- Run folders use `<yyyyMMdd-HHmmss>` and are audit-only, not resume state.





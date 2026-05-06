# Package Format Summary

Short reference for agents. See `docs/package-format-reference.md` for the full specification.

## Top-Level Layout

```
<WorkingDirectory>/
  <org>/
    <project>/
      migration-config.json
      manifest.json
      .migration/
        Checkpoints/
        Logs/
        State/
      Identities/
      WorkItems/
      Teams/
      Nodes/
      Permissions/
```

## Key Files

| File | Contents |
|---|---|
| `migration-config.json` | Resolved config written by the agent at startup |
| `manifest.json` | Package metadata: version, modules run, timestamps |
| `.migration/Checkpoints/<module>-<phase>.cursor` | Resume cursor for each module+phase |
| `.migration/Logs/progress.jsonl` | Per-event structured progress log |
| `Identities/mapping.json` | Operator-editable identity map |

## WorkItems Layout

```
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
- All files are human-readable JSON (except binary attachments).
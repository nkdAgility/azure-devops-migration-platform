# Package Format

Compressed agent context for the migration package. See `docs/package-format-reference.md` for the canonical human-readable reference and `docs/package-guide.md` for operator guidance.

## 1. Package Root Resolution

`PackageRoot` is the configured `Package.WorkingDirectory`. Project artefacts live beneath org/project subfolders inside that root:

```text
<WorkingDirectory>/
  .migration/
  <org-folder-name>/<project>/
```

For example, given:

- `WorkingDirectory`: `storage\my-export`
- Organisation URL: `https://dev.azure.com/contoso`
- Project: `MyProject`

The resulting project subtree is: `storage\my-export\contoso\MyProject\`

The org folder name is extracted from the organisation URL (the last path segment, e.g. `contoso` from `https://dev.azure.com/contoso`). For TFS collection URLs like `http://tfs:8080/tfs/DefaultCollection`, the folder name is `DefaultCollection`.

This resolution is performed by `PathUtilities.ExtractOrgFolderName()` and applied when project-relative artefact paths are resolved. `WorkItems` and other module folders never appear directly under `WorkingDirectory` — they always live under `<org>/<project>/`.

## 2. Package Structure (Canonical Format)

```text
PackageRoot/
  .migration/
    migration-config.json
    plan.json
    inventory.complete.json
    prepare.complete.json
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
  {org}/{project}/
    manifest.json
    WorkItems/
    Nodes/
    referenced-paths.json   ← all area/iteration paths referenced by exported work item revisions
    source-tree.json        ← full classification tree snapshot (area + iteration) from the source
    prepare-report.json     ← written by PrepareAsync: node existence validation against target
    Teams/
      {team-slug}/
        team.json             ← team definition, settings, iterations, members, capacity, area paths
      prepare-report.json     ← written by PrepareAsync: team/group validation against target
    Permissions/
      prepare-report.json     ← written by PrepareAsync: ACL compatibility validation against target
    Builds/
    Git/
    Identities/
      descriptors.jsonl       ← one identity descriptor JSON per line (JSONL format)
      mapping.json            ← operator-editable identity override map (source → target)
      unresolved.json         ← identities that could not be auto-resolved
      prepare-report.json     ← written by PrepareAsync: identity auto-match and unresolved report
    .migration/
      inventory.workitems.cursor.json
      export.workitems.cursor.json
      import.workitems.cursor.json
      export.identities.cursor.json
```

> **Legacy fallback:** Packages created before the split between root `.migration/` and project-local `.migration/` may store cursor files under root `.migration/Checkpoints/` (or legacy `Checkpoints/`). Readers should try the new project-local location first, then fall back to the older root-level locations.

### Scope Semantics

- Root `.migration/` is authoritative package state shared across runs.
- `/{org}/{project}/.migration/` is authoritative project-scoped resume state.
- `.migration/runs/<runId>/` is run-scoped audit output only.
- Cursor identity is action-qualified by design (`<action>.<module>`), so inventory/export/import never collide.

Run-scoped `job.json`, `plan.json`, and `config.json` are copies of what was executed for that run. They are not the source of truth for later resume or phase-gate decisions.

The WorkItems layout is canonical and must not be altered:

```text
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      [comment.json]          ← written when a comment edit/delete is detected (disable with inlineComments.enabled: false)
      <attachment files>
      <embedded image files>
    <ticks>-<workItemId>-c<commentId>/
      comment.json
      <embedded image files>
```

Key characteristics:

- Chronological ordering is guaranteed across revision sub-folders and comment sub-folders
- No global index required
- Each comment version (original + edits) stored in a separate comment sub-folder
- Embedded images stored beside `revision.json` (for revision-field images) or beside `comment.json` (for comment images)
- Streaming import processes revision and comment sub-folders together in lexicographic (chronological) order
- Resume is trivial
- Human-auditable

### Run Log Folder

The `.migration/runs/<runId>/logs/` folder contains structured observability records written by the Migration Agent during one specific job execution:

```text
logs/
    progress.ndjson
    diagnostics.ndjson
    diagnostics-001.ndjson   ← rotated segment (when max size exceeded)
```

The run folder name uses second-level UTC timestamp format `<yyyyMMdd-HHmmss>` (for example `20260506-143822`) so folders sort chronologically.

Each run folder also contains `job.json`, `plan.json`, and `config.json` as audit copies of what was executed.

| File | Format | Description |
| --- | --- | --- |
| `progress.ndjson` | NDJSON | One `ProgressEvent` record per line. Tracks module cursor state, stage transitions, and item counts. Written through `IPackageAccess.AppendLogAsync` by `PackageProgressSink`. |
| `diagnostics.ndjson` | NDJSON | Structured diagnostic log records (ILogger output). Each line is a JSON object with `timestamp`, `level`, `category`, `message`, and optional `exception` fields. Written through `IPackageAccess.AppendLogAsync` by `PackageLoggerProvider`. |
| `agent-NNN.jsonl` | NDJSON | Rotated log segments when the primary segment exceeds the configured max size. |

Both files are append-only and survive resume. They are the durable audit record of that job execution — the control plane's in-memory ring buffer is ephemeral.

**Backward compatibility:** Packages created before run-scoped logging may have log files directly under `.migration/Logs/` (for example `.migration/Logs/agent.jsonl`). The `LogDownloadController` falls back to this flat layout when no run-scoped folder is found.

### Naming Conventions

| Segment | Format | Example |
| --- | --- | --- |
| Date folder | `yyyy-MM-dd` | `2026-02-25` |
| Revision folder | `<ticks>-<workItemId>-<revisionIndex>` | `638760123456789012-12345-17` |

Folder names sort lexicographically in chronological order. This invariant enables streaming import without a global index and must be preserved.

## 3. Manifest (Package Metadata)

`manifest.json` at `PackageRoot/{org}/{project}/manifest.json`:

```json
{
  "packageVersion": "1.0",
  "toolVersion": "x.y.z",
  "runId": "...",
  "configHash": "...",
  "source": {
    "type": "AzureDevOpsServices | TeamFoundationServer",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "..."
  },
  "includedTypes": [
    "WorkItems",
    "Teams",
    "Identities",
    "Nodes",
    "Permissions",
    "Builds",
    "Git"
  ],
  "schemaVersions": {
    "WorkItems": "1.0",
    "Teams": "1.0"
  }
}
```

The manifest is **not required** for streaming import, but is **required** for:

- Validation
- Compatibility checks
- Upgrade safety
- Tooling
- Zip portability

### Versioning Rules

- `packageVersion` is incremented on breaking changes to the package layout.
- `schemaVersions` tracks per-module schema independently.
- An upgrader must be provided for each breaking schema change.
- Config versioning is handled separately; see `docs/configuration-reference.md`.

### Manifest Fields

| Field | Required | Description |
| --- | --- | --- |
| `packageVersion` | Yes | Package layout version |
| `toolVersion` | Yes | Version of the tool that produced the package |
| `runId` | Yes | Unique identifier for the export run |
| `configHash` | Yes | Hash of the config used to produce the package |
| `source.type` | Yes | `AzureDevOpsServices` or `TeamFoundationServer` |
| `source.orgOrCollection` | Yes | Organisation URL or TFS collection URL |
| `source.project` | Yes | Project name |
| `source.apiVersion` | Yes | REST API version used during export |
| `includedTypes` | Yes | Data type modules included in this package |
| `schemaVersions` | Yes | Per-module schema versions |





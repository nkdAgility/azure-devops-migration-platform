# Package Format

## 1. Package Root Resolution

`PackageRoot` is derived by appending the organisation folder name and project name to the configured `Package.WorkingDirectory`:

```
<WorkingDirectory>/<org-folder-name>/<project>/
```

For example, given:
- `WorkingDirectory`: `storage\my-export`
- Organisation URL: `https://dev.azure.com/contoso`
- Project: `MyProject`

The resulting `PackageRoot` is: `storage\my-export\contoso\MyProject\`

The org folder name is extracted from the organisation URL (the last path segment, e.g. `contoso` from `https://dev.azure.com/contoso`). For TFS collection URLs like `http://tfs:8080/tfs/DefaultCollection`, the folder name is `DefaultCollection`.

This resolution is performed by `PathUtilities.ExtractOrgFolderName()` and applied in the CLI commands (`QueueCommand`, `TfsExportCommand`) before the `IArtefactStore` is created. `WorkItems` and other module folders should never appear directly under `WorkingDirectory` — they always live under `<org>/<project>/`.

## 2. Package Structure (Canonical Format)

```
PackageRoot/
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
    Checkpoints/
      workitems.cursor.json
      identities.cursor.json  ← cursor for IdentitiesModule export/import resume
      nodes.cursor.json       ← cursor for NodesModule import resume
      teams.cursor.json       ← cursor for TeamsModule export/import resume
      prepare.complete.json ← marker written when Prepare completes successfully; Import checks for this
      idmap.db              ← source→target work item ID mappings
      export_progress.db    ← per-WI export revision index (fast-forward resume)
    Logs/
      <ticks>-<jobId>/
        progress.jsonl
        agent.jsonl
```

> **Legacy fallback:** Packages created before the `.migration/` dotfolder change may store `Checkpoints/` and `Logs/` directly under `PackageRoot/`. All code that reads these paths tries the `.migration/` location first, then falls back to the legacy root-level location. The `PackagePaths` static class in `DevOpsMigrationPlatform.Abstractions` defines both current and legacy path constants.

The WorkItems layout is canonical and must not be altered:

```
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

### .migration/Logs/

The `.migration/Logs/` folder contains structured observability records written by the Migration Agent during job execution. Each job writes to its own subfolder to prevent logs from different runs overwriting each other:

```
Logs/
  <ticks>-<jobId>/
    progress.jsonl
    agent.jsonl
    agent-001.jsonl   ← rotated segment (when max size exceeded)
```

The subfolder name uses `<ticks>-<jobId>` (e.g. `638807123456789012-a1b2c3d4`) so that folders sort chronologically and are traceable to the originating job.

| File | Format | Description |
|---|---|---|
| `progress.jsonl` | NDJSON | One `ProgressEvent` record per line. Tracks module cursor state, stage transitions, and item counts. Written by `PackageProgressSink`. |
| `agent.jsonl` | NDJSON | Structured diagnostic log records (ILogger output). Each line is a JSON object with `timestamp`, `level`, `category`, `message`, and optional `exception` fields. Written by `PackageDiagnosticSink`. |
| `agent-NNN.jsonl` | NDJSON | Rotated log segments when the primary segment exceeds the configured max size. |

Both files are append-only and survive resume. They are the durable record of job execution — the control plane's in-memory ring buffer is ephemeral.

**Backward compatibility:** Packages created before job-scoped logging may have log files directly under `.migration/Logs/` (e.g. `.migration/Logs/agent.jsonl`). The `LogDownloadController` falls back to this flat layout when no job-scoped subfolder is found.

### Naming Conventions

| Segment | Format | Example |
|---|---|---|
| Date folder | `yyyy-MM-dd` | `2026-02-25` |
| Revision folder | `<ticks>-<workItemId>-<revisionIndex>` | `638760123456789012-12345-17` |

Folder names sort lexicographically in chronological order. This invariant enables streaming import without a global index and must be preserved.

## 3. Manifest (Package Metadata)

`manifest.json` at `PackageRoot/manifest.json`:

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
- Config versioning is handled separately; see [docs/configuration.md](configuration.md).

### Manifest Fields

| Field | Required | Description |
|---|---|---|
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

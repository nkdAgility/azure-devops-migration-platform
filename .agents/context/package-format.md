# Package Format

## 2. Package Structure (Canonical Format)

```
PackageRoot/
  manifest.json
  WorkItems/
  Teams/
  Permissions/
  Builds/
  Git/
  Identities/
  Checkpoints/
  Logs/
```

The WorkItems layout is canonical and must not be altered:

```
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
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

### Logs/

The `Logs/` folder contains structured observability records written by the Migration Agent during job execution:

| File | Format | Description |
|---|---|---|
| `progress.jsonl` | NDJSON | One `ProgressEvent` record per line. Tracks module cursor state, stage transitions, and item counts. Written by `PackageProgressSink`. |
| `agent.jsonl` | NDJSON | Structured diagnostic log records (ILogger output). Each line is a JSON object with `timestamp`, `level`, `category`, `message`, and optional `exception` fields. Written by `PackageDiagnosticSink`. |

Both files are append-only and survive resume. They are the durable record of job execution — the control plane's in-memory ring buffer is ephemeral.

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

# Package Format Reference

Audience: Operators and contributors who need the exact package layout and file-scope semantics.

## 1. Package Root Resolution

`PackageRoot` is the configured package working directory. Project artefacts always live beneath organisation and project folders inside that root:

```text
<WorkingDirectory>/
  .migration/
  <org-folder-name>/<project>/
```

For example, given:

- `WorkingDirectory`: `storage\my-export`
- Organisation URL: `https://dev.azure.com/contoso`
- Project: `MyProject`

The resulting project subtree is `storage\my-export\contoso\MyProject\`.

For Azure DevOps Services, the org folder name is the last path segment of the organisation URL, for example `contoso` from `https://dev.azure.com/contoso`. For TFS collection URLs such as `http://tfs:8080/tfs/DefaultCollection`, the folder name is `DefaultCollection`.

## 2. Scope Semantics

The package has four state scopes:

- Root `.migration/` is authoritative package-scoped orchestration state shared across runs.
- `/{org}/.migration/` is authoritative organisation-scoped resume state.
- `/{org}/{project}/.migration/` is authoritative project-scoped resume state.
- `.migration/runs/<runId>/` is run-scoped audit output only.
- Cursor identity is action-qualified per module (`<action>.<module>.cursor.json`) to prevent cross-phase collisions.
- Read precedence for state lookups is project → org → package.
- Writes and resets target only the most-specific resolved scope for the active context.

Run-scoped `job.json`, `plan.json`, and `config.json` are copies of what was executed for that run. They are not the source of truth for later resume, phase-gate, or orchestration decisions.

## 3. Canonical Layout

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
          diagnostics-001.ndjson
  {org}/
    .migration/
      export.identities.cursor.json
  {org}/{project}/
    manifest.json
    WorkItems/
    Nodes/
    referenced-paths.json
    source-tree.json
    prepare-report.json
    Teams/
      {team-slug}/
        team.json
      prepare-report.json
    Permissions/
      prepare-report.json
    Builds/
    Git/
    Identities/
      descriptors.jsonl
      mapping.json
      unresolved.json
      prepare-report.json
    .migration/
      inventory.workitems.cursor.json
      export.workitems.cursor.json
      import.workitems.cursor.json
      export.identities.cursor.json
```

### Root `.migration/`

Root `.migration/` is the only valid location for package-wide state shared across runs, including:

- resolved package configuration
- execution plan
- phase completion markers
- package-wide checkpoint databases
- run audit folders

### Project `/{org}/{project}/.migration/`

Each project-local `.migration/` folder stores cursors for one organisation, project, action, and module combination. Filenames follow this shape:

```text
<action>.<module>.cursor.json
```

Examples:

- `inventory.workitems.cursor.json`
- `export.workitems.cursor.json`
- `import.workitems.cursor.json`
- `export.identities.cursor.json`

### Organisation `/{org}/.migration/`

Each organisation-local `.migration/` folder stores action/module cursor files for organisation-level workflows that are not tied to a specific project.

Examples:

- `export.identities.cursor.json`

### Run `.migration/runs/<runId>/`

Each run folder is an audit snapshot for one execution. It contains:

- `job.json` — audit copy of the leased job
- `plan.json` — audit copy of the plan executed for that run
- `config.json` — audit copy of the resolved configuration used by that run
- `logs/` — structured progress and diagnostic records for that run

Run folders use UTC timestamp format `<yyyyMMdd-HHmmss>` so they sort chronologically.

## 4. WorkItems Layout

The WorkItems layout is canonical and must preserve lexicographic chronological ordering:

```text
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      [comment.json]
      <attachment files>
      <embedded image files>
    <ticks>-<workItemId>-c<commentId>/
      comment.json
      <embedded image files>
```

Key characteristics:

- chronological ordering is guaranteed across revision and comment sub-folders
- no global index is required
- streaming import can enumerate in lexicographic order without resorting in memory
- attachments and embedded images stay beside the data that references them
- each folder is a natural resume position

## 5. Manifest

`manifest.json` is project-scoped package metadata.

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

The manifest is not required for streaming import, but it is required for validation, compatibility checks, upgrade safety, and portable tooling.

## 6. Legacy Fallback

Packages created before scoped metadata routing may store cursor files under root `.migration/Checkpoints/` or legacy `Checkpoints/`. Readers should try state locations in this order: project scope, org scope, package scope, then legacy root-level checkpoint files.

Packages created before run-scoped logging may have diagnostic files directly under `.migration/Logs/`. Tooling may fall back to that flat layout when no run-scoped folder is present.

## 7. Package Boundary Reference

The package layout and the package boundary are related but not identical concerns.

- This file owns the exact layout, file names, and scope semantics.
- [package-boundary-reference.md](package-boundary-reference.md) owns the contributor-facing description of `IPackageAccess`, routing ownership, and the relationship between the boundary and the underlying persistence stores.

## 8. Zip Packaging

Zip is a transport wrapper around the package directory layout. It does not redefine the package format.

### Pack

```text
PackageRoot/ -> migration-package.zip
```

- pack is applied after export completes or on demand via tooling
- the zip preserves the relative directory structure from `PackageRoot/` downward
- no content transformation occurs during packing

### Unpack

```text
migration-package.zip -> PackageRoot/
```

- unpack extracts to a specified directory before import begins
- if `artefacts.zip` is `true` in configuration, unpack is handled automatically by the runner before import
- partial extraction of root `.migration/`, a project `.migration/`, or `.migration/runs/<runId>/` is a valid tooling scenario

### Guarantees

- order preservation: unpacking preserves traversal order
- streaming import compatibility: unpacked packages behave the same as in-place packages
- determinism: identical export inputs produce structurally identical packages, excluding run-specific audit data and timestamped metadata

### Large Packages

- use zip64 for packages larger than standard zip limits
- pack and unpack tooling must enable zip64 when needed
- full extraction is not required by the core package design

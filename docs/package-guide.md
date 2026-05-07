# Package Guide

Audience: Operators.

## What Is the Migration Package?

The migration package is the intermediary between source and target. It is a directory tree that contains all exported data in a structured, human-readable format. The package is the source of truth for the migration.

## Why It Exists

- **Portability** — the package can be copied to a different machine to run import.
- **Resumability** — if a job is interrupted, resuming reads the cursor from the package and continues where it left off.
- **Auditability** — every exported item is inspectable before import.
- **Independence** — export and import can run at different times without both systems being available simultaneously.

## What Is In It

```
<WorkingDirectory>/
  .migration/
    migration-config.json       # resolved config written by agent at startup
    plan.json                   # package-level execution plan
    inventory.complete.json     # phase completion marker
    prepare.complete.json       # phase completion marker
  <org>/
    <project>/
      manifest.json             # project manifest (version, timestamps, modules run)
      .migration/
        export.workitems.cursor.json   # project-scoped cursor state
        import.workitems.cursor.json   # project-scoped cursor state
      Identities/
        mapping.json            # identity map (operator editable)
        prepare-report.json     # prepare phase report
      WorkItems/
        <date>/
          <ticks>-<id>-<rev>/
            revision.json       # work item revision data
            attachments/        # binary attachments for this revision
      Teams/
        teams.json
      Nodes/
        area-nodes.json
        iteration-nodes.json
```

## How to Inspect It

- `manifest.json` — check which modules ran and when.
- `Identities/prepare-report.json` — review identity mapping issues.
- `Identities/mapping.json` — edit to resolve unmapped identities.
- `WorkItems/<date>/<id>/revision.json` — inspect exported work item data.

## How Package Data Supports Resume

Each project-local `.migration/` folder holds cursor files for that org/project/action/module combination. Each cursor records the last successfully processed item for that project scope. Root `.migration/` holds package-level orchestration state and phase completion markers.

## How Package Zip/Export Works

The package can be compressed for transfer:

```
devopsmigration package zip --config migration.json --output migration-package.zip
```

The zip preserves the full directory structure. It can be unzipped on a different machine and used for import.

See [`package-format-reference.md`](package-format-reference.md) for the precise format specification.
# Package Guide

Audience: Operators.

This guide explains how to inspect and use the package operationally. For the exact layout and file-scope rules, see [`package-format-reference.md`](package-format-reference.md).

## What Is the Migration Package?

The migration package is the intermediary between source and target. It is a directory tree that contains all exported data in a structured, human-readable format. The package is the source of truth for the migration.

## Why It Exists

- **Portability** — the package can be copied to a different machine to run import.
- **Resumability** — if a job is interrupted, resuming reads the cursor from the package and continues where it left off.
- **Auditability** — every exported item is inspectable before import.
- **Independence** — export and import can run at different times without both systems being available simultaneously.

## What Is In It

```text
<WorkingDirectory>/
  .migration/
    migration-config.json       # resolved config written by agent at startup
    plan.json                   # package-level execution plan
    inventory.complete.json     # phase completion marker
    prepare.complete.json       # phase completion marker
    runs/
      20260506-161053/
        job.json                # audit copy of the leased job
        plan.json               # audit copy of the run plan
        config.json             # audit copy of the run config
        logs/
          progress.ndjson
          diagnostics.ndjson
  <org>/
    .migration/
      export.identities.cursor.json   # org-scoped cursor state
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
            <attachment files>  # binary attachments stored beside revision.json
      Teams/
        <team-slug>/
          team.json
      Nodes/
        source-tree.json
```

## How to Inspect It

- `manifest.json` — check which modules ran and when.
- `Identities/prepare-report.json` — review identity mapping issues.
- `Identities/mapping.json` — edit to resolve unmapped identities.
- `WorkItems/<date>/<ticks>-<workItemId>-<revisionIndex>/revision.json` — inspect exported work item data.

## How Package Data Supports Resume

Project-local `.migration/` folders hold cursor files for org/project/action/module combinations. Organisation-local `/{org}/.migration/` folders hold cursor files for org-scoped modules. Root `.migration/` holds package-scoped orchestration state and phase completion markers.

When reading resume metadata, the runtime checks project scope first, then org scope, then package scope. Writes and resets target only the most-specific resolved scope.

The `.migration/runs/<runId>/` folder is different: it is run-scoped audit data for one execution only. It keeps copies of the job, plan, and config that were executed, plus that run's logs. Later runs do not depend on those files to resume.

Action-qualified cursor naming (`inventory.<module>`, `export.<module>`, `import.<module>`) is required so inventory, export, and import state cannot overwrite each other for the same project.

## How Package Zip/Export Works

The package can be compressed for transfer:

```text
devopsmigration package zip --config migration.json --output migration-package.zip
```

The zip preserves the full directory structure. It can be unzipped on a different machine and used for import.

See [`package-format-reference.md`](package-format-reference.md) for the precise format specification.

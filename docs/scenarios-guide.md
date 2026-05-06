# Scenarios Guide

Audience: Operators.

Scenario files are pre-built configuration examples in the `scenarios/` folder. They represent common migration patterns and can be used as-is or customised.

## Available Scenarios

| File | Description |
|---|---|
| `queue-export-ado-workitems-single-project.json` | Export work items from a single ADO project |
| `queue-import-ado-workitems-single-project.json` | Import work items to a single ADO project |
| `queue-migrate-ado-workitems-single-project.json` | Full migrate (Export + Prepare + Import) for a single ADO project |

## How to Choose a Scenario

1. **Export only** — use an `export` scenario when you want to extract data from the source without importing yet.
2. **Import only** — use an `import` scenario when you have an existing package and want to push it to a target.
3. **Migrate** — use a `migrate` scenario for a full end-to-end run.
4. **TFS source** — select a scenario with `tfs` in the name; these are routed to the TFS Migration Agent automatically.

## How to Use a Scenario

1. Copy the scenario file to your working directory:
   ```
   Copy-Item scenarios\queue-export-ado-workitems-single-project.json migration.json
   ```

2. Edit the copied file to set your org URLs, project names, and token environment variable names.

3. Set your environment variables:
   ```powershell
   $env:SOURCE_PAT = "your-pat"
   $env:TARGET_PAT = "your-pat"
   ```

4. Run:
   ```
   devopsmigration queue --config migration.json --follow
   ```

## How to Modify a Scenario Safely

- Always copy the scenario — never edit the original in `scenarios/`.
- Change `Source.Url`, `Source.Project`, `Target.Url`, `Target.Project` to match your environment.
- Change `Package.WorkingDirectory` to a path with sufficient disk space.
- Do not change `ConfigVersion` unless you are explicitly upgrading the schema.
- Add or remove modules in the `Modules` section to control what is exported/imported.

## Single-Project vs Multi-Project

By default, scenarios target a single project. For multi-project migrations, run separate jobs per project, each with its own `WorkingDirectory`.

## Export-Only, Import-Only, Migrate

Set `Mode` in the config to control what the job does:

| Mode | Phases run |
|---|---|
| `Inventory` | Inventory only |
| `Export` | Inventory → Export |
| `Prepare` | Prepare |
| `Import` | Prepare → Import |
| `Migrate` | Inventory → Export → Prepare → Import → Validate |

See [`migration-process-guide.md`](migration-process-guide.md) for phase details.
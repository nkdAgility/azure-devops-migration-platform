# Quickstart: Simulated Data Source

**Branch**: `copilot/simulate-migration-data`

This guide shows how to run a full simulated migration — no TFS/Azure DevOps server required.

---

## Prerequisites

- .NET 10 SDK installed
- Repository cloned; build succeeds: `pwsh build.ps1`
- No external credentials needed

---

## 1. Run a Simulated Migration (25,000 items)

Use the provided ready-to-run scenario configuration:

```bash
devopsmigration migrate --config scenarios/migrate-simulated-25k.json
```

This runs:
1. **Export** — generates 25,000 work items deterministically and writes the package to `Logs/SimulatedRun-25k/`
2. **Validate** — validates the package structure and schema
3. **Import** — streams the package through `SimulatedWorkItemImportSink` (no external writes)

Expected output:
- `Logs/SimulatedRun-25k/WorkItems/` — revision folders in canonical layout
- `Logs/SimulatedRun-25k/Checkpoints/workitems.cursor.json` — completed cursor
- `Logs/SimulatedRun-25k/Logs/progress.jsonl` — progress events
- `Logs/SimulatedRun-25k/manifest.json` — includes `source.simulatedSeed` for reproducibility
- `Logs/SimulatedRun-25k/Logs/simulated-import-summary.jsonl` — import counts

---

## 2. Run from VS Code

Open the **Run and Debug** panel (`Ctrl+Shift+D`) and select:

> **🧪 Migrate: Simulated 25k**

This launches `devopsmigration migrate` with `scenarios/migrate-simulated-25k.json`. The integrated terminal shows live progress.

---

## 3. Run the TUI alongside a simulated migration

In one terminal:

```bash
devopsmigration migrate --config scenarios/migrate-simulated-25k.json
```

In a second terminal (while the migration is running):

```bash
devopsmigration tui
```

The TUI connects to the local control plane and shows live module progress, phase transitions, and item counts.

---

## 4. Reproduce a run exactly

Every simulated run records its seed in `manifest.json`. To reproduce an earlier run:

```bash
cat Logs/SimulatedRun-25k/manifest.json | grep simulatedSeed
# e.g. "simulatedSeed": 1872354901
```

Set the seed in your config:

```json
"source": {
  "type": "Simulated",
  "seed": 1872354901,
  "workItemCount": 25000
}
```

Two runs with the same `seed` and `workItemCount` produce byte-identical `revision.json` files.

---

## 5. Discovery inventory only

```bash
devopsmigration discovery inventory --config scenarios/migrate-simulated-25k.json
```

Produces `discovery-summary.csv` with the simulated project and work item count. No network calls.

---

## 6. Run the system test

```bash
dotnet test tests/DevOpsMigrationPlatform.SystemTests/ \
  --filter "TestCategory=SystemTest" \
  --logger "console;verbosity=normal"
```

The test runs a full `migrate` with 100 simulated items in-process and asserts:
- Package folder structure
- Cursor file existence
- `progress.jsonl` event count
- Validation report with zero errors

Expected duration: < 5 minutes in CI.

---

## 7. Customise the simulation

Edit or create a config file based on `scenarios/migrate-simulated-25k.json`:

```json
{
  "configVersion": "1.0",
  "mode": "Both",
  "artefacts": {
    "path": "${workspaceFolder}/Logs/MySimulatedRun"
  },
  "source": {
    "type": "Simulated",
    "seed": 999,
    "workItemCount": 500,
    "projectCount": 3,
    "workItemTypeDistribution": {
      "Bug": 60,
      "Task": 40
    },
    "avgRevisionsPerItem": 5,
    "includeAttachments": true,
    "attachmentSizeBytes": 1024,
    "includeLinks": true
  },
  "target": {
    "type": "Simulated",
    "validateOnWrite": true,
    "failOnFirstError": false
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [{ "type": "all", "parameters": {} }]
    }
  ]
}
```

Run with:

```bash
devopsmigration migrate --config my-simulated-config.json
```

---

## Scenario Config: `scenarios/migrate-simulated-25k.json`

```json
{
  "configVersion": "1.0",
  "mode": "Both",
  "artefacts": {
    "path": "${workspaceFolder}/Logs/SimulatedRun-25k"
  },
  "source": {
    "type": "Simulated",
    "workItemCount": 25000,
    "avgRevisionsPerItem": 3,
    "includeLinks": true,
    "includeAttachments": false
  },
  "target": {
    "type": "Simulated",
    "validateOnWrite": true,
    "failOnFirstError": true
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [{ "type": "all", "parameters": {} }]
    }
  ]
}
```

> **Note**: `seed` is omitted to exercise the auto-seed path. The chosen seed is logged at startup and recorded in `manifest.json`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| "source.workItemCount is required" | Config missing `workItemCount` | Add `"workItemCount": 25000` to `source` block |
| Export resumes from checkpoint but wrong data | `seed` or `workItemCount` changed between runs | Delete the package folder and start fresh, or set `--force` (if available) |
| TUI shows no progress | Migration already completed | Re-run `devopsmigration migrate`; TUI connects to live jobs only |
| System test fails with build error | New project not built | Run `pwsh build.ps1` before the test |

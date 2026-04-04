# Quickstart: Inventory Command

**Feature Branch**: `003-inventory-command`  
**Phase**: 1 — Design & Contracts

This guide covers how to configure, run, and test the inventory command. It is aimed at platform engineers implementing the feature and operators using it.

---

## Prerequisites

- .NET 10 SDK installed
- `devopsmigration` CLI built from this branch (`dotnet build src/DevOpsMigrationPlatform.CLI.Migration`)
- Azure DevOps PAT with **Read** permissions on the target organisation(s)
- (TFS only) `tfsmigration.exe` (.NET 4.8 subprocess) available at a known path

---

## 1. Create a Config File

### Minimal inventory-only config

```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/my-org",
        "token": "$ENV:ADO_PAT"
      }
    ]
  }
}
```

Save as `migration.json` in your working directory (or any path — pass it with `--config`).

### Multi-source config

```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/org-a",
        "token": "$ENV:ORG_A_PAT"
      },
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/org-b",
        "token": "$ENV:ORG_B_PAT"
      }
    ]
  }
}
```

### TFS source

```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "TeamFoundationServer",
        "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
        "token": "$ENV:TFS_PAT"
      }
    ]
  }
}
```

### Single-project filter (in config)

```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/my-org",
        "project": "MyProject",
        "token": "$ENV:ADO_PAT"
      }
    ]
  }
}
```

---

## 2. Set Environment Variables

Never put PATs in plain text in config files. Use `$ENV:VARNAME` references:

```bash
# Bash / sh
export ADO_PAT="your-personal-access-token"

# PowerShell
$env:ADO_PAT = "your-personal-access-token"

# Windows cmd
set ADO_PAT=your-personal-access-token
```

---

## 3. Run the Command

### Basic run (uses `migration.json` in current directory)

```
migrate discovery inventory
```

### Explicit config file path

```
migrate discovery inventory --config /path/to/migration.json
```

### Override project for all sources

```
migrate discovery inventory --project MyProject
```

This overrides any `project` value in the config sources and restricts all sources to `MyProject` only.

### Save output to CSV

```
migrate discovery inventory --out summary.csv
```

### Full example

```
migrate discovery inventory \
  --config migration.json \
  --project MyProject \
  --out results.csv
```

---

## 4. Expected Terminal Output

```
 ______          ___
|  _ \ \        / (_)   
| | | |\ \  / / _  
| |_| | \ \/ / / _ \  
|____/   \__/ /_/ \_\

─────────────────────────────────────────────────────────────

[https://dev.azure.com/my-org]

 ╭─────────────────────────────────────────────╮
 │           Inventory Progress                │
 ├─────────────────────────┬───────────────────┤
 │ Project                 │        Work Items │
 ├─────────────────────────┼───────────────────┤
 │ Alpha                   │            12 450 │
 │ Beta                    │               …   │
 │ Gamma                   │            87 321 │
 ╰─────────────────────────┴───────────────────╯

✅ Discovery complete.
Saved to results.csv
```

Live table updates as each project is counted. Projects in-progress show `…`.

---

## 5. CSV Output Format

When `--out` is specified the CSV has one row per project, across all sources:

```csv
Source,Project,WorkItems
https://dev.azure.com/my-org,Alpha,12450
https://dev.azure.com/my-org,Beta,500
https://dev.azure.com/my-org,Gamma,87321
```

---

## 6. Error Scenarios

### Missing environment variable

```
Error: Environment variable 'ADO_PAT' referenced in config token is not set.
```
**Fix**: Set `ADO_PAT` in the current shell session.

### Missing `inventory` section

```
Error: The 'inventory' section is missing from the config file. 
Expected: migration.json must contain an "inventory" key with a "sources" array.
```
**Fix**: Add the `inventory` section (see examples above).

### Wrong config version

```
Error: ConfigVersion '1.0' is not supported for inventory features. 
Please update configVersion to "2.0" in your migration.json.
```
**Fix**: Change `"configVersion": "1.0"` to `"configVersion": "2.0"`.

### Authentication failure

```
[https://dev.azure.com/my-org] Error: 401 Unauthorized — check your PAT has Read access.
Exit code: 1
```
**Fix**: Verify the PAT has the `vso.project` and `vso.work_read` scopes.

### Project not found

```
[https://dev.azure.com/my-org] Warning: Project 'NonExistentProject' not found. Count: 0.
Exit code: 1
```

### Partial pagination failure

```
[https://dev.azure.com/my-org] Warning: API error fetching page 3 of 4 for 'LargeProject'.
Partial count: 40000 (may be incomplete).
Exit code: 1
```

---

## 7. Developer Setup (Local Build & Test)

### Build

```bash
cd /path/to/repo
dotnet build src/DevOpsMigrationPlatform.CLI.Migration
```

### Run unit tests

```bash
dotnet test tests/ --filter "Category!=Integration"
```

### Run acceptance tests (Reqnroll/Gherkin)

```bash
dotnet test tests/ --filter "Feature=InventoryCommand"
```

### Run with a real Azure DevOps org (integration)

```bash
export ADO_PAT="..."
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration -- \
  discovery inventory \
  --config specs/003-inventory-command/sample-migration.json
```

---

## 8. Key Types Quick Reference

| Type | Project | Purpose |
|---|---|---|
| `InventoryOptions` | `Abstractions` | Sealed options bound from `inventory` section |
| `InventorySourceOptions` | `Abstractions` | Per-source connection entry |
| `ITokenResolver` | `Abstractions` | `$ENV:VARNAME` resolution contract |
| `TokenResolver` | `Infrastructure` | `ITokenResolver` implementation |
| `ICatalogService` | `Abstractions` | Project listing + paginated work item counting |
| `CatalogService` | `Infrastructure.AzureDevOps` | Azure DevOps REST implementation |
| `InventorySourceResult` | `Abstractions` | Aggregate result for one source |
| `ProjectDiscoverySummary` | `Abstractions` | Per-project count snapshot (existing) |
| `TfsInventoryRequest` | `Abstractions` | stdin DTO for TFS subprocess |
| `ExternalToolRunner` | `CLI.Migration` | Generic subprocess bridge |
| `InventoryCommand` | `CLI.Migration` | Spectre.Console command orchestrator |
| `V2ConfigUpgrader` | `Infrastructure` | `1.0 → 2.0` no-op upgrader |

---

## 9. Config Migration (1.0 → 2.0)

If you have an existing `migration.json` at version `1.0` used for export/import:

**Before** (`1.0` — existing export/import config, unchanged):
```json
{
  "configVersion": "1.0",
  "mode": "Export",
  "artefacts": { "path": "D:\\exports\\run-001" },
  "source": { "type": "AzureDevOpsServices", "orgOrCollection": "..." },
  "target": { "type": "AzureDevOpsServices", "orgOrCollection": "..." },
  "modules": []
}
```

**After** (same config, version bumped + `inventory` added — both export and inventory now work):
```json
{
  "configVersion": "2.0",
  "mode": "Export",
  "artefacts": { "path": "D:\\exports\\run-001" },
  "source": { "type": "AzureDevOpsServices", "orgOrCollection": "..." },
  "target": { "type": "AzureDevOpsServices", "orgOrCollection": "..." },
  "modules": [],
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "...",
        "token": "$ENV:ADO_PAT"
      }
    ]
  }
}
```

**Only the `configVersion` bump is required** to keep the export/import commands working. The `inventory` section is only required when running `discovery inventory`. Other commands (`export`, `import`, `both`, `validate`, etc.) continue working with v2.0 configs that have no `inventory` section.

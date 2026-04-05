# Quickstart: Work Items Inventory Command

**How to run the inventory command once implemented.**

---

## Single-project inventory (Mode 1 — migration config)

**1. Create a config file `migration.json`:**

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": "Alpha",
    "apiVersion": "7.1",
    "authentication": {
      "type": "Pat",
      "accessToken": "$ENV:MIGRATION_SOURCE_PAT"
    }
  }
}
```

**2. Set the PAT environment variable:**

```powershell
$env:MIGRATION_SOURCE_PAT = "your-pat-here"
```

**3. Run:**

```
devopsmigration discovery inventory --config migration.json
```

The terminal renders a live-updating table. When complete, `discovery-summary.csv` is written to the current working directory.

To write the CSV to a specific directory:

```
devopsmigration discovery inventory --config migration.json --output C:\reports
```

---

## All-projects inventory (Mode 1 — `--all-projects` flag)

Leave `project` null in the config:

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": null,
    "apiVersion": "7.1",
    "authentication": {
      "type": "Pat",
      "accessToken": "$ENV:MIGRATION_SOURCE_PAT"
    }
  }
}
```

```
devopsmigration discovery inventory --config migration.json --all-projects
```

---

## Multi-org tooling roster (Mode 2)

**1. Create `roster.json`:**

```json
{
  "configVersion": "1.0",
  "organisations": [
    {
      "type": "AzureDevOpsServices",
      "orgOrCollection": "https://dev.azure.com/org-a",
      "projects": ["Alpha", "Beta"],
      "apiVersion": "7.1",
      "authentication": { "type": "Pat", "accessToken": "$ENV:ORG_A_PAT" }
    },
    {
      "type": "AzureDevOpsServices",
      "orgOrCollection": "https://dev.azure.com/org-b",
      "projects": [],
      "apiVersion": "7.1",
      "authentication": { "type": "Pat", "accessToken": "$ENV:SHARED_PAT" }
    },
    {
      "enabled": false,
      "type": "TeamFoundationServer",
      "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
      "projects": [],
      "apiVersion": "15.0",
      "authentication": { "type": "Windows" }
    }
  ]
}
```

**2. Set PATs:**

```powershell
$env:ORG_A_PAT    = "pat-for-org-a"
$env:SHARED_PAT   = "shared-pat"
```

**3. Run:**

```
devopsmigration discovery inventory --config roster.json
```

The TFS entry with `"enabled": false` is silently skipped. All projects in org-b are inventoried (empty `projects` array). Only Alpha and Beta from org-a are inventoried.

---

## TFS on-premises (Mode 1 — Windows auth)

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "TeamFoundationServer",
    "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
    "project": "MyProject",
    "apiVersion": "15.0",
    "authentication": { "type": "Windows" }
  }
}
```

```
devopsmigration discovery inventory --config tfs-migration.json
```

The CLI spawns the TFS subprocess automatically; no additional flags are required.

---

## Output

**Terminal (live):**

```
╭─────────────────────────────────────────────────────────────────╮
│                     Inventory Progress                          │
├─────────────┬────────────┬───────────┬───────┬───────────┬──────┤
│ Project     │ Work Items │ Revisions │ Repos │ Pipelines │ ...  │
├─────────────┼────────────┼───────────┼───────┼───────────┼──────┤
│ Alpha       │ 12,450     │ 38,200    │ 0     │ 0         │ ...  │
│ Beta        │ 3,100      │ 7,800     │ 0     │ 0         │ ...  │
╰─────────────┴────────────┴───────────┴───────┴───────────┴──────╯
✅ Discovery complete. Saved to discovery-summary.csv
```

**`discovery-summary.csv`:**

```
OrgOrCollection,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc
https://dev.azure.com/myorg,Alpha,12450,38200,0,0,True,,2026-04-04T10:23:11Z
https://dev.azure.com/myorg,Beta,3100,7800,0,0,True,,2026-04-04T10:24:05Z
```

A project that failed mid-count:

```
OrgOrCollection,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc
https://dev.azure.com/myorg,Gamma,800,0,0,0,False,Authentication failure: PAT expired,2026-04-04T10:24:12Z
```

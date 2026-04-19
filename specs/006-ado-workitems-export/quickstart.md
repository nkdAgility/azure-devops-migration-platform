# Quickstart: Work Items Export — Azure DevOps via REST API

**Feature**: `006-ado-workitems-export`  
**Prerequisite**: The platform is built and configured. See [docs/cli.md](../../docs/cli.md) and [docs/configuration.md](../../docs/configuration.md).

---

## Overview

This feature enables exporting all revisions of all work items from an Azure DevOps project to a local or cloud package. The export:

- Tracks every revision of every work item (fields, links, attachments)
- Stores attachments beside their revision — the package is self-contained
- Resumes automatically from the last cursor position if interrupted
- Reports live progress to the TUI and control plane

---

## Step 1 — Create a migration configuration file

Create `migration.json` in your working directory:

```json
{
  "source": {
    "type": "AzureDevOpsServices",
    "url": "https://dev.azure.com/my-organisation",
    "project": "MyProject",
    "authentication": {
      "type": "PersonalAccessToken",
      "token": "$(ADO_PAT)"
    }
  },
  "target": {
    "type": "AzureDevOpsServices",
    "url": "https://dev.azure.com/target-organisation",
    "project": "TargetProject",
    "authentication": {
      "type": "PersonalAccessToken",
      "token": "$(ADO_TARGET_PAT)"
    }
  },
  "artefacts": {
    "packagePath": "C:/migration-packages/my-project"
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [
        {
          "type": "wiql",
          "parameters": {
            "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
            "includeAttachments": true
          }
        }
      ]
    }
  ]
}
```

**Environment variable substitution**: Set `ADO_PAT` and `ADO_TARGET_PAT` in your shell — the platform substitutes `$(VAR_NAME)` from the environment before building the job contract.

---

## Step 2 — Run the export

```shell
devopsmigration export --config migration.json
```

The CLI:
1. Validates `migration.json` (schema + credential presence)
2. Starts the control plane and agent (local mode via Aspire)
3. Submits the export job
4. Streams progress to the terminal

**Expected output:**

```
[WorkItems] Exporting work items from MyProject...
[WorkItems] Work item 1  | rev 0 of 3  | attachments: 0
[WorkItems] Work item 1  | rev 1 of 3  | attachments: 1
[WorkItems] Work item 1  | rev 2 of 3  | attachments: 0
...
[WorkItems] ✓ Completed — 10,342 revisions, 423 attachments (0 failed)
```

---

## Step 3 — Inspect the package

```
C:/migration-packages/my-project/
├── WorkItems/
│   ├── 2024-01-15/
│   │   ├── 00638412345678901234-1-0/
│   │   │   ├── revision.json
│   │   │   └── f1a2b3c4-0000-0000-0000-screenshot.png
│   │   └── 00638412345678901234-1-1/
│   │       └── revision.json
│   └── 2024-02-20/
│       └── 00638512345678901234-42-0/
│           └── revision.json
├── Checkpoints/
│   └── workitems.cursor.json
└── manifest.json
```

---

## Resuming a failed export

If the export is interrupted (network failure, process kill), simply run the same command again:

```shell
devopsmigration export --config migration.json
```

The agent reads `Checkpoints/workitems.cursor.json` and skips all revision folders already marked `Completed`. Only unprocessed revisions are fetched and written.

---

## Limiting scope with a WIQL filter

To export only `Bug` work items:

```json
"parameters": {
  "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] = 'Bug' ORDER BY [System.Id]"
}
```

To export a specific area path:

```json
"parameters": {
  "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.AreaPath] UNDER 'MyProject\\\\Backend' ORDER BY [System.Id]"
}
```

---

## Skipping attachment downloads

Set `includeAttachments: false` to record attachment metadata without downloading binaries:

```json
"parameters": {
  "includeAttachments": false
}
```

Attachment metadata (`originalName`, `sha256`, `size`) is still written to `revision.json` but no binary files are created. This is useful for fast inventory passes or when binaries are not needed.

---

## Configuration reference

| Parameter | Type | Default | Description |
|---|---|---|---|
| `query` | `string` | `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]` | WIQL filter for work item IDs |
| `includeAttachments` | `bool` | `true` | Download attachment binaries |

Full schema: [contracts/work-items-scope-parameters.schema.json](contracts/work-items-scope-parameters.schema.json)

---

## Troubleshooting

| Symptom | Likely cause | Resolution |
|---|---|---|
| `401 Unauthorized` immediately | PAT expired or missing `Work Items (Read)` scope | Regenerate PAT with correct scopes |
| `WIQL query returned more than 20000 items` | Area/iteration path too broad | Narrow WIQL `AreaPath UNDER` clause or rely on automatic date-window fallback |
| Attachment download retrying repeatedly | Transient network issue | Allow retries (default 8); if persistent, check network connectivity |
| Resuming export re-downloads every revision | Cursor file missing or deleted | Do not delete `Checkpoints/workitems.cursor.json` during an export run |

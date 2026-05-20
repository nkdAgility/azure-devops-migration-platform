# Quickstart — Azure DevOps Work Items Import

**Feature**: 013-ado-workitems-import  
**Date**: 2026-04-15

---

## Overview

This guide covers how to run a work items import from an exported package to a target Azure DevOps project.

## Prerequisites

1. An exported package at a known path (e.g. `D:\exports\run-001`) containing `WorkItems/` with revision folders.
2. A target Azure DevOps Services project with:
   - Work item types matching those in the package (Bug, Task, User Story, etc.)
   - Area and iteration paths matching those referenced by work items
   - A PAT with **Work Items (Read, Write)** and **Project and Team (Read)** permissions
3. The `devopsmigration` CLI built and available.

## Configuration

Create a config file (e.g. `import.json`):

```json
{
  "configVersion": "1.0",
  "mode": "Import",
  "artefacts": {
    "path": "D:\\exports\\run-001"
  },
  "target": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": "MyTargetProject",
    "apiVersion": "7.1",
    "authentication": {
      "Type": "AccessToken",
      "accessToken": "$ENV:TARGET_PAT"
    }
  },
  "modules": [
    {
      "name": "WorkItems",
      "extensions": [
        { "type": "Revisions",      "enabled": true },
        { "type": "Links",          "enabled": true },
        { "type": "Attachments",    "enabled": true },
        { "type": "Comments",       "enabled": true },
        { "type": "EmbeddedImages", "enabled": true }
      ]
    }
  ]
}
```

## Running the Import

```bash
# Set the PAT
export TARGET_PAT="your-pat-here"   # Linux/macOS
$env:TARGET_PAT = "your-pat-here"   # PowerShell

# Run the import
devopsmigration queue --config import.json
```

## Resume After Interruption

Simply re-run the same command. The import reads `Checkpoints/workitems.cursor.json` and skips completed work.

```bash
devopsmigration queue --config import.json
```

## Force Fresh Import

To restart from scratch (preserves the ID map to avoid duplicates):

```bash
devopsmigration queue --config import.json --force-fresh
```

## Work Item Resolution Strategies

For repeat/incremental imports, add a `WorkItemResolutionStrategy` extension to discover existing target work items:

### TargetField Strategy
Requires a custom field on the target project (e.g. `Custom.MigratedSourceId`):

```json
{
  "type": "WorkItemResolutionStrategy",
  "enabled": true,
  "parameters": {
    "strategy": "TargetField",
    "fieldName": "Custom.MigratedSourceId"
  }
}
```

### TargetHyperlink Strategy
Uses hyperlinks with a configured URL pattern:

```json
{
  "type": "WorkItemResolutionStrategy",
  "enabled": true,
  "parameters": {
    "strategy": "TargetHyperlink",
    "urlPattern": "migration://source/{org}/{project}/{workItemId}"
  }
}
```

## What to Expect

- Work items appear in the target in chronological order
- Revision history is replayed (each revision creates a target revision)
- Links, attachments, and comments are imported alongside their revisions
- Unresolved identities are logged to `Identities/unresolved.json` (import continues)
- Progress is reported live in the terminal and written to `Logs/progress.jsonl`

## Troubleshooting

| Issue | Cause | Resolution |
|-------|-------|------------|
| `401 Unauthorized` | PAT missing or expired | Check `$ENV:TARGET_PAT` is set correctly |
| `404 Not Found` | Project does not exist | Verify `target.project` name |
| `VS403XXX: Work item type not found` | Missing work item type in target | Create the work item type in the target process template |
| Import is slow | Rate limiting | Check `policies.retries.max` and `policies.throttle.maxConcurrency` |
| Duplicate work items | `idmap.db` was deleted | Use `--force-fresh` (preserves idmap) or do not delete `Checkpoints/idmap.db` |

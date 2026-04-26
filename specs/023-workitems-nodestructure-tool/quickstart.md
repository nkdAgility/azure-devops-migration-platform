# Quickstart — NodeStructure Tool

**Feature**: 023-workitems-nodestructure-tool

---

## Minimal Configuration

Add a `NodeStructure` entry under `MigrationPlatform.Tools` in your migration config:

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "AutoCreateNodes": true
      }
    }
  }
}
```

With this minimal config:
- Paths that start with the source project name are **auto-swapped** to the target project name.
- Missing nodes are created automatically in the target.
- No explicit path mapping is needed for same-structure migrations.

## Cross-Project Migration with Path Remapping

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "AutoCreateNodes": true,
        "AreaPathMappings": {
          "OldProject\\Team Alpha": "NewProject\\Engineering\\Team Alpha",
          "OldProject\\Team Beta": "NewProject\\Engineering\\Team Beta"
        },
        "IterationPathMappings": {
          "OldProject\\Sprint 1": "NewProject\\Q1\\Sprint 1",
          "OldProject\\Sprint 2": "NewProject\\Q1\\Sprint 2"
        }
      }
    }
  }
}
```

## Full Tree Replication

To copy the entire classification tree from source to target:

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "AutoCreateNodes": true,
        "ReplicateSourceTree": true
      }
    }
  }
}
```

Run **export** first (captures `Nodes/source-tree.json` and `Nodes/referenced-paths.json`), then **import** (replicates nodes before writing work items).

> **Note**: The export always writes `Nodes/source-tree.json` (full classification tree) and `Nodes/referenced-paths.json` (paths found in revisions), regardless of configuration flags. The `ReplicateSourceTree` flag controls whether the import replicates the full tree.

## Graceful Handling of Bad Paths

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "AutoCreateNodes": false,
        "SkipOnUnresolvableArea": true,
        "SkipOnUnresolvableIteration": true
      }
    }
  }
}
```

Revisions with unresolvable paths are skipped with warnings; the import continues.

## Cross-Locale Migration

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "CreateMissingNodes": true,
        "AreaLanguageOverride": "Area",
        "IterationLanguageOverride": "Iteration"
      }
    }
  }
}
```

Normalises localised root segments (e.g., `"Área"` → `"Area"`) before mapping lookup.

## Validation

Run `migrate validate` before import to discover unmapped paths:

```
> migrate validate --config my-config.json

NodeStructure Validation Report:
  Unmapped area paths:
    "OldProject\Archived\Team C" — 47 revisions affected
  Unmapped iteration paths:
    "OldProject\Legacy Sprint" — 12 revisions affected
  Unanchored paths:
    "ThirdPartyProject\External" — 3 revisions (not in source project)
```

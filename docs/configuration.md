# Configuration

## 10. Configuration Model

A single JSON configuration file drives the entire run.

### Full Schema

```json
{
  "configVersion": "1.0",
  "mode": "Export | Import | Both",
  "artefacts": {
    "path": "D:\\exports\\run-001",
    "zip": false
  },
  "source": {
    "type": "AzureDevOpsServices | TeamFoundationServer",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "...",
    "authentication": {
      "type": "Pat | Windows",
      "accessToken": "<literal-token> | $ENV:MY_PAT_VAR"
    }
  },
  "target": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "...",
    "authentication": {
      "type": "Pat",
      "accessToken": "$ENV:TARGET_PAT"
    }
  },
  "organisations": [
    {
      "type": "AzureDevOpsServices | TeamFoundationServer",
      "url": "https://dev.azure.com/myorg",
      "projects": ["Alpha", "Beta"],
      "apiVersion": "7.1",
      "authentication": {
        "type": "Pat",
        "accessToken": "$ENV:ORG_PAT"
      },
      "enabled": true
    }
  ],
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [
        {
          "type": "wiql",
          "parameters": {
            "query": "SELECT [System.Id] FROM WorkItems WHERE ...",
            "includeRevisions": true,
            "includeLinks": true,
            "includeAttachments": true
          }
        }
      ]
    }
  ],
  "policies": {
    "retries": { "max": 8 },
    "throttle": { "maxConcurrency": 4 }
  }
}
```

> **Mode 1 vs Mode 2 (inventory-only)**:
> - **Mode 1** — Use a `source` block. Exactly one org/collection is targeted. Mutual exclusion: `source` and `organisations` cannot both be set.
> - **Mode 2** — Use an `organisations` array for multi-org inventory. Each entry may have its own auth, project filter, and `enabled` flag.
>
> **Token resolution order** (for `accessToken` and similar fields):
> 1. If value starts with `$ENV:VARNAME` — reads environment variable `VARNAME` (throws if unset or empty).
> 2. If value is a non-empty literal — used as-is.
> 3. If value is null or empty — no auth token applied (Windows-integrated auth).

### Top-Level Fields

| Field | Required | Description |
|---|---|---|
| `configVersion` | Yes | Config schema version; used by the upgrader |
| `mode` | Yes | `Export`, `Import`, or `Both` |
| `artefacts.path` | Yes | Absolute path to the package root directory |
| `artefacts.zip` | No | If `true`, pack/unpack around the run; default `false` |
| `source` | Required for `Export` and `Both`; Mode 1 inventory | Source system connection details |
| `source.authentication` | No | Auth credentials block (`type` + `accessToken`). If omitted, Windows-integrated auth is used. |
| `target` | Required for `Import` and `Both` | Target system connection details |
| `target.authentication` | No | Auth credentials block (`type` + `accessToken`). |
| `organisations` | Mode 2 inventory only | Multi-org tooling roster. Mutually exclusive with `source`. Each entry has `type`, `url`, `projects`, `authentication`, and `enabled`. |
| `modules` | Yes | Ordered list of modules to run with their scope configurations |
| `policies` | No | Retry and throttle policies |

### Module Scopes Pattern

Each module declares its own scope schema. The orchestrator passes the raw scope configuration to the module; validation of scope parameters is the module's responsibility (in `ValidateAsync`).

Common scope types:

| Type | Description |
|---|---|
| `wiql` | WIQL query selecting work items to export |
| `all` | Export everything of this type |
| `include` | Explicit list of items to include |

### Policies

| Policy | Field | Default | Description |
|---|---|---|---|
| Retries | `policies.retries.max` | `3` | Maximum retry attempts for transient failures |
| Concurrency | `policies.throttle.maxConcurrency` | `2` | Maximum parallel API requests |

### Config Versioning and Upgrader

- `configVersion` must be incremented on any breaking change to the config schema.
- An upgrader must exist for each version transition (e.g., `1.0 → 2.0`).
- The tool must detect an outdated config version and either auto-upgrade (with warning) or fail fast with instructions.
- Configs from future versions must fail fast with a clear error message.

---

## Scenario Configs

Ready-to-run example configuration files live under `/scenarios/` at the repository root. Each file targets a specific connectivity scenario and is wired to a VS Code launch configuration for quick local debugging.

| File | Scenario |
|---|---|
| `inventory-ado-single-project.json` | Single Azure DevOps project inventory (PAT auth) |
| `inventory-ado-multi-project.json` | Multi-project Azure DevOps inventory (PAT auth) |
| `inventory-tfs-windows-auth.json` | On-premises TFS inventory (Windows-integrated auth) |
| `inventory-multi-org.json` | Multi-organisation inventory with per-org PAT tokens |

Credentials in these files use `$ENV:VARNAME` references — never literal tokens. Set the corresponding environment variables locally (e.g. `AZDEVOPS_DEV_PAT`) before running.

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
    "apiVersion": "..."
  },
  "target": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "..."
  },
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

### Top-Level Fields

| Field | Required | Description |
|---|---|---|
| `configVersion` | Yes | Config schema version; used by the upgrader |
| `mode` | Yes | `Export`, `Import`, or `Both` |
| `artefacts.path` | Yes | Absolute path to the package root directory |
| `artefacts.zip` | No | If `true`, pack/unpack around the run; default `false` |
| `source` | Required for `Export` and `Both` | Source system connection details |
| `target` | Required for `Import` and `Both` | Target system connection details |
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

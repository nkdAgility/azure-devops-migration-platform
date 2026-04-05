# Contracts: Work Items Inventory Command

**Phase 1 output for feature 003-inventory-workitems**

The inventory command has two external contracts: the **config file schema** (operator-facing) and the **TFS subprocess NDJSON protocol** (process-bridge).

---

## Contract 1 — Config File Schema

Version: `1.0` (same `configVersion` field as migration config)

### Mode 1 — `source`-based (migration config reuse)

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "AzureDevOpsServices | TeamFoundationServer",
    "orgOrCollection": "<url>",
    "project": "<name> | null",
    "apiVersion": "<version>",
    "authentication": {
      "type": "Pat | Windows",
      "accessToken": "<literal> | $ENV:<VARNAME>"
    }
  }
}
```

CLI flags (in addition to `--config`):

| Flag | Required | Default | Notes |
|---|---|---|---|
| `--output <path>` | No | Current working directory | Directory where `discovery-summary.csv` is written |
| `--all-projects` | No | `false` | Required in Mode 1 when `source.project` is null |

Fields:

| Path | Required | Notes |
|---|---|---|
| `configVersion` | Yes | Must be `"1.0"` |
| `source.type` | Yes | `"AzureDevOpsServices"` or `"TeamFoundationServer"` |
| `source.orgOrCollection` | Yes | Organisation or collection URL |
| `source.project` | No | If null/absent, `--all-projects` CLI flag is required |
| `source.apiVersion` | Yes | Pinned API version string |
| `source.authentication.type` | Yes | `"Pat"` or `"Windows"` |
| `source.authentication.accessToken` | Yes for Pat | Literal or `$ENV:VARNAME` |

IConfiguration `__`-path env var override applies to all `source.*` fields (standard .NET layering).

### Mode 2 — `organisations`-based (tooling roster)

```json
{
  "configVersion": "1.0",
  "organisations": [
    {
      "enabled": true,
      "type": "AzureDevOpsServices | TeamFoundationServer",
      "orgOrCollection": "<url>",
      "projects": ["<name>"],
      "apiVersion": "<version>",
      "authentication": {
        "type": "Pat | Windows",
        "accessToken": "<literal> | $ENV:<VARNAME>"
      }
    }
  ]
}
```

Fields per entry:

| Path | Required | Default | Notes |
|---|---|---|---|
| `enabled` | No | `true` | `false` = skip silently |
| `type` | Yes | — | `"AzureDevOpsServices"` or `"TeamFoundationServer"` |
| `orgOrCollection` | Yes | — | URL |
| `projects` | No | `[]` | Empty/absent = all projects in org |
| `apiVersion` | Yes | — | Pinned version |
| `authentication.type` | Yes | — | `"Pat"` or `"Windows"` |
| `authentication.accessToken` | Yes for Pat | — | Literal or `$ENV:VARNAME` |

IConfiguration `__`-path overrides do **not** reach `organisations[n]` entries. Use `$ENV:VARNAME` for per-entry env refs.

### Validation errors (both modes)

| Condition | Error message |
|---|---|
| Both `source` and `organisations` set | `"Config error: 'source' and 'organisations' are mutually exclusive. Use 'source' for a single migration target, or 'organisations' for a multi-org roster."` |
| Neither set | `"Config error: Config must contain either a 'source' block (Mode 1) or an 'organisations' array (Mode 2)."` |
| Mode 1, project null, no `--all-projects` | `"Config error: 'source.project' is not set. Specify a project in the config or pass --all-projects to inventory the whole organisation."` |
| Mode 2, array empty | `"Config error: 'organisations' array is empty."` |
| Pat auth, resolved token empty | `"Config error: PAT for '{orgOrCollection}' resolved to an empty string. Set 'authentication.accessToken' to a literal value or '$ENV:VARNAME'."` |

### CSV output schema (`discovery-summary.csv`)

One row per project. All projects are written, including those that failed mid-count.

| Column | Type | Notes |
|---|---|---|
| `OrgOrCollection` | string | Source org or collection URL |
| `ProjectName` | string | Project name |
| `WorkItemsCount` | int | Total work items counted (partial on failure) |
| `RevisionsCount` | int | Sum of all `System.Rev` values (partial on failure) |
| `ReposCount` | int | Always 0 in this feature |
| `PipelinesCount` | int | Always 0 in this feature |
| `IsComplete` | bool | `True` if all windows scanned without error |
| `Error` | string | Empty on success; error message on failure |
| `LastUpdatedUtc` | ISO 8601 | UTC timestamp of last count update |

---

## Contract 2 — TFS Subprocess NDJSON Protocol (inventory subcommand)

The .NET 10 host spawns the TFS subprocess as:

```
tfsmigration.exe inventory --collection <url> [--project <name>] [--all-projects]
```

Credentials are passed via **stdin** as UTF-8 JSON (same pattern as export):

```json
{ "pat": "<resolved-token>" }
```

For Windows auth, stdin is `{}` (empty object; the subprocess uses the current Windows identity).

### Stdout: NDJSON `InventoryProgressEvent` lines

One JSON object per line, each line is a complete `InventoryProgressEvent`:

```json
{"projectName":"Alpha","orgOrCollection":"https://dev.azure.com/myorg","workItemsCount":1200,"revisionsCount":3450,"isComplete":false,"windowStart":"2026-01-01T00:00:00Z","windowEnd":"2026-05-01T00:00:00Z","windowSize":"120.00:00:00","error":null,"timestamp":"2026-04-04T10:23:11Z"}
```

Final event per project has `"isComplete": true`.

Error events have a non-null `"error"` field and `"isComplete": true`.

### Stderr: unstructured error text only

Used for unhandled exceptions and fatal startup errors. Not parsed by the host.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | All enabled entries counted (some may have per-project errors in NDJSON) |
| 1 | Fatal startup/auth error — subprocess could not connect |
| 2 | Subprocess received cancellation signal |
| 3+ | Unhandled exception |

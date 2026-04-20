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
    "type": "AzureDevOpsServices | TeamFoundationServer | Simulated",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "...",
    "authentication": {
      "type": "Pat | Windows",
      "accessToken": "<literal-token> | $ENV:MY_PAT_VAR"
    },
    "_simulatedOnly_seed": 42,
    "_simulatedOnly_workItemCount": 25000,
    "_simulatedOnly_projectCount": 1,
    "_simulatedOnly_workItemTypeDistribution": { "Bug": 40, "Task": 40, "User Story": 20 },
    "_simulatedOnly_avgRevisionsPerItem": 3,
    "_simulatedOnly_includeAttachments": false,
    "_simulatedOnly_includeLinks": true
  },
  "target": {
    "type": "AzureDevOpsServices | Simulated",
    "orgOrCollection": "...",
    "project": "...",
    "apiVersion": "...",
    "authentication": {
      "type": "Pat",
      "accessToken": "$ENV:TARGET_PAT"
    },
    "_simulatedOnly_validateOnWrite": true,
    "_simulatedOnly_failOnFirstError": true
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
            "query": "SELECT [System.Id] FROM WorkItems WHERE ..."
          }
        }
      ],
      "extensions": [
        { "type": "Revisions",      "enabled": true },
        { "type": "Links",          "enabled": true },
        { "type": "Attachments",    "enabled": true },
        { "type": "Comments",       "enabled": true },
        { "type": "EmbeddedImages", "enabled": true }
      ]
    }
  ],
  "policies": {
    "retries": { "max": 8 },
    "throttle": { "maxConcurrency": 4 }
  },
  "environment": {
    "type": "Standalone | Hosted",
    "controlPlane": {
      "baseUrl": "http://localhost:5100"
    },
    "agentRunner": {
      "type": "AzureContainerApps",
      "subscriptionId": "$ENV:AZURE_SUBSCRIPTION_ID",
      "resourceGroup": "$ENV:AZURE_RESOURCE_GROUP",
      "environmentName": "$ENV:ACA_ENVIRONMENT",
      "auth": {
        "type": "ServicePrincipal",
        "tenantId": "$ENV:AZURE_TENANT_ID",
        "clientId": "$ENV:AZURE_CLIENT_ID",
        "clientSecret": "$ENV:AZURE_CLIENT_SECRET"
      }
    }
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
| `source` | Required for `Export` and `Both`; Mode 1 inventory | Source system connection details. `type` must be one of `AzureDevOpsServices`, `TeamFoundationServer`, or `Simulated`. |
| `source.authentication` | No | Auth credentials block (`type` + `accessToken`). If omitted, Windows-integrated auth is used. Not used for `Simulated` source type. |
| `target` | Required for `Import` and `Both` | Target system connection details. `type` must be `AzureDevOpsServices` or `Simulated`. |
| `target.authentication` | No | Auth credentials block (`type` + `accessToken`). Not used for `Simulated` target type. |
| `organisations` | Mode 2 inventory only | Multi-org tooling roster. Mutually exclusive with `source`. Each entry has `type`, `url`, `projects`, `authentication`, `enabled`, and an optional `scopes` array. |
| `modules` | Yes | Ordered list of modules to run. Each module declares `scopes` (selection criteria) and named `extensions`. |
| `policies` | No | Retry and throttle policies |

### Organisation `enabled` Flag — Discovery Behaviour

The `enabled` flag on each organisation entry has a precise and intentional meaning in the context of `discovery dependencies`:

| Behaviour | `enabled: true` | `enabled: false` |
|---|---|---|
| Iterate and discover work item links | ✅ Yes | ❌ No — skipped entirely |
| Participate in GUID → project name resolution | ✅ Yes | ✅ **Yes — still included** |

Setting `enabled: false` only suppresses the discovery iteration for that organisation — the platform will not enumerate its work items or analyse its links. However, all configured organisations (enabled or not) are still used to resolve cross-organisation project name GUIDs to human-readable names.

**Use case**: You have three organisations — `org1`, `org2`, and `org3`. You only want to run dependency discovery against one project in `org1`, but your work items in `org1` may contain cross-organisation links pointing at projects in `org2` or `org3`. Set `org2` and `org3` to `enabled: false` to skip their discovery, while still allowing the tool to resolve linked project GUIDs in those organisations to readable names:

```json
{
  "organisations": [
    {
      "type": "AzureDevOpsServices",
      "url": "https://dev.azure.com/org1",
      "projects": ["MyProject"],
      "enabled": true,
      "authentication": { "type": "Pat", "accessToken": "$ENV:ORG1_PAT" }
    },
    {
      "type": "AzureDevOpsServices",
      "url": "https://dev.azure.com/org2",
      "enabled": false,
      "authentication": { "type": "Pat", "accessToken": "$ENV:ORG2_PAT" }
    },
    {
      "type": "AzureDevOpsServices",
      "url": "https://dev.azure.com/org3",
      "enabled": false,
      "authentication": { "type": "Pat", "accessToken": "$ENV:ORG3_PAT" }
    }
  ]
}
```

In this configuration, discovery runs only against `org1/MyProject`. Any cross-org links pointing at `org2` or `org3` are still resolved to their human-readable project names (rather than remaining as raw GUIDs), and are marked `Reachable` in the output CSV because credentials are present. If no PAT is provided for a disabled organisation, project GUIDs in that org cannot be resolved and the raw GUID is used as the project name in the output.

### Module Scopes and Extensions Pattern

Each module declares `scopes` (mandatory selection criteria) and a list of named `extensions`.
`Scopes` determine **what** the module operates on. For WorkItems the only current scope type is `wiql`,
whose `query` parameter supplies the WIQL statement.
`Extensions` determine **what additional data** is collected alongside each item.
Each extension is a named sub-module that can be independently enabled or disabled.
Extension-specific parameters live inside that extension's `parameters` block.

WorkItems extensions:

| Extension Type | Description |
|---|---|
| `Revisions` | Export full revision history |
| `Links` | Export related links, external links, and hyperlinks |
| `Attachments` | Download and store attachment binaries beside each `revision.json` |
| `Comments` | Fetch comment versions from the ADO Comments API and write as `comment.json` |
| `EmbeddedImages` | Download and rewrite inline images from HTML/Markdown fields |

### Policies

| Policy | Field | Default | Description |
|---|---|---|---|
| Retries | `policies.retries.max` | `3` | Maximum retry attempts for transient failures |
| Concurrency | `policies.throttle.maxConcurrency` | `2` | Maximum parallel API requests |

### WorkItems Module — Scopes and Extensions

The `WorkItems` module accepts a `scopes` array and named extensions:

| Field / Extension | Type | Default | Description |
|---|---|---|---|
| `scopes[wiql].parameters.query` | string | platform default | WIQL query selecting work items. `@project` is substituted with the configured project name. |
| `scopes[filter].parameters.mode` | string | — | Filter direction: `include` (only items matching the pattern are processed) or `exclude` (items matching the pattern are skipped). Required when scope type is `filter`. |
| `scopes[filter].parameters.field` | string | — | Reference name of the ADO field to evaluate (e.g. `System.AreaPath`). Must be non-empty. |
| `scopes[filter].parameters.pattern` | string | — | Case-insensitive regex pattern applied to the field value (using `System.Text.RegularExpressions`, 2 s timeout). Must be a valid regex. |
| `extensions[Revisions].enabled` | bool | `true` | When `true`, export all revision history. When `false`, export latest state only. |
| `extensions[Links].enabled` | bool | `true` | When `true`, export related links, external links, and hyperlinks. |
| `extensions[Attachments].enabled` | bool | `true` | When `true`, download and store attachment binaries beside each `revision.json`. |
| `extensions[Comments].enabled` | bool | `true` | When `true`, fetch comment versions from the ADO Comments API and write `comment.json` beside matching revisions. |
| `extensions[Comments].parameters.includeDeleted` | bool | `false` | When `true`, include soft-deleted comments in the export. |
| `extensions[EmbeddedImages].enabled` | bool | `true` | When `true`, download inline images from HTML/Markdown fields and rewrite URLs. |
| `extensions[EmbeddedImages].parameters.downloadTimeoutSeconds` | int | `30` | Timeout in seconds for individual image downloads. |
| `extensions[WorkItemResolutionStrategy].enabled` | bool | `false` | When `true`, seed `idmap.db` from the target at import startup using the configured strategy. Applicable to **import** only. |
| `extensions[WorkItemResolutionStrategy].parameters.strategy` | string | — | Strategy name: `TargetField` or `TargetHyperlink`. Required when enabled. |
| `extensions[WorkItemResolutionStrategy].parameters.fieldName` | string | — | **TargetField only**: Reference name of the custom field that holds the source work item ID (e.g. `Custom.SourceWorkItemId`). |
| `extensions[WorkItemResolutionStrategy].parameters.urlPattern` | string | — | **TargetHyperlink only**: URL pattern with `{id}` as the source ID placeholder (e.g. `https://source.example.com/wi/{id}`). |

> **Filter scope semantics**: Multiple `filter` scopes are evaluated with AND logic. An `include` filter retains only items whose field value matches the pattern; an `exclude` filter discards items whose field value matches. Items where the filtered field is absent pass `exclude` (absent = does not match) and fail `include`. Prefer short, indexed reference-data fields (e.g. `System.AreaPath`, `System.WorkItemType`) to minimise pre-fetch time.

### Config Versioning and Upgrader

- `configVersion` must be incremented on any breaking change to the config schema.
- An upgrader must exist for each version transition (e.g., `1.0 → 2.0`).
- The tool must detect an outdated config version and either auto-upgrade (with warning) or fail fast with instructions.
- Configs from future versions must fail fast with a clear error message.

### Polymorphic Endpoint Config

`source` and `target` blocks use a **type-discriminated polymorphic model**. The `type` field is the discriminator — it must appear first (or at minimum be present) in the JSON object. The platform reads the `type` value, looks up the registered `MigrationEndpointOptions` subtype, then deserialises the remaining fields into that subtype.

| `type` value | Options class | Extra fields |
|---|---|---|
| `AzureDevOpsServices` | `AzureDevOpsEndpointOptions` | `url`, `project`, `apiVersion`, `authentication` |
| `TeamFoundationServer` | `TeamFoundationServerEndpointOptions` | `url`, `project`, `authentication` |
| `Simulated` | `SimulatedEndpointOptions` | `generator.projects[].name`, `generator.projects[].workItemTypes[].type`, `generator.projects[].workItemTypes[].count`, `generator.projects[].workItemTypes[].revisionsPerItem` |

An unknown `type` value causes a startup error with a message containing the offending discriminator value. Each connector assembly registers its `type` key via `AddEndpointOptionsType(key, typeof(TOptions))` at DI startup time.

**Example — Simulated source with explicit generator config:**

```json
{
  "source": {
    "type": "Simulated",
    "generator": {
      "projects": [
        {
          "name": "SimulatedProject1",
          "workItemTypes": [
            { "type": "Bug",        "count": 5, "revisionsPerItem": 3 },
            { "type": "Task",       "count": 5, "revisionsPerItem": 2 }
          ]
        }
      ]
    }
  }
}
```

### Simulated Source Configuration

When `source.type` is `Simulated`, the following fields control the generated data. Authentication is not used.

| Field | Required | Default | Description |
|---|---|---|---|
| `source.seed` | No | random (logged + recorded in manifest) | Integer seed for deterministic data generation. Same seed + same `workItemCount` produces identical packages across runs. |
| `source.workItemCount` | Yes for Simulated | — | Total number of work items to generate. Minimum 1; tested successfully at 25,000. |
| `source.projectCount` | No | 1 | Number of simulated projects to generate across. |
| `source.workItemTypeDistribution` | No | `{ "Bug": 50, "Task": 50 }` | Map of work item type name to percentage. Must sum to 100. |
| `source.avgRevisionsPerItem` | No | 3 | Average number of revisions per generated work item. |
| `source.includeAttachments` | No | false | When true, attachment metadata (and optionally binaries) are generated per revision. |
| `source.includeLinks` | No | true | When true, related links are generated between simulated work items. |

### Simulated Target Configuration

When `target.type` is `Simulated`, all work items are accepted without writing to any external system. Authentication is not used.

| Field | Required | Default | Description |
|---|---|---|---|
| `target.validateOnWrite` | No | true | Validates each revision against the package schema as it arrives. |
| `target.failOnFirstError` | No | true | Fails the import on the first schema validation error. |

---

### Environment Configuration

The `environment` section configures the execution environment. It replaces the previous `MIGRATION_API_URL` environment variable and `--url` CLI flag. The config file is the single source of truth for the control plane endpoint.

When the `environment` section is absent, the platform defaults to **Standalone** mode with the control plane at `http://localhost:5100`.

#### Standalone (default)

```json
{
  "environment": {
    "type": "Standalone",
    "controlPlane": {
      "baseUrl": "http://localhost:5100"
    }
  }
}
```

#### Hosted (with optional AgentRunner)

```json
{
  "environment": {
    "type": "Hosted",
    "controlPlane": {
      "baseUrl": "https://controlplane.example.com"
    },
    "agentRunner": {
      "type": "AzureContainerApps",
      "subscriptionId": "$ENV:AZURE_SUBSCRIPTION_ID",
      "resourceGroup": "$ENV:AZURE_RESOURCE_GROUP",
      "environmentName": "$ENV:ACA_ENVIRONMENT",
      "auth": {
        "type": "ServicePrincipal",
        "tenantId": "$ENV:AZURE_TENANT_ID",
        "clientId": "$ENV:AZURE_CLIENT_ID",
        "clientSecret": "$ENV:AZURE_CLIENT_SECRET"
      }
    }
  }
}
```

| Field | Required | Default | Description |
|---|---|---|---|
| `environment.type` | No | `Standalone` | `Standalone` or `Hosted` |
| `environment.controlPlane.baseUrl` | No (Standalone) / Yes (Hosted) | `http://localhost:5100` | Control plane HTTP endpoint |
| `environment.agentRunner` | No | `null` | Agent runner config for hosted cross-tenant execution. Must be null for Standalone. |
| `environment.agentRunner.type` | Yes (if agentRunner) | — | Runner type, e.g. `AzureContainerApps` |
| `environment.agentRunner.subscriptionId` | Yes (if agentRunner) | — | Azure subscription ID. Supports `$ENV:` |
| `environment.agentRunner.resourceGroup` | Yes (if agentRunner) | — | Azure resource group. Supports `$ENV:` |
| `environment.agentRunner.environmentName` | Yes (if agentRunner) | — | Container Apps environment name. Supports `$ENV:` |
| `environment.agentRunner.auth.type` | Yes (if auth) | — | `ServicePrincipal` |
| `environment.agentRunner.auth.tenantId` | Yes (if auth) | — | Azure AD tenant ID. Supports `$ENV:` |
| `environment.agentRunner.auth.clientId` | Yes (if auth) | — | Service principal client ID. Supports `$ENV:` |
| `environment.agentRunner.auth.clientSecret` | Yes (if auth) | — | Service principal secret. Supports `$ENV:`. Never logged. |

#### Validation Rules

1. **Standalone** — `agentRunner` must be null.
2. **Hosted without agentRunner** — valid (agent execution handled by the control plane environment).
3. **Hosted with agentRunner** — `subscriptionId`, `resourceGroup`, `environmentName`, and `auth` are all required.
4. **Auth type ServicePrincipal** — `tenantId`, `clientId`, `clientSecret` are all required.
5. All `$ENV:VAR_NAME` references are resolved at runtime; fail fast if the variable is unset or empty.

---

## Scenario Configs

Ready-to-run example configuration files live under `/scenarios/` at the repository root. Each file targets a specific connectivity scenario and is wired to a VS Code launch configuration for quick local debugging.

| File | Scenario |
|---|---|
| `inventory-ado-single-project.json` | Single Azure DevOps project inventory (PAT auth) |
| `inventory-ado-multi-project.json` | Multi-project Azure DevOps inventory (PAT auth) |
| `inventory-tfs-windows-auth.json` | On-premises TFS inventory (Windows-integrated auth) |
| `inventory-multi-org.json` | Multi-organisation inventory with per-org PAT tokens |
| `queue-export-ado-workitems-single-project.json` | Export all work items from a single Azure DevOps project (PAT auth); inline comment fetching enabled by default |
| `queue-export-ado-workitems-inline-comments.json` | Export all work items with inline comment fetching explicitly disabled (`inlineComments.enabled: false`) for performance-sensitive runs |
| `export-simulated.json` | Simulated source export (25,000 work items, no external connectivity required) |
| `migrate-simulated.json` | Full simulated end-to-end migration — both source and target simulated (25,000 work items) |

Credentials in these files use `$ENV:VARNAME` references — never literal tokens. Set the corresponding environment variables locally (e.g. `AZDEVOPS_SYSTEM_TEST_PAT`) before running.

---

## Telemetry

All OTel instruments follow a dot-separated naming convention under the `migration.` prefix:

- **Meter name**: `DevOpsMigrationPlatform.Migration` (v2.0)
- **Instrument naming**: `migration.<category>.<metric>` — e.g. `migration.workitems.attempted`, `migration.payload.field_count`, `migration.correctness.broken_links`
- **Mandatory dimension tags**: Every metric emission includes `job.id`, `operation` (`export` | `import`), and `module` (e.g. `WorkItems`)
- **Optional tag**: `source.type` (e.g. `AzureDevOps`, `TfsObjectModel`, `Simulated`)

The canonical list of instrument names is defined in `WellKnownMetricNames` (`src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`). Tags are constructed via `MigrationTagList.Create()`.

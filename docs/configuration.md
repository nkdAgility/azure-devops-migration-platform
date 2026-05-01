# Configuration

## 10. Configuration Model

A single JSON configuration file drives the entire run.

### Full Schema

```json
{
  "configVersion": "2.0",
  "mode": "Export | Prepare | Import | Migrate",
  "artefacts": {
    "path": "D:\\exports\\run-001",
    "zip": false
  },
  "source": {
    "type": "AzureDevOpsServices | TeamFoundationServer | Simulated",
    "url": "https://dev.azure.com/myorg",
    "project": "MyProject",
    "apiVersion": "7.1",
    "authentication": {
      "type": "Pat | Windows",
      "accessToken": "<literal-token> | $ENV:MY_PAT_VAR"
    }
  },
  "target": {
    "type": "AzureDevOpsServices | Simulated",
    "url": "https://dev.azure.com/targetorg",
    "project": "TargetProject",
    "apiVersion": "7.1",
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
  "Tools": {
    "FieldTransform": {
      "Enabled": true,
      "TransformGroups": [
        {
          "Name": "StateRemapping",
          "Enabled": true,
          "ApplyTo": ["Bug", "UserStory"],
          "Transforms": [
            {
              "Type": "MapValue",
              "Field": "System.State",
              "ValueMap": {
                "Active": "In Progress",
                "Resolved": "Done"
              }
            }
          ]
        }
      ]
    },
    "NodeTranslation": {
      "Enabled": true,
      "ReplicateSourceTree": true,
      "AutoCreateNodes": true,
      "SkipOnUnresolvableArea": false,
      "SkipOnUnresolvableIteration": false,
      "AreaPathMappings": [
        { "Match": "^OldProject\\\\", "Replacement": "NewProject\\" }
      ],
      "IterationPathMappings": []
    }
  },
  "modules": [
    {
      "name": "Identities",
      "enabled": true,
      "defaultIdentity": "migration-service@contoso.com"
    },
    {
      "name": "Nodes",
      "enabled": true
    },
    {
      "name": "Teams",
      "enabled": true,
      "scope": "all",
      "filter": "",
      "extensions": [
        { "type": "TeamSettings",    "enabled": true },
        { "type": "TeamIterations",  "enabled": true },
        { "type": "TeamMembers",     "enabled": true },
        { "type": "TeamCapacity",    "enabled": true }
      ]
    },
    {
      "name": "WorkItems",
      "scopes": [
        {
          "type": "wiql",
          "parameters": {
            "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.ChangedDate] ASC"
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
| `mode` | Yes | `Export`, `Prepare`, `Import`, or `Migrate` |
| `artefacts.path` | Yes | Absolute path to the package root directory |
| `artefacts.zip` | No | If `true`, pack/unpack around the run; default `false` |
| `source` | Required for `Export` and `Migrate`; Mode 1 inventory | Source system connection details. `type` must be one of `AzureDevOpsServices`, `TeamFoundationServer`, or `Simulated`. |
| `source.authentication` | No | Auth credentials block (`type` + `accessToken`). If omitted, Windows-integrated auth is used. Not used for `Simulated` source type. |
| `target` | Required for `Prepare`, `Import`, and `Migrate` | Target system connection details. `type` must be `AzureDevOpsServices` or `Simulated`. |
| `target.authentication` | No | Auth credentials block (`type` + `accessToken`). Not used for `Simulated` target type. |
| `organisations` | Mode 2 inventory only | Multi-org tooling roster. Mutually exclusive with `source`. Each entry has `type`, `url`, `projects`, `authentication`, `enabled`, and an optional `scopes` array. |
| `Tools` | No | Keyed object of shared tool declarations. Each key is the tool type name (e.g., `FieldTransform`), each value is tool-specific configuration. Extensions reference tools by key name. |
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

### Resumable Batching — Operational Responsibilities

Callers of `IWorkItemFetchService` (inventory, dependency analysis, discovery) can opt in to resumable batching by setting `ResumeEnabled = true` on `WorkItemFetchScope`. This enables interrupted operations to continue from a saved `BatchContinuationToken` instead of reprocessing the entire project.

**Resume is not a configuration-file setting.** It is a programmatic option set by orchestrator code. Configuration is relevant only for the operational responsibilities described below.

#### Caller Persistence Cadence

The batching strategy emits a `BatchContinuationToken` via a callback after each batch window. Callers choose their own persistence cadence:

- **Every batch** — lowest re-work on interruption; highest I/O overhead.
- **Every N batches** — balanced; at most N batches of re-work on interruption.
- **Completion only** — lowest I/O; no mid-run resume capability (only useful for short runs).

A mandatory completion checkpoint (`Completed = true`) is emitted at end-of-stream. Callers **must** persist this final token to enable safe no-op resume on subsequent runs.

#### Duplicate Handling

Source data can change between runs. When resume is active, the same work item ID may appear in multiple batch windows (once before interruption, once after resume). The strategy does **not** deduplicate — callers must handle duplicates via one of:

- **Idempotent persistence** — write operations that are safe to repeat (e.g. upsert semantics).
- **Explicit dedup** — caller-side ID tracking to skip already-processed items.
- **Log-only** — accept duplicates and record them for diagnostic purposes.

#### Safe Restart Guidance

| Scenario | Expected behavior |
|----------|-------------------|
| Resume with unchanged query | Continues from saved position (no re-work) |
| Resume with changed query | `ResumeRejectedException` thrown; caller decides recovery |
| Resume with no saved token | Starts from beginning (info log, no error) |
| Resume with corrupted token | Treated as no token; starts from beginning |
| `ResumeEnabled = false` (default) | Token ignored; normal traversal; zero behavioral change |

### Config Versioning and Upgrader

- `configVersion` must be incremented on any breaking change to the config schema.
- An upgrader must exist for each version transition (e.g., `1.0 → 2.0`).
- The tool must detect an outdated config version and either auto-upgrade (with warning) or fail fast with instructions.
- Configs from future versions must fail fast with a clear error message.
- **Current version**: `"2.0"` (since feature 025-agent-config-package). Config travels as `configPayload` inside the `Job` dispatch token; the agent writes it to `migration-config.json` at job startup.

---

### Runtime Config Injection — `IOptions<T>` Pattern

The runtime **does not** inject the monolithic `MigrationOptions` type into modules or tools. Instead, each module or tool declares a dependency on its own isolated options slice via `IOptions<T>`:

- Each options class declares a `public const string SectionName` constant (e.g. `"MigrationPlatform:Modules:WorkItems"`).
- The options class is registered in the connector or module's `Add*Services()` extension method via `AddSchemaEntry<T>()`, which both registers the `IOptions<T>` binding and adds a `SchemaOptionsEntry` to drive schema generation.
- Modules receive only their own options: `IOptions<WorkItemsModuleOptions>`, `IOptions<TeamsModuleOptions>`, etc.

`MigrationOptions` is a **serialisation-only DTO** — it describes the config file shape and is used by the CLI to write `migration-config.json`. It is **not** injected into modules or tools at runtime.

---

### Cross-Cutting Job Values — `IAgentJobContext`

Modules and tools that need access to the current job's execution mode, package path, or config version **must** use `IAgentJobContext` — not navigate the options graph. This keeps the module's dependency on the job context explicit and independent from config binding:

```csharp
public interface IAgentJobContext
{
    string Mode { get; }          // "Export", "Import", "Prepare", or "Migrate"
    string PackagePath { get; }   // Resolved absolute path to the package root on disk
    string ConfigVersion { get; } // e.g. "2.0"
}
```

`IAgentJobContext` is scoped to a single agent job. It is constructed once when the job starts and never mutated. It is registered by `MigrationAgentServiceExtensions` (and by `TfsMigrationAgentServiceExtensions` for the TFS agent) before any module executes.

---

### Connector Endpoint Info — `ISourceEndpointInfo` / `ITargetEndpointInfo`

Modules and tools that need the resolved source or target URL and project name **must** use `ISourceEndpointInfo` / `ITargetEndpointInfo`. These are registered by each connector's `Add*Services()` method and resolve against the job's config at startup:

```csharp
public interface ISourceEndpointInfo
{
    string Url { get; }           // e.g. https://dev.azure.com/myorg
    string Project { get; }       // Project name
    string ConnectorType { get; } // "AzureDevOpsServices" | "TeamFoundationServer" | "Simulated"
}

public interface ITargetEndpointInfo  // not registered for TFS (source-only)
{
    string Url { get; }
    string Project { get; }
    string ConnectorType { get; }
}
```

Modules **must not** inject connector-specific options classes (e.g. `AzureDevOpsEndpointOptions`) directly. Using `ISourceEndpointInfo` and `ITargetEndpointInfo` keeps modules connector-agnostic and independently testable.

---

### Schema Generation — `SchemaOptionsEntry`

The committed `migration.schema.json` (in `src/DevOpsMigrationPlatform.CLI.Migration/`) is generated automatically from DI registrations at build time by the `DevOpsMigrationPlatform.SchemaGenerator` project.

How it works:
1. Each options class that contributes to the schema calls `services.AddSchemaEntry<T>(SectionName)` in its connector's or module's `Add*Services()` extension.
2. This registers a `SchemaOptionsEntry` singleton (containing the CLR type and the JSON path) into the DI container.
3. The schema generator resolves all `SchemaOptionsEntry` instances from the container, derives JSON Schema from each type using NJsonSchema reflection, and assembles the merged schema.
4. The schema is written to `migration.schema.json` and committed to the repository.

To add a new configurable section to the schema:
- Add a `public const string SectionName = "MigrationPlatform:...";` to the options class.
- Call `services.AddSchemaEntry<YourOptions>(YourOptions.SectionName)` in the appropriate `Add*Services()` method.
- Rebuild — the schema generator updates `migration.schema.json` automatically.

---

### IDE IntelliSense — `json.schemas` Integration

The repository registers `migration.schema.json` in `.vscode/settings.json` so VS Code automatically applies it to any `migration.json` file opened in the workspace:

```json
{
  "json.schemas": [
    {
      "fileMatch": ["**/migration*.json", "**/scenario*.json"],
      "url": "./src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json"
    }
  ]
}
```

This provides:
- IntelliSense completions for all sections (`source`, `target`, `Tools`, `modules`, etc.)
- Hover documentation on each key
- Validation warnings for unknown or wrongly-typed keys
- Discriminated-union completions for `source.type` / `target.type`

---

### Tier 0 Validation — Schema Check Before Submission

Before the CLI serialises the config into `Job.ConfigPayload` or makes any network call, it validates the raw `migration.json` file against the committed `migration.schema.json`. This is **Tier 0** — purely local, no connectivity required.

- Unknown keys at any nesting level → non-zero exit, JSON path printed
- Missing required fields → non-zero exit, field path printed
- Wrong value types → non-zero exit, path + constraint printed
- `migration.schema.json` absent from the CLI output directory → warning logged, Tier 0 skipped, Tier 1 proceeds

See [docs/validation.md](validation.md) for the full four-tier model.

---

### NodeTranslation Tool

The `NodeTranslation` tool (config key `Tools.NodeTranslation`) controls area and iteration path remapping, node auto-creation, and source tree replication:

| Field | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Enable/disable the tool entirely |
| `ReplicateSourceTree` | bool | `false` | Copy the full source area/iteration tree to the target before import |
| `AutoCreateNodes` | bool | `true` | Auto-create any referenced path that does not exist on the target |
| `SkipOnUnresolvableArea` | bool | `false` | If `true`, skip a work item rather than failing when its area path cannot be resolved or created |
| `SkipOnUnresolvableIteration` | bool | `false` | If `true`, skip rather than fail on unresolvable iteration paths |
| `AreaLanguageOverride` | string | `null` | Override the default localised root node name for area paths (e.g. `"Area"` in English, `"Bereich"` in German) |
| `IterationLanguageOverride` | string | `null` | Override the localised root node name for iteration paths |
| `AreaPathMappings` | array | `[]` | Regex-based area path transformations applied before node creation. Each entry has `Match` (regex) and `Replacement`. Applied in order. |
| `IterationPathMappings` | array | `[]` | Same as `AreaPathMappings` but for iteration paths |

**Example**: Rename the project root prefix on all paths:

```json
"Tools": {
  "NodeTranslation": {
    "Enabled": true,
    "AutoCreateNodes": true,
    "AreaPathMappings": [
      { "Match": "^OldProject\\\\", "Replacement": "NewProject\\" }
    ]
  }
}
```

---

### FieldTransform Tool — Available Transform Types

The `FieldTransform` tool (`Tools.FieldTransform`) applies a named sequence of transform groups to work item revisions. Transforms execute in array order within each group; groups execute in array order. The `Phase` field (default: `Import`) controls when the transform runs.

Available transform types:

| Type | Description |
|---|---|
| `CopyField` | Copy value from one field to another |
| `CopyFieldBatch` | Copy multiple fields in a single declaration (shorthand for multiple `CopyField` transforms) |
| `SetField` | Set a field to a literal constant value |
| `MapValue` | Translate values via a key→value map |
| `MergeFields` | Concatenate multiple source fields into one target field using a format template |
| `CalculateField` | Compute a new value from an arithmetic/string expression |
| `ClearField` | Remove (null-out) a field's value |
| `ExcludeField` | Remove a field from the revision entirely |
| `ConditionalTag` | Add or remove a tag based on a condition |
| `FieldToTag` | Promote a field value to a tag |
| `MergeToTag` | Merge multiple field values into a single tag |
| `ConditionalField` | Set or transform a field only when a condition is met |
| `RegexField` | Apply a regex find-and-replace to a field value |
| `TreeToTag` | Flatten a hierarchical tree path (area/iteration) into tag values |

Each transform must specify at minimum `Type` and `Field`. Additional fields are type-specific.

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

### Telemetry Layer Architecture

Telemetry is structured into three layers with different cross-runtime characteristics:

| Layer | What lives here | Cross-runtime? | Guard? |
|---|---|---|---|
| **Recording** — interfaces, constants, tag builders | `Abstractions/Telemetry/` | Both net481 and net10.0 | No `#if` guards |
| **Instrument** — concrete `Meter`/`Counter`/`Histogram` classes | `Infrastructure/Telemetry/` | Both net481 and net10.0 | No `#if` guards |
| **Pipeline** — OTel SDK exporters, readers, DI registration | `Infrastructure/Telemetry/` and host projects | Host-specific | `#if !NETFRAMEWORK` where needed |

The recording and instrument layers use only `System.Diagnostics.DiagnosticSource` types (`TagList`, `Meter`, `Counter<T>`, etc.), which are available on net481 via the NuGet package and inbox on net10.0. This means all metric interfaces and concrete implementations compile and run on both runtimes.

The pipeline layer references OTel SDK types (`BaseExporter<Metric>`, `PeriodicExportingMetricReader`, `MeterProviderBuilder`) that have different registration patterns per host. The .NET 10 hosts use `TelemetryServiceExtensions` (via `ServiceDefaults`). The TFS subprocess registers its OTel pipeline directly in `MigrationPlatformHost.CreateDefaultBuilder()`.

#### Adding a new metric

1. Add the instrument name constant to `WellKnownMetricNames.cs` (or `WellKnownDiscoveryMetricNames.cs`).
2. Add the recording method to the appropriate interface (`IMigrationMetrics` or `IDiscoveryMetrics`).
3. Implement the instrument field and method in the concrete class (`MigrationMetrics` or `DiscoveryMetrics`).
4. Call the recording method from module code, passing a pre-built `TagList` via `MigrationTagList.Create()`.
5. If the metric should appear in `MetricSnapshot`, add a property and update `SnapshotMetricExporter`.
6. Add a unit test to the corresponding `*MetricsTests` class.

See `.agents/context/telemetry-architecture.md` for the full guide including code examples and placement rules.

### Data Classification

Log statements are classified by data sensitivity using `DataClassification` scopes. The classification determines which log destinations receive the data:

| Classification | Description | Azure Monitor | Package Log | Control Plane |
|---|---|---|---|---|
| **System** (default) | Operational logs — health checks, module lifecycle, job IDs | ✅ | ✅ | ✅ |
| **Customer** | Customer-identifiable data — field values, project names, org URLs, attachment paths | ❌ | ✅ | ✅ |
| **Derived** | Aggregates and counts — "500 work items processed" | ✅ | ✅ | ✅ |

**Rules:**

- Unclassified logs default to **System** — safe for Azure Monitor. This enables gradual rollout.
- Only **Customer** is filtered from Azure Monitor. System and Derived pass through.
- The filter is implemented as a provider-level logging filter on `OpenTelemetryLoggerProvider` (`DataClassificationLogging.AddDataClassificationFilter()`) that reads the ambient `DataClassificationScope.Current` value.
- `PackageLoggerProvider` and `ControlPlaneLoggerProvider` write **all** classifications unfiltered — the filter only applies to the OTel export pipeline.
- When scopes are nested, the **innermost** classification wins.

**Usage in code:**

```csharp
// .NET 10 code (Infrastructure project): use the ILogger extension
using (logger.BeginDataScope(DataClassification.Customer))
{
    logger.LogInformation("Importing into {OrgUrl}/{Project}", orgUrl, project);
}

// .NET 4.8 code (TFS ObjectModel): use the static scope directly
using (DataClassificationScope.Begin(DataClassification.Customer))
{
    logger.LogDebug("Streaming revisions for project {Project}", project);
}
```

---

## Tools

The `Tools` section at the `MigrationPlatform` config root declares shared, cross-cutting tool singletons. Each key is the tool type name; each value is tool-specific configuration. Extensions reference tools by key name and may declare per-extension overrides.

### FieldTransform Tool

The `FieldTransform` tool applies a declared set of field transformation rules to each work item revision. Rules are grouped into named `TransformGroups`, each with an optional work-item-type filter (`ApplyTo`). Groups and transforms within a group are applied in declaration order.

#### Schema

| Field | Required | Default | Description |
|---|---|---|---|
| `Tools.FieldTransform.Enabled` | No | `true` | Master switch. When `false`, all transform groups are skipped. |
| `Tools.FieldTransform.TransformGroups[].Name` | Yes | — | Logical name for the group (used in logs and diagnostics). |
| `Tools.FieldTransform.TransformGroups[].Enabled` | No | `true` | When `false`, the group is skipped entirely. |
| `Tools.FieldTransform.TransformGroups[].ApplyTo` | No | all types | Optional array of work item type names. The group is applied only when the revision type matches. |
| `Tools.FieldTransform.TransformGroups[].Transforms[].Type` | Yes | — | Transform discriminator. See transform types below. |
| `Tools.FieldTransform.TransformGroups[].Transforms[].Field` | Varies | — | Reference name of the field to read or write (e.g. `System.State`). |

#### Transform Types

| Type | Purpose |
|---|---|
| `CopyField` | Copy field A → field B with optional default |
| `CopyFieldMulti` | Multiple source→target field copy pairs |
| `SetLiteral` | Set field to a literal value |
| `MapValue` | Dictionary-based value remapping (e.g. State values) |
| `MergeFields` | Merge multiple source fields into one with format string |
| `CalculateField` | Compute field from an expression |
| `ClearField` | Null-out a field |
| `SkipField` | Exclude field from the written revision (not imported) |
| `ValueToTag` | Append to `System.Tags` when field value matches a pattern |
| `FieldToTag` | Append field value to `System.Tags` |
| `MergeToTagField` | Merge multiple field values into a tag-style target field |
| `ConditionalMap` | Conditional multi-field → single field mapping |
| `RegexReplace` | Regex find-and-replace within a field value |
| `TreeToTag` | Convert area/iteration tree path into a tag |

#### Example

```json
{
  "MigrationPlatform": {
    "Tools": {
      "FieldTransform": {
        "Enabled": true,
        "TransformGroups": [
          {
            "Name": "StateRemapping",
            "Enabled": true,
            "ApplyTo": ["Bug", "UserStory"],
            "Transforms": [
              {
                "Type": "MapValue",
                "Field": "System.State",
                "ValueMap": {
                  "Active": "In Progress",
                  "Resolved": "Done"
                }
              }
            ]
          }
        ]
      }
    }
  }
}
```

---

### NodeTranslation Tool

The `NodeTranslation` tool manages classification node (area/iteration) path translation, replication, and validation during import. It normalises localised root names (e.g. `Área` → `Area`), applies explicit path mappings, and can auto-create or replicate nodes from the source tree.

#### Schema

| Field | Required | Default | Description |
|---|---|---|---|
| `Tools.NodeTranslation.Enabled` | No | `true` | Master switch. When `false`, node-structure processing is entirely skipped. |
| `Tools.NodeTranslation.ReplicateSourceTree` | No | `false` | When `true`, the tool reads `Nodes/source-tree.json` and pre-creates every area and iteration node on the target before work items are imported. |
| `Tools.NodeTranslation.AreaLanguageOverride` | No | `null` | Localised name for the `Area` root node on the source (e.g. `"Área"` for Spanish-language ADO instances). |
| `Tools.NodeTranslation.IterationLanguageOverride` | No | `null` | Localised name for the `Iteration` root node on the source. |
| `Tools.NodeTranslation.AreaPathMappings[]` | No | `[]` | Ordered list of regex-based path rewriting rules for area paths. |
| `Tools.NodeTranslation.AreaPathMappings[].Match` | Yes | — | .NET regular-expression pattern applied to the full area path. Compiled with `RegexOptions.IgnoreCase \| RegexOptions.NonBacktracking`. |
| `Tools.NodeTranslation.AreaPathMappings[].Replacement` | Yes | — | Replacement string (supports `$1`, `$2`, … capture-group references). |
| `Tools.NodeTranslation.IterationPathMappings[]` | No | `[]` | Ordered list of regex-based path rewriting rules for iteration paths. |
| `Tools.NodeTranslation.IterationPathMappings[].Match` | Yes | — | .NET regular-expression pattern applied to the full iteration path. Compiled with `RegexOptions.IgnoreCase \| RegexOptions.NonBacktracking`. |
| `Tools.NodeTranslation.IterationPathMappings[].Replacement` | Yes | — | Replacement string (supports capture-group references). |
| `Tools.NodeTranslation.SkipOnUnresolvableArea` | No | `false` | When `true`, revisions with an unresolvable area path are skipped with a warning. When `false`, an unresolvable area path throws and halts the run. |
| `Tools.NodeTranslation.SkipOnUnresolvableIteration` | No | `false` | When `true`, revisions with an unresolvable iteration path are skipped with a warning. When `false`, an unresolvable iteration path throws and halts the run. |
| `Tools.NodeTranslation.AutoCreateNodes` | No | `false` | When `true`, nodes referenced by work item revisions are automatically created on the target if they do not already exist. |

#### Example

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeTranslation": {
        "Enabled": true,
        "ReplicateSourceTree": true,
        "AreaLanguageOverride": "Área",
        "AreaPathMappings": [
          { "Match": "^SourceOrg\\\\(.*)", "Replacement": "TargetOrg\\$1" }
        ],
        "IterationPathMappings": [
          { "Match": "^SourceOrg\\\\Sprint (\\d+)", "Replacement": "TargetOrg\\Sprint $1" }
        ],
        "SkipOnUnresolvableArea": false,
        "SkipOnUnresolvableIteration": false,
        "AutoCreateNodes": true
      }
    }
  }
}
```

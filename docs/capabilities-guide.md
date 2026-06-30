# Source Types

## 9. Source Types

The system supports two source types, controlled by `source.type` in the configuration.

### AzureDevOpsServices

```json
"source": {
  "type": "AzureDevOpsServices",
  "orgOrCollection": "https://dev.azure.com/myorg",
  "project": "MyProject",
  "apiVersion": "7.1"
}
```

**Requirements:**

- Uses the Azure DevOps REST API natively from .NET 10.
- Access token authentication (PAT, service principal, or managed identity).
- Must respect `policies.throttle.maxConcurrency` to avoid hitting rate limits.
- `apiVersion` must be pinned; do not use auto-negotiation in production runs.
- All REST responses must be validated against expected schema before being written to the package.

**Inventory:**

A `devopsmigration queue` run with `Mode: Inventory` uses the REST API directly for ADO Services:

- Date-windowed WIQL queries via `WorkItemTrackingHttpClient.QueryByWiqlAsync`.
- Initial window: 120 days. Halves if result ≥ 20,000 items; grows by 1 day after narrow success.
- Access token authentication via `VssBasicCredential`; configured by `source.authentication.accessToken` (supports `$ENV:VARNAME` resolution).

### TeamFoundationServer

```json
"source": {
  "type": "TeamFoundationServer",
  "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
  "project": "MyProject",
  "apiVersion": "15.0"
}
```

**Requirements:**

- The TFS Object Model (net481) cannot be called from .NET 10. Jobs with `source.type: TeamFoundationServer` are routed to `DevOpsMigrationPlatform.TfsMigrationAgent` via capability matching (`GET /agents/lease?capabilities=tfs`).
- The TFS agent uses `IModule` dispatch — the same pattern as the .NET 10 `MigrationAgent`. `TfsWorkItemsModule.ExportAsync` drives the export via `IWorkItemRevisionSource` (TFS OM implementation) and `WorkItemExportOrchestrator`.
- The TFS agent writes to the package via `IPackageAccess` (`FileSystemArtefactStore` is the underlying store on net481).
- A job with `source.type: TeamFoundationServer` and a blob package URI MUST be rejected at Tier 0 validation.
- Credentials are passed via the job contract (same as ADO Services) — never via command-line arguments.
- The TFS agent is Windows-only and cannot run in containers. `AgentLifecycleService` spawns it on Windows and skips it elsewhere.
- The package output is identical to an ADO export — the same canonical layout. An exported TFS package feeds directly into the standard `import` flow.

See [docs/agent-hosting.md — TFS Migration Agent](agent-hosting.md#tfs-migration-agent) for the full specification.

**Inventory:**

For TFS source types, a `devopsmigration queue` run with `Mode: Inventory` submits a `Job` (with `Kind: Inventory` and `Connectors: [TeamFoundationServer]`) to the control plane. The TFS agent picks it up via capability matching (`GET /agents/lease?capabilities=tfs`) and runs the TFS inventory module. The TFS inventory module uses `WorkItemStoreExtensions.QueryCountAllByDateChunk` for date-windowed counting (same 120-day / 20k algorithm as the ADO Services path).

### Validation and Normalisation

Regardless of source type, data written to the package must conform to the schema versions declared in `manifest.json`. The exporter or adapter layer is responsible for:

1. Validating its own output before writing.
2. Emitting a validation report to `.migration/Logs/` if any anomalies are found.
3. Failing fast if required fields are absent.

The control plane performs a secondary validation pass before beginning import when running in `Migrate` mode.

### Simulated

```json
"source": {
  "type": "Simulated",
  "seed": 42,
  "generator": {
    "projects": [
      {
        "name": "SimulatedProject1",
        "workItemTypes": [
          { "type": "Bug",        "count": 10000, "revisionsPerItem": 3 },
          { "type": "Task",       "count": 10000, "revisionsPerItem": 3 },
          { "type": "User Story", "count":  5000, "revisionsPerItem": 4 }
        ]
      }
    ]
  },
  "includeAttachments": false,
  "includeLinks": true
}
```

**Requirements:**

- Intended for **testing and development only**. No real data is read from or written to an external server.
- Generates work items deterministically: given the same `seed` and the same `generator.projects` configuration, every run produces identical work item identifiers, field values, revision counts, and link structures.
- Simulated work item field values are prefixed with `[SIMULATED]` so a package produced by simulation cannot be mistaken for a real export.
- Plugs into the existing module architecture as a standard `IModule` export implementation — the same `IArtefactStore` path as real sources.
- When `seed` is omitted, a random seed is chosen automatically, logged at `Information` level, and recorded in `manifest.json` so the run is reproducible.
- Identity mapping still runs for simulated migrations: the simulated source generates a fixed set of synthetic user identities and the `IdentitiesModule` processes them in the normal order.
- A `Simulated*Source` MUST yield at least 2 items per operation. Zero-item sources silently make all downstream tests pass vacuously and are forbidden.

**Simulated target configuration** is also supported for full end-to-end testing without a live Azure DevOps organisation:

```json
"target": {
  "type": "Simulated",
  "validateOnWrite": true,
  "failOnFirstError": true
}
```

The simulated target accepts all work items presented during import without writing to any external system. It validates each revision against the package schema as it arrives (when `validateOnWrite: true`).

**Simulated dependency discovery:**

`SimulatedDependencyDiscoveryServiceFactory` is registered via `AddSimulatedDependencyAnalysis()` for Simulated-sourced jobs. It implements `IDependencyDiscoveryServiceFactory` and delegates to `SimulatedWorkItemLinkAnalysisService` (keyed `"Simulated"`), returning an empty link sequence without any network calls. This closes the Simulated connector gap — a full `capture.dependencies.*` → `analyse.dependencies.*` pipeline runs end-to-end without external connectivity when using `source.type: Simulated`.

**Inventory:**

A `devopsmigration queue` run with `Mode: Inventory` and `source.type: Simulated` returns per-project work item and revision counts derived directly from `generator.projects` configuration (no query windowing is needed). Output format is identical to the real ADO Services path.

**Scenario configs:**

See `/scenarios/` for ready-to-run simulated configuration files:

| File | Scenario |
|---|---|
| `export-simulated.json` | Simulated source export (25,000 work items, no external connectivity) |
| `migrate-simulated.json` | Full simulated migration — source and target both simulated (25,000 work items) |

---

## Teams Module — Board Configuration Capability

The `TeamsModule` exports and imports per-team board configuration via the
`BoardConfigTeamExtension`. This extension is registered as an `IModuleExtension`
and runs as part of the standard per-team extension loop.

### What is exported

For each team, `board-config.json` is written to `Teams/{slug}/board-config.json`.
It contains:

| Field | Description |
|---|---|
| `boards[].boardName` | Board name (one per backlog level) |
| `boards[].columns` | Kanban columns with WIP limits, column types, state mappings, and split status |
| `boards[].swimLanes` | Swim lane names and IDs |
| `cardRules` | Card colour-coding rules (aggregated across all boards for the team) |
| `backlogs` | Backlog level display names and WIT category references |
| `taskboardColumns` | Sprint taskboard column names, types, and state mappings |

Each data type can be independently enabled or disabled via `BoardConfigExtensionOptions`.

### Connector coverage

| Connector | BoardConfig | Backlogs | TaskboardColumns |
|---|---|---|---|
| `AzureDevOpsServices` | ✔ | ✔ | ✔ |
| `TeamFoundationServer` | ✗ (Skipped) | ✗ (Skipped) | ✗ (Skipped) |
| `Simulated` | ✔ | ✔ | ✔ |

When the connector declares no `BoardConfig` capability, the extension emits a
`BoardConfigSkipped` progress event and returns without writing any artefact — no
error is raised.

### Import modes

Controlled by `BoardConfig.importMode`:

| Mode | Behaviour |
|---|---|
| `Replace` (default) | Overwrites target board config with package values |
| `Merge` | Merges package values with existing target (source-only entries added; target-only entries preserved) |
| `Skip` | Leaves target unchanged if it already has board config |

### Invalid state mapping filter (FR-013)

Before writing columns to the target, state mappings referencing states absent from
the current target board are silently omitted with a per-column `LogWarning`. This
prevents `400 Bad Request` errors when the target process template differs from the source.

### Configuration section

`MigrationPlatform:Modules:Teams:Extensions:BoardConfig`

```json
{
  "MigrationPlatform": {
    "Modules": {
      "Teams": {
        "Extensions": {
          "BoardConfig": {
            "Enabled": true,
            "Columns": true,
            "SwimLanes": true,
            "CardRules": true,
            "Backlogs": true,
            "TaskboardColumns": true,
            "ImportMode": "Replace"
          }
        }
      }
    }
  }
}
```

### Known Limitations

- **No mixed-mode discovery.** All organisations in a single `MigrationOptions` configuration must be the same source type. A discovery run cannot mix TFS (Team Foundation Server) and Azure DevOps Services entries. Each CLI host registers a single `IWorkItemDiscoveryService` and `IProjectDiscoveryService` implementation; the orchestrator uses whatever is injected. On-premises Azure DevOps Server instances that support the REST API should use source type `AzureDevOpsServices`.
- **Simulated source is not for production use.** It provides no guarantee of realistic data distribution beyond the configured parameters. Use it only for development, testing, and performance benchmarking.

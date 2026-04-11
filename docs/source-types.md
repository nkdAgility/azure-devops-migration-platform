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
- PAT or service principal authentication.
- Must respect `policies.throttle.maxConcurrency` to avoid hitting rate limits.
- `apiVersion` must be pinned; do not use auto-negotiation in production runs.
- All REST responses must be validated against expected schema before being written to the package.

**Inventory:**

The `devopsmigration discovery inventory` command uses the REST API directly for ADO Services:

- Date-windowed WIQL queries via `WorkItemTrackingHttpClient.QueryByWiqlAsync`.
- Initial window: 120 days. Halves if result ≥ 20,000 items; grows by 1 day after narrow success.
- PAT authentication via `VssBasicCredential`; configured by `source.authentication.accessToken` (supports `$ENV:VARNAME` resolution).

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

- The .NET 10 host cannot call TFS Object Model APIs directly.
- The export MUST be delegated to `TfsExportCommand` in `DevOpsMigrationPlatform.CLI.Migration`, which uses `ExternalToolRunner` to spawn the `DevOpsMigrationPlatform.CLI.TfsMigration` subprocess (built against .NET 4.8), and `TfsExporterProcessAdapter` to translate the subprocess's NDJSON stdout into progress events. These are the only permitted TFS-aware .NET 10 constructs.
- The process bridge protocol is:
  - **stdin** — `TfsExportRequest` as UTF-8 JSON (includes credentials; never via CLI args)
  - **stdout** — NDJSON progress lines consumed by the adapter and forwarded to `IProgressSink`
  - **stderr** — unstructured error detail captured for failure logging
  - **exit code** — 0 for success, 1–5 for specific failure categories
  - **cancellation** — adapter writes a sentinel file; subprocess polls and aborts gracefully
- The subprocess writes package files and cursor checkpoints to `packageRootPath`.
- The adapter validates and normalises the exporter's output before treating it as canonical package data.
- If normalisation is required (e.g., field name casing differences), it is applied transparently.

See [docs/tfs-exporter.md](tfs-exporter.md) for the complete process bridge specification.

**Inventory:**

For TFC/Azure DevOps Server source types, the `devopsmigration discovery inventory` command delegates to the `tfsmigration.exe` subprocess via `TfsInventoryProcessAdapter`:

- The parent .NET 10 process spawns `tfsmigration.exe inventory --collection <url> [--project <name>] [--all-projects]`.
- Credentials are passed via stdin as a single JSON line: `{"pat":"<token>"}` or `{}` for Windows-integrated auth.
- The subprocess emits `InventoryProgressEvent` records as NDJSON on stdout; the parent process parses each line and drives the Spectre.Console live table.
- The TFS subprocess uses `WorkItemStoreExtensions.QueryCountAllByDateChunk` for date-windowed counting (same 120-day / 20k algorithm as the ADO Services path).

### Validation and Normalisation

Regardless of source type, data written to the package must conform to the schema versions declared in `manifest.json`. The exporter or adapter layer is responsible for:

1. Validating its own output before writing.
2. Emitting a validation report to `Logs/` if any anomalies are found.
3. Failing fast if required fields are absent.

The control plane performs a secondary validation pass before beginning import when running in `Both` mode.

### Simulated

```json
"source": {
  "type": "Simulated",
  "seed": 42,
  "workItemCount": 25000,
  "projectCount": 1,
  "workItemTypeDistribution": {
    "Bug": 40,
    "Task": 40,
    "User Story": 20
  },
  "avgRevisionsPerItem": 3,
  "includeAttachments": false,
  "includeLinks": true
}
```

**Requirements:**

- Intended for **testing and development only**. No real data is read from or written to an external server.
- Generates work items deterministically: given the same `seed` and `workItemCount`, every run produces identical work item identifiers, field values, revision counts, and link structures.
- Simulated work item field values are prefixed with `[SIMULATED]` so a package produced by simulation cannot be mistaken for a real export.
- Plugs into the existing module architecture as a standard `IDataTypeModule` export implementation — the same `IArtefactStore` path as real sources.
- When `seed` is omitted, a random seed is chosen automatically, logged at `Information` level, and recorded in `manifest.json` so the run is reproducible.
- Identity mapping still runs for simulated migrations: the simulated source generates a fixed set of synthetic user identities and the `IdentitiesModule` processes them in the normal order.

**Simulated target configuration** is also supported for full end-to-end testing without a live Azure DevOps organisation:

```json
"target": {
  "type": "Simulated",
  "validateOnWrite": true,
  "failOnFirstError": true
}
```

The simulated target accepts all work items presented during import without writing to any external system. It validates each revision against the package schema as it arrives (when `validateOnWrite: true`).

**Inventory:**

The `devopsmigration discovery inventory` command with `source.type: Simulated` returns per-project work item and revision counts derived directly from configuration (no query windowing is needed). Output format is identical to the real ADO Services path.

**Scenario configs:**

See `/scenarios/` for ready-to-run simulated configuration files:

| File | Scenario |
|---|---|
| `export-simulated.json` | Simulated source export (25,000 work items, no external connectivity) |
| `migrate-simulated.json` | Full simulated migration — source and target both simulated (25,000 work items) |

### Known Limitations

- **No mixed-mode discovery.** All organisations in a single `DiscoveryOptions` configuration must be the same source type. A discovery run cannot mix TFS (Team Foundation Server) and Azure DevOps Services entries. Each CLI host registers a single `IWorkItemDiscoveryService` and `IProjectDiscoveryService` implementation; the orchestrator uses whatever is injected. On-premises Azure DevOps Server instances that support the REST API should use source type `AzureDevOpsServices`.
- **Simulated source is not for production use.** It provides no guarantee of realistic data distribution beyond the configured parameters. Use it only for development, testing, and performance benchmarking.

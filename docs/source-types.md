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

- Uses the Azure DevOps REST API natively from .NET 9.
- PAT or service principal authentication.
- Must respect `policies.throttle.maxConcurrency` to avoid hitting rate limits.
- `apiVersion` must be pinned; do not use auto-negotiation in production runs.
- All REST responses must be validated against expected schema before being written to the package.

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
- The export MUST be delegated to `ITfsExporterAdapter`, implemented by `TfsExporterProcessAdapter`, which spawns the `DevOpsMigrationPlatform.TfsExporter` subprocess (built against .NET 4.8).
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

### Validation and Normalisation

Regardless of source type, data written to the package must conform to the schema versions declared in `manifest.json`. The exporter or adapter layer is responsible for:

1. Validating its own output before writing.
2. Emitting a validation report to `Logs/` if any anomalies are found.
3. Failing fast if required fields are absent.

The control plane performs a secondary validation pass before beginning import when running in `Both` mode.

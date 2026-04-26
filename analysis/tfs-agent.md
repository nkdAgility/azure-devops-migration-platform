# Viability Analysis: TfsMigrationAgent via Control Plane

> **Status**: Draft — iterating  
> **Date**: 2026-04-26  
> **Context**: Assess converting `DevOpsMigrationPlatform.CLI.TfsMigration` from a CLI-spawned subprocess into a `TfsMigrationAgent` managed by the Control Plane, symmetric with `MigrationAgent`.

---

## Current Architecture

`CLI.TfsMigration` is a standalone .NET 4.8.1 console application with two subcommands (`export`, `inventory`). It is spawned as a **subprocess** by the .NET 10 Migration CLI — never called in-process.

### Call Chain (Export)

```
QueueCommand.ExecuteExportAsync()
  └─ if source.Type == "TeamFoundationServer"
       └─ TfsExportRunner.RunAsync()
            └─ ExternalToolRunner.RunWithStreamingAsync("tfsmigration.exe", "export ...")
                 └─ spawns tfsmigration.exe as child process
                      └─ stdout NDJSON → TfsExporterProcessAdapter.OnStdoutLine() → IProgressSink
```

### Key Files (.NET 10 side)

| File | Role |
|---|---|
| `CLI.Migration/Commands/TfsExportCommand.cs` | Contains `TfsExportRunner` static helper |
| `CLI.Migration/ExternalToolRunner.cs` | Generic process bridge (spawns exe, streams stdout/stderr) |
| `CLI.Migration/TfsExporterProcessAdapter.cs` | Parses NDJSON stdout → `ProgressEvent` → `IProgressSink` |

### Key Files (.NET 4.8 side)

| File | Role |
|---|---|
| `CLI.TfsMigration/Program.cs` | CLI entry point (Spectre.Console `export` + `inventory` subcommands) |
| `CLI.TfsMigration/TfsExportAgent.cs` | Export executor using shared `IArtefactStore`, `ICheckpointingService`, `IProgressSink` |
| `CLI.TfsMigration/Commands/ExportCommand.cs` | Spectre.Console command for export |
| `CLI.TfsMigration/Commands/InventoryCommand.cs` | Spectre.Console command for inventory |

### Inventory Path (Not Wired)

`TfsInventoryProcessAdapter` is specified in spec 003 but **does not exist** in the source tree. The TFS-side `InventoryCommand` exists but is not connected to the .NET 10 CLI inventory command for `source.type == "TeamFoundationServer"`.

---

## Proposed Change

Convert the TFS binary from a CLI-spawned subprocess into a first-class agent that:

1. Polls the Control Plane for TFS-compatible jobs via `GET /agents/lease`
2. Acquires a lease, executes the export/inventory, reports progress via `POST /agents/lease/{leaseId}/progress`
3. Heartbeats, completes, or fails via the standard lease protocol
4. Is lifecycle-managed by `AgentLifecycleService` in `ControlPlaneHost`

---

## Hard Blockers

### 1. .NET Framework 4.8.1 vs Agent HTTP Client Stack

The `MigrationAgent` is a .NET 10 Worker Service using `HttpClient`, Aspire service discovery, and `IHostedService` patterns. The TFS binary **must** remain net481 (TFS Object Model is .NET Framework-only).

**Impact**: The entire agent polling/lease/heartbeat/progress protocol must be reimplemented using net481-compatible HTTP libraries (`System.Net.Http` 4.x or `HttpWebRequest`). Significant but not impossible — the protocol is REST.

### 2. No Docker/Container Support — Ever

TFS Object Model requires Windows + .NET Framework 4.8.1. It cannot run in:
- Linux containers
- Windows Nano Server containers
- .NET 10

**Impact**: `ContainerAgentLauncher` can never manage it. ACA/KEDA cannot scale it. Permanently limited to **Standalone** and **Dedicated Server** topologies via `LocalProcessAgentLauncher` only.

### 3. `AgentLifecycleService` Manages One Agent Type

Currently resolves `../MigrationAgent/DevOpsMigrationPlatform.MigrationAgent.exe` only.

**Impact**: Must either add a second sibling path (`../TfsMigrationAgent/tfsmigration.exe`) with a second spawn loop, or generalise into a multi-agent spawner.

---

## Significant Complications

### 4. Job Routing / Agent Capability Matching

`GET /agents/lease` returns any queued job to any agent. If a TFS export job gets leased to a .NET 10 `MigrationAgent`, it cannot execute it.

**Impact**: Need **job type affinity** — agents declare capabilities (e.g. `"tfs": true`), and the control plane matches jobs to capable agents. Non-trivial schema and protocol change to the lease system affecting all topologies.

### 5. Credential Flow Changes

Today: CLI → stdin → subprocess (never in args, never in job definition).

As an agent: credentials must flow through the `MigrationJob` definition (same as the .NET 10 MigrationAgent). The TFS agent would need to parse the job JSON and extract credentials at startup. This is straightforward but changes the existing stdin-based protocol.

### 6. Package URI Resolution

`MigrationAgent` resolves `packageUri` to either `FileSystemArtefactStore` or `AzureBlobArtefactStore`. The TFS agent can only use `FileSystemArtefactStore` (no Azure Blob SDK on net481).

**Impact**: If a job specifies a blob URI, the TFS agent cannot execute it. Another routing constraint layered on top of #4.

### 7. Inventory Is Not a "Job"

`tfsmigration.exe inventory` is a discovery command, not a migration job. Discovery commands don't go through the control plane — they run locally and produce output files.

**Impact**: Making inventory a control-plane-managed job is a larger design shift affecting the entire discovery command model.

---

## Benefits

| Benefit | Value |
|---|---|
| Unified job lifecycle | TFS exports appear in control plane job list with full state machine (Queued → Running → Completed), pause/resume/cancel |
| TUI visibility | TFS export progress streams to TUI via SSE, identical to ADO exports |
| Lease-based resume | Heartbeat timeout → automatic reassignment (though only on the same Windows machine) |
| Consistent telemetry | OTel metrics/traces from TFS agent flow through the same pipeline |
| Reduced CLI complexity | `TfsExportRunner`, `TfsExporterProcessAdapter`, and the entire subprocess bridge in `CLI.Migration` could be removed |

---

## Costs

| Cost | Impact |
|---|---|
| Topology restriction | TFS agent can never run in Cloud mode — permanent Windows-only, process-only |
| Job routing complexity | Control plane needs agent capability matching — affects all topologies |
| Credential model change | Must move from stdin-piped PATs to reading credentials from job definition |
| Two agent types to manage | `AgentLifecycleService` doubles in complexity; Aspire AppHost needs conditional TFS resource |
| net481 HTTP client | Must implement lease/heartbeat/progress protocol with older HTTP stack |
| Inventory doesn't fit | Discovery commands have no control plane path today |

---

## Key Tension

The `MigrationAgent` model assumes a **stateless, scalable, containerisable** worker. The TFS agent is inherently:
- **Stateful** (Windows auth / NTLM / Kerberos)
- **Non-scalable** (single machine, single TFS OM connection)
- **Non-containerisable** (.NET Framework 4.8.1, TFS OM SOAP bindings)

Forcing it into the same model creates special cases throughout the control plane (capability matching, credential flow, URI restrictions, topology limitations).

---

## Alternative: CLI-Side Progress Bridge (Lighter Option)

Keep the subprocess model but add a **CLI-side progress bridge** that POSTs `ProgressEvent` records to the control plane while the subprocess runs.

### How It Works

1. CLI creates a `MigrationJob` with `mode: "TfsExport"` and submits it to the control plane (state: `Running`, self-leased).
2. CLI spawns `tfsmigration.exe` as today via `ExternalToolRunner`.
3. `TfsExporterProcessAdapter` continues parsing NDJSON, but **also** forwards events to the control plane via `POST /agents/lease/{leaseId}/progress`.
4. On completion/failure, CLI calls `/agents/lease/{leaseId}/complete` or `/agents/lease/{leaseId}/fail`.
5. TUI sees the job with live progress, identical UX to a full agent.

### What This Gives You

- **TUI visibility** — TFS exports appear in the job list with live progress streaming
- **Job tracking** — full state machine, pause/cancel signals
- **Unified telemetry** — metrics forwarded to control plane
- **Zero changes to TFS binary** — `CLI.TfsMigration` is untouched
- **No job routing changes** — CLI self-leases the job, no capability matching needed
- **No credential model changes** — stdin flow preserved
- **No net481 HTTP client work** — all HTTP is on the .NET 10 side

### What This Doesn't Give You

- Automatic agent restart on crash (the CLI is the supervisor, same as today)
- Control-plane-initiated TFS export (an operator must run the CLI command)
- Horizontal scaling of TFS exports (irrelevant — TFS OM is single-connection anyway)

### Estimated Effort

| Change | Scope |
|---|---|
| Create job + self-lease in `TfsExportRunner` | ~50 lines in `CLI.Migration` |
| Forward progress events from `TfsExporterProcessAdapter` to control plane | ~20 lines (add `ControlPlaneProgressSink` alongside existing `IProgressSink`) |
| Complete/fail lease on exit | ~10 lines in `TfsExportRunner` |
| Same pattern for inventory (if desired) | Mirror of the above |

---

## Recommendation

The **CLI-side progress bridge** delivers ~80% of the value at ~20% of the cost. It preserves the well-suited subprocess model for TFS's constraints while adding the missing control plane visibility.

The full `TfsMigrationAgent` conversion is technically feasible but architecturally expensive and creates permanent special cases in the control plane for a component that cannot participate in the platform's scaling and containerisation model.

### Decision Matrix

| Criterion | Full TfsMigrationAgent | CLI Progress Bridge |
|---|---|---|
| TUI visibility | ✅ | ✅ |
| Job lifecycle tracking | ✅ | ✅ |
| Live progress streaming | ✅ | ✅ |
| Unified telemetry | ✅ | ✅ |
| No control plane changes | ❌ (capability routing) | ✅ |
| No credential model changes | ❌ | ✅ |
| No TFS binary changes | ❌ (major rewrite) | ✅ |
| Cloud-mode support | ❌ (never) | ❌ (never — TFS constraint) |
| Auto-restart on crash | ✅ | ❌ (CLI supervises) |
| Control-plane-initiated export | ✅ | ❌ (CLI-initiated only) |
| Effort | High | Low |

---

## Open Questions

1. Is control-plane-initiated TFS export a requirement? (If operators always run TFS exports manually from a Windows machine, the CLI bridge is sufficient.)
2. Should inventory follow the same pattern, or remain a pure local discovery command?
3. Is the future TFS Import Agent (docs/tfs-exporter.md#future-tfs-import-agent) expected to follow the same model chosen here?
4. Does the platform need to support unattended/scheduled TFS exports without a human at the CLI?

# TfsMigrationAgent: Architectural Consistency Analysis

> **Status**: Draft — iterating  
> **Date**: 2026-04-26  
> **Decision**: Convert `DevOpsMigrationPlatform.CLI.TfsMigration` into a first-class `TfsMigrationAgent` that communicates with the Control Plane via HTTP, symmetric with `MigrationAgent`.  
> **Rationale**: Architectural consistency. One agent model, one communication pattern, one lifecycle model. The TFS agent has permanent topology constraints (Windows-only, process-only) but those are caveats, not reasons to have a different architecture.

---

## Current Architecture (Before)

```
CLI (net10.0)
  ├─ QueueCommand → POST /jobs → ControlPlane → MigrationAgent (net10.0)
  │                                                └─ polls GET /agents/lease
  │                                                └─ heartbeats, progress, complete/fail
  │
  └─ TfsExportRunner → ExternalToolRunner.RunWithStreamingAsync()
                          └─ spawns tfsmigration.exe (net481) as child process
                               └─ stdout NDJSON → TfsExporterProcessAdapter → IProgressSink
                               └─ CLI supervises lifecycle, no control plane involvement
```

**Problems with this:**
- TFS exports are invisible to the control plane — no job record, no progress in TUI, no state machine
- Two completely different execution models for the same platform
- CLI contains execution supervision logic (subprocess management) that belongs in the control plane
- `TfsExporterProcessAdapter`, `ExternalToolRunner`, `TfsExportRunner` are bridge code that exists solely because of the architectural split
- The future TFS Import Agent would duplicate the same split

---

## Target Architecture (After)

```
CLI (net10.0)
  └─ QueueCommand → POST /jobs → ControlPlane
                                    ├─ MigrationAgent (net10.0) ← polls GET /agents/lease
                                    └─ TfsMigrationAgent (net481) ← polls GET /agents/lease
                                         (same protocol, same lifecycle, same state machine)
```

Both agents:
1. Poll `GET /agents/lease` for jobs
2. Acquire a lease
3. Execute the job (export, import, inventory)
4. Heartbeat via `POST /agents/lease/{leaseId}/heartbeat`
5. Report progress via `POST /agents/lease/{leaseId}/progress`
6. Signal completion via `POST /agents/lease/{leaseId}/complete` or `/fail`

**The TFS agent is just another agent.** It happens to be net481 and Windows-only, but from the control plane's perspective it's identical.

---

## What Changes

### 1. New Project: `DevOpsMigrationPlatform.TfsMigrationAgent`

Replaces `DevOpsMigrationPlatform.CLI.TfsMigration`. Same net481 target, same TFS OM dependency, but structured as a polling agent instead of a CLI.

| Aspect | Current (`CLI.TfsMigration`) | New (`TfsMigrationAgent`) |
|---|---|---|
| Entry point | Spectre.Console CLI (`export` / `inventory` subcommands) | Long-running polling loop (like `JobAgentWorker`) |
| Gets work from | CLI arguments + stdin | `GET /agents/lease` from Control Plane |
| Reports progress | NDJSON to stdout (parent parses) | `POST /agents/lease/{leaseId}/progress` to Control Plane |
| Credentials | stdin JSON from parent process | Job definition (same as MigrationAgent) |
| Lifecycle | CLI spawns, supervises, kills | `AgentLifecycleService` spawns, monitors, restarts |
| Heartbeat | None | `POST /agents/lease/{leaseId}/heartbeat` every 30s |
| State machine | None (exit code 0 or non-zero) | Queued → Leased → Running → Completed/Failed |
| Resume | CLI passes `--resume` arg | Reads cursor from package via `IStateStore` (same as MigrationAgent) |

**Internal structure mirrors MigrationAgent:**

```
DevOpsMigrationPlatform.TfsMigrationAgent/
  Program.cs                          ← net481 console app, host builder, DI setup
  TfsJobAgentWorker.cs                ← polling loop (mirror of JobAgentWorker)
  TfsMigrationAgentServiceExtensions.cs ← DI registration
  ControlPlane/
    TfsControlPlaneClient.cs          ← HTTP client for lease/heartbeat/progress (net481 HttpClient)
    TfsControlPlaneProgressSink.cs    ← IProgressSink that POSTs to control plane
  DevOpsMigrationPlatform.TfsMigrationAgent.csproj  ← net481
```

### 2. net481 HTTP Client for Control Plane Protocol

The TFS agent needs an HTTP client for the control plane protocol using net481-compatible libraries.

**What's available on net481:**
- `System.Net.Http.HttpClient` (ships with .NET 4.5+) — fully capable for REST
- `System.Text.Json` (NuGet package, already referenced by `CLI.TfsMigration`) — for serialisation
- No `IHttpClientFactory`, no Polly resilience handler

**Implementation approach:**
- Single `HttpClient` instance (net481 `HttpClient` is thread-safe, reuse recommended)
- Manual retry with exponential backoff for lease polling (mirror the 5s poll interval)
- Manual circuit-breaking is unnecessary — the control plane is on localhost in all TFS-supported topologies
- Base URL from environment variable `ControlPlane__BaseUrl` (same as MigrationAgent receives from `AgentLifecycleService`)

**Endpoints to implement:**

| Endpoint | Method | Purpose |
|---|---|---|
| `GET /agents/lease` | Poll | Acquire a job (returns 204 No Content or 200 + lease) |
| `POST /agents/lease/{leaseId}/heartbeat` | Timer | Keep lease alive |
| `POST /agents/lease/{leaseId}/progress` | Per-event | Report `ProgressEvent` |
| `POST /agents/lease/{leaseId}/metrics` | Timer | Push `JobMetrics` snapshot |
| `POST /agents/lease/{leaseId}/complete` | Terminal | Job succeeded |
| `POST /agents/lease/{leaseId}/fail` | Terminal | Job failed (with error detail) |

**Estimated code:** ~150–200 lines. This is a thin HTTP wrapper, not a framework.

### 3. Control Plane: Job Routing (Agent Capability Matching)

**The problem:** `GET /agents/lease` currently returns any queued job to any agent. A TFS export job leased to the .NET 10 MigrationAgent would fail (no TFS OM). An ADO Services export leased to the TFS agent would also fail.

**Solution: Agent self-declaration on lease request.**

The agent includes its capabilities in the lease poll:

```
GET /agents/lease?capabilities=tfs
GET /agents/lease?capabilities=ado,simulated
```

The control plane matches `source.type` in the job to agent capabilities:

| Job `source.type` | Required capability |
|---|---|
| `AzureDevOpsServices` | `ado` |
| `TeamFoundationServer` | `tfs` |
| `Simulated` | `simulated` |

**Control plane changes:**
- `GET /agents/lease` accepts `?capabilities=` query parameter (comma-separated)
- Lease assignment query adds `WHERE source_type IN (agent_capabilities)` filter
- If no capable agent is available, job stays in `Queued` (existing behaviour for no agents)

**Schema change:** The `jobs` table already stores `job_json` as JSONB. The control plane extracts `source.type` at submission time and stores it as a denormalised `source_type TEXT` column for efficient lease matching. No new table needed.

**Migration:** Add column `source_type` to `jobs` table, backfill from `job_json->'source'->>'type'`.

**Impact:** Low. The lease endpoint already filters by state. Adding a capability filter is one additional `WHERE` clause.

### 4. `AgentLifecycleService`: Multi-Agent Spawning

Currently spawns only `../MigrationAgent/DevOpsMigrationPlatform.MigrationAgent.exe`.

**Change:** Generalise to spawn multiple sibling agents from a configured list.

```csharp
// Current (single agent):
var agentPath = Path.Combine(AppContext.BaseDirectory, "..", "MigrationAgent", exeName);

// New (multi-agent):
var agents = new[]
{
    new AgentDefinition("MigrationAgent", "DevOpsMigrationPlatform.MigrationAgent"),
    new AgentDefinition("TfsMigrationAgent", "tfsmigration"),  // net481 exe
};
```

Each agent gets its own spawn + restart loop (parallel `Task`). The exponential backoff logic is unchanged per agent. On shutdown, all agent processes are killed.

**Topologies:**
- **Standalone mode** (default): `AgentLifecycleService` spawns both agents — MigrationAgent and TfsMigrationAgent — as sibling processes. The TFS agent binary is only present on Windows (`win-x64` package); on Linux/macOS, the TFS agent entry is skipped (log a note, don't error).
- **Cloud mode**: Only the MigrationAgent exists as a container; the TFS agent binary is absent and skipped.
- **Development**: Either launch both via `AgentLifecycleService` (standalone), or run the TFS agent independently via a `launch.json` profile pointed at the control plane URL.

### 5. `build.ps1`: Rename and Restructure TFS Output

**Current:**
- Published to `cli-tfs-win-x64/` staging directory
- Packaged into `TfsMigration/` subfolder inside win-x64 zip

**New:**
- Published to `tfs-agent-win-x64/` staging directory
- Packaged into `TfsMigrationAgent/` subfolder inside win-x64 zip (peer of `MigrationAgent/` and `ControlPlane/`)

The `AgentLifecycleService` resolves `../TfsMigrationAgent/tfsmigration.exe` — matching this layout.

### 6. CLI: Remove Subprocess Bridge Code

The following files/classes become **obsolete** and are deleted:

| File | What it does today | Why it's no longer needed |
|---|---|---|
| `CLI.Migration/Commands/TfsExportCommand.cs` | Contains `TfsExportRunner` — spawns subprocess, streams output | TFS exports go through control plane like everything else |
| `CLI.Migration/ExternalToolRunner.cs` | Generic process bridge | No subprocess to spawn |
| `CLI.Migration/TfsExporterProcessAdapter.cs` | Parses NDJSON stdout → ProgressEvent | TFS agent reports directly to control plane |
| `CLI.Migration/IExternalToolRunner.cs` | Interface for `ExternalToolRunner` | No longer needed |

**`QueueCommand.ExecuteExportAsync()`** simplifies to:

```csharp
// Before:
if (source.Type == "TeamFoundationServer")
    return await TfsExportRunner.RunAsync(config, ...);  // subprocess
return await ExecuteAdoExportAsync(config, ...);          // control plane job

// After:
// All source types go through the same path — POST /jobs → agent picks it up
return await SubmitJobAsync(config, ...);
```

The CLI no longer needs to know *which* agent will execute the job. It submits a job; the control plane routes it.

### 7. Job Contract: TFS Source Authentication

The job contract already supports `source.type: "TeamFoundationServer"`. The authentication model needs to support both PAT and Windows Integrated Auth:

```json
{
  "source": {
    "type": "TeamFoundationServer",
    "url": "http://tfs:8080/tfs/DefaultCollection",
    "project": "MyProject",
    "authentication": {
      "type": "PersonalAccessToken",
      "pat": "<token>"
    }
  }
}
```

For Windows Integrated Auth (NTLM/Kerberos):

```json
{
  "source": {
    "type": "TeamFoundationServer",
    "url": "http://tfs:8080/tfs/DefaultCollection",
    "project": "MyProject",
    "authentication": {
      "type": "WindowsIntegrated"
    }
  }
}
```

The TFS agent reads this from the job definition. No special credential flow needed — the PAT is in the job JSON (same as ADO Services), and Windows auth is implicit from the process identity.

### 8. Inventory: Becomes a Discovery Job

Currently, `tfsmigration.exe inventory` is a CLI subcommand, not a job. In the new model, TFS inventory becomes a `DiscoveryJob` with `source.type: "TeamFoundationServer"`, routed to the TFS agent via capability matching.

**This means:**
- `discovery inventory` for TFS sources submits a `DiscoveryJob` to the control plane (same as ADO)
- The TFS agent picks it up and runs `TfsInventoryAgent` (existing class, renamed)
- Progress streams to the control plane → TUI
- The `TfsInventoryProcessAdapter` that was never implemented becomes unnecessary

### 9. Progress Reporting: `ControlPlaneProgressSink` for net481

Replace `StdoutProgressSink` (writes NDJSON to stdout for parent process) with a new `TfsControlPlaneProgressSink` that POSTs directly to the control plane.

```csharp
// net481 implementation
public sealed class TfsControlPlaneProgressSink : IProgressSink
{
    private readonly HttpClient _client;
    private readonly string _leaseId;

    public void Emit(ProgressEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        // Fire-and-forget POST (same as net10.0 ControlPlaneProgressSink)
        _client.PostAsync($"/agents/lease/{_leaseId}/progress", content);
    }
}
```

`StdoutProgressSink` and `StdoutInventoryProgressSink` are deleted.

---

## What Stays the Same

| Component | Change? | Notes |
|---|---|---|
| `IArtefactStore` / `FileSystemArtefactStore` | No | TFS agent uses the same package write path |
| `IStateStore` / checkpoint cursors | No | Same cursor model, same resume logic |
| `IProgressSink` interface | No | New implementation (HTTP POST), same interface |
| `WorkItemExportOrchestrator` | No | TFS agent delegates to the same orchestrator |
| `IWorkItemRevisionSource` (TFS OM impl) | No | TFS-specific source, unchanged |
| `IAttachmentBinarySource` (TFS OM impl) | No | TFS-specific source, unchanged |
| `TfsExportAgent` class | Renamed | Becomes internal to `TfsMigrationAgent`, no longer a public entry point |
| `Abstractions` multi-targeting (`net481;net10.0`) | No | Already supports both runtimes |
| `Infrastructure` multi-targeting | No | Already supports both runtimes |
| `Infrastructure.TfsObjectModel` (net481 only) | No | Referenced by TFS agent, unchanged |
| Classification tree capture | No | Already implemented in `ExportCommand`, moves to `TfsJobAgentWorker` |

---

## Caveats and Permanent Constraints

### C1: Windows-Only, Process-Only

The TFS agent can never run in:
- Linux containers (TFS OM is .NET Framework, Windows-only)
- Windows Nano Server containers (TFS OM needs full .NET Framework)
- Azure Container Apps / Kubernetes / any container orchestrator

**It runs as a native Windows process.** This is a permanent constraint of the TFS Object Model, not a design limitation. The architecture accommodates it — `AgentLifecycleService` skips the TFS agent binary on non-Windows platforms and in cloud mode.

### C2: No `IHttpClientFactory` / Polly Resilience on net481

The net481 HTTP client is a plain `System.Net.Http.HttpClient`. There's no `IHttpClientFactory` and no Polly. The agent needs manual:
- Retry logic for lease polling (simple loop with `Task.Delay`)
- Timeout handling (set `HttpClient.Timeout`)
- Error handling for network failures

This is straightforward — the control plane is always on the same machine or LAN for TFS topologies. The MigrationAgent's elaborate resilience stack (30s attempt timeout, 150s total, 61s circuit breaker) is overkill here; a simple retry loop is sufficient.

### C3: No `IHostedService` / `BackgroundService` on net481

.NET Framework 4.8.1 doesn't have `Microsoft.Extensions.Hosting` built in. Two options:

**Option A — Plain console loop (simpler):**
```csharp
static async Task Main(string[] args)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var worker = new TfsJobAgentWorker(controlPlaneUrl);
    await worker.RunAsync(cts.Token);
}
```

**Option B — `Microsoft.Extensions.Hosting` via NuGet:**
net481 can consume `Microsoft.Extensions.Hosting` (targets .NET Standard 2.0). This gives `BackgroundService`, `IHostedService`, `IHost`, and `IConfiguration` — making the TFS agent structurally identical to the MigrationAgent. Worth evaluating; if it works cleanly, it significantly reduces code divergence.

### C4: OpenTelemetry on net481 — Manual Setup Required

OTel is **mandatory** — the TFS agent must emit traces, metrics, and structured logs like every other component. The OTel SDK targets .NET Standard 2.0, so it runs on net481.

The TFS agent creates a manual `TracerProvider` and `MeterProvider` at startup:

```csharp
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("TfsMigrationAgent"))
    .AddSource("DevOpsMigrationPlatform.*")
    .AddOtlpExporter()
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("TfsMigrationAgent"))
    .AddMeter("DevOpsMigrationPlatform.*")
    .AddOtlpExporter()
    .Build();
```

The OTLP endpoint is configured via `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable (set by `AgentLifecycleService` or `launch.json`). NuGet packages: `OpenTelemetry`, `OpenTelemetry.Exporter.Otlp`, `OpenTelemetry.Extensions.Hosting` (all net481-compatible).

### C5: Package URI — Filesystem Only

The TFS agent can only resolve `file:///` package URIs (`FileSystemArtefactStore`). It cannot use `AzureBlobArtefactStore` (Azure Blob SDK dependency chain is problematic on net481).

**This is fine.** TFS exports always run on a Windows machine with filesystem access to the package. A TFS source + blob package URI is an invalid combination — the control plane should reject it at submission time (Tier 0 validation: `source.type == TFS && packageUri.scheme != file → reject`).

### C6: Credential Security in `job_json`

With the subprocess model, credentials were piped via stdin — transient and never persisted. With the agent model, credentials are part of the `MigrationJob` stored in `job_json` (JSONB column in PostgreSQL).

**This is the same model as ADO Services exports** — PATs are already stored in `job_json` for ADO jobs. The control plane docs already state it "does not inspect or proxy credential values." The security posture is identical.

For Windows Integrated Auth, no credential is stored — the `authentication.type: "WindowsIntegrated"` flag tells the TFS agent to use the process identity (NTLM/Kerberos). The TFS agent process inherits the Windows credentials of whatever account runs `AgentLifecycleService`.

### C7: Discovery Jobs Need Capability Routing Too

Today, `DiscoveryJob` only has `discoveryType` (Inventory, Dependencies, Both) — it doesn't have a `source.type`. For TFS inventory to be routed to the TFS agent, `DiscoveryJob` needs a `source` field or the control plane needs another routing dimension.

**Simplest fix:** Add `source` (with `type` and `url`) to `DiscoveryJob`, matching `MigrationJob`. The capability routing then works identically for both job types.

---

## Change Summary by Component

### Control Plane (`ControlPlane` + `ControlPlaneHost`)

| Change | Effort | Risk |
|---|---|---|
| Add `source_type` column to `jobs` table | Low | EF migration |
| Add `?capabilities=` filter to `GET /agents/lease` | Low | One WHERE clause |
| Generalise `AgentLifecycleService` to multi-agent | Medium | Parallel spawn loops, per-agent skip logic |
| Add `source` field to `DiscoveryJob` schema | Low | Schema addition |
| Tier 0 validation: reject TFS + blob URI | Low | One check |

### New Project: `TfsMigrationAgent` (net481)

| Component | Effort | Notes |
|---|---|---|
| `Program.cs` (entry point + DI) | Low | Plain console app, manual DI |
| `TfsJobAgentWorker.cs` (polling loop) | Medium | Mirror `JobAgentWorker` logic in net481 |
| `TfsControlPlaneClient.cs` (HTTP) | Medium | ~150 lines, plain `HttpClient` |
| `TfsControlPlaneProgressSink.cs` | Low | ~30 lines |
| Heartbeat timer | Low | `System.Threading.Timer` |
| Migration export execution | Low | Reuse existing `TfsExportAgent` / `WorkItemExportOrchestrator` |
| Discovery/inventory execution | Low | Reuse existing `TfsInventoryAgent` |

### CLI (`CLI.Migration`)

| Change | Effort | Risk |
|---|---|---|
| Delete `TfsExportRunner` | Low | Remove file |
| Delete `ExternalToolRunner` | Low | Remove file (check no other callers) |
| Delete `TfsExporterProcessAdapter` | Low | Remove file |
| Delete `IExternalToolRunner` | Low | Remove interface |
| Simplify `QueueCommand.ExecuteExportAsync()` | Low | Remove TFS branch — all sources use same path |
| Remove `TfsInventoryProcessAdapter` references | Low | Was never implemented anyway |

### Old Project: `CLI.TfsMigration` → Deleted

The entire project is replaced by `TfsMigrationAgent`. All reusable code (TFS OM wrappers, `TfsExportAgent`, `TfsInventoryAgent`) moves to the new project.

### `build.ps1`

| Change | Effort |
|---|---|
| Rename `$CliTfsProject` → `$TfsAgentProject` | Low |
| Change publish output to `tfs-agent-win-x64/` | Low |
| Package into `TfsMigrationAgent/` subfolder (peer of `MigrationAgent/`) | Low |
| Remove `build-tfs-cli` VS Code task, add `build-tfs-agent` | Low |

### `launch.json`

| Change | Effort |
|---|---|
| Replace 3 TFS CLI profiles with 1 TFS Agent profile | Low |
| Remove `preLaunchTask: build-tfs-cli`, add `build-tfs-agent` | Low |

### Tests

| Change | Effort |
|---|---|
| Delete `TfsExportCommandTests.cs` (tests `TfsExportRunner.ResolveExePath()`) | Low |
| Add `TfsControlPlaneClient` unit tests | Medium |
| Add `TfsJobAgentWorker` polling loop tests | Medium |

### Docs

| Document | Change |
|---|---|
| `docs/tfs-exporter.md` | Major rewrite — subprocess protocol → agent protocol |
| `docs/architecture.md` | Update TFS Export Agent description |
| `docs/control-plane.md` | Add capability routing, multi-agent lifecycle |
| `docs/agent-hosting.md` | Note TFS agent as a peer |
| `.agents/guardrails/architecture-boundaries.md` | Update TFS isolation rules |
| `.agents/guardrails/coding-standards.md` | Update subprocess references |
| `.agents/context/job-lifecycle.md` | Add `source` to `DiscoveryJob` |

---

## Migration Path

### Phase 1: Control Plane Capability Routing
1. Add `source_type` column + EF migration
2. Add `?capabilities=` to lease endpoint
3. `MigrationAgent` polls with `?capabilities=ado,simulated`
4. Existing behaviour preserved — all current jobs are ADO/Simulated

### Phase 2: Build TfsMigrationAgent
1. Create project, implement polling loop + HTTP client
2. Move `TfsExportAgent`, `TfsInventoryAgent`, TFS OM wrappers from `CLI.TfsMigration`
3. Implement `TfsControlPlaneProgressSink`
4. Agent polls with `?capabilities=tfs`
5. Test standalone: agent + control plane on Windows, TFS export job

### Phase 3: Remove CLI Subprocess Bridge
1. Delete `TfsExportRunner`, `ExternalToolRunner`, `TfsExporterProcessAdapter`
2. Simplify `QueueCommand` — all source types use same submission path
3. Delete `CLI.TfsMigration` project
4. Update `build.ps1` packaging

### Phase 4: Update Docs and Guardrails
1. Rewrite `docs/tfs-exporter.md`
2. Update all architecture docs
3. Update guardrails

---

## Open Questions

1. **`ExternalToolRunner` — other callers?** Verify no other code uses `ExternalToolRunner` before deleting. The inventory subprocess adapter was never implemented, but check for any other callers.

2. **TFS Import Agent** — `docs/tfs-exporter.md` describes a future `TfsImportAgent`. With this architecture, it's trivially added: same `TfsMigrationAgent` binary, new `ImportAsync()` path, capability `tfs`, `mode: Import`. No new project, no new subprocess bridge.

3. **`Microsoft.Extensions.Hosting` on net481** — .NET Framework 4.8.1 can consume `Microsoft.Extensions.Hosting` via NuGet (targets .NET Standard 2.0). This would give `BackgroundService`, `IHostedService`, `IHost`, and `IConfiguration` on net481 — making the TFS agent structurally identical to the MigrationAgent. Worth evaluating vs. a plain console loop. If it works cleanly, it significantly reduces the code divergence.

4. **Cancellation model** — the current subprocess uses a sentinel file for cancellation. The agent model can use `POST /agents/lease/{leaseId}/heartbeat` response to signal pause/cancel (the MigrationAgent reads a signal from the heartbeat response). The sentinel file mechanism is deleted.

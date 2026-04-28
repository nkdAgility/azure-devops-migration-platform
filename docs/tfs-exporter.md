# TFS Legacy Process Bridge

## Purpose

The TFS Object Model (TFS OM) is a .NET Framework 3.x/4.x SOAP library that cannot run in .NET 9/10. The entire platform runs on .NET 10 — with one narrowly bounded exception: when the source is an on-premises Team Foundation Server, extraction must delegate to an isolated external subprocess built against .NET Framework 4.8.

> **TFS is a source-only connector.** Team Foundation Server is always the migration *origin* — it is never the migration *destination*. As a consequence, `ITeamTarget`, `IWorkItemImportTarget`, and all other target-side interfaces are **not implemented for TFS** and none are required. The `#if !NET481` guards on those interfaces reinforce this: they are unreachable from the TFS subprocess by design. This is an explicit architectural decision.

The subprocess is not a dumb pipe. It contains a `TfsExportAgent` class that is the structural parallel of the .NET 10 `MigrationAgent`: it receives a job definition, writes to the package using shared abstractions, maintains its own checkpoint cursor, and reports progress. The process boundary is the only seam — the contract is shared via multi-targeted `Abstractions`.

This document specifies the process isolation boundary, the multi-targeting strategy for shared abstractions, the communication protocol, and the adapter contract that allow the .NET 10 host to invoke the .NET 4.8 exporter safely and without any runtime coupling.

---

## Isolation Principle

```
┌─────────────────────────────────────────────────────────────────┐
│  .NET 10 Host (CLI.Migration / Migration Agent)                 │
│                                                                 │
│  TfsExportCommand (or WorkItemsModule)                          │
│       │                                                         │
│       │  calls                                                  │
│       ▼                                                         │
│  ExternalToolRunner  (DevOpsMigrationPlatform.CLI.Migration)    │
│       │  spawns subprocess via ProcessStartInfo                 │
│       │  reads stdout (NDJSON progress lines) via callback      │
│       │  reads stderr (error messages) via callback             │
│       │  returns exit code                                      │
└───────┼─────────────────────────────────────────────────────────┘
        │  process execution only — no compiled reference
┌───────▼──────────────────────────────────────────────────────────┐
│  .NET 4.8 Subprocess (DevOpsMigrationPlatform.CLI.TfsMigration)  │
│                                                                  │
│  CLI entry point (ExportCommand)                                 │
│       │  reads CLI args + stdin credentials                      │
│       │  constructs job definition                               │
│       ▼                                                          │
│  TfsExportAgent                                                  │
│       ├─ IWorkItemExportService  (TFS OM → IArtefactStore)       │
│       ├─ IArtefactStore          (FileSystemArtefactStore, net481)│
│       ├─ IStateStore             (cursor checkpoint / resume)    │
│       └─ IProgressSink           (StdoutProgressSink → NDJSON)   │
│                                                                  │
│  Exits 0 (success) or non-zero (failure)                         │
└──────────────────────────────────────────────────────────────────┘
```

The .NET 10 host has **no compiled reference** to the .NET 4.8 project. Coupling exists only through: CLI arguments (non-sensitive config), stdin JSON (credentials), NDJSON stdout lines (progress), and the package files written to disk by `TfsExportAgent`.

---

## Projects

| Project | Target Frameworks | Role |
|---|---|---|
| `DevOpsMigrationPlatform.Abstractions` | `net481;net10.0` | Shared interfaces and models — compiles for both runtimes |
| `DevOpsMigrationPlatform.Infrastructure` | `net481;net10.0` | Shared infrastructure (file-based artefact store, utilities) — compiles for both runtimes |
| `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` | `net481` | TFS Object Model host — `IWorkItemExportService`, TFS OM service classes. net481 only, never referenced by any net10.0 project. |
| `DevOpsMigrationPlatform.CLI.TfsMigration` | `net481` | CLI entry point + `TfsExportAgent` — the .NET 4.8 export executor |
| `DevOpsMigrationPlatform.CLI.Migration` | `net10.0` | Main CLI — contains `ExternalToolRunner`, the only permitted caller of `tfsmigration.exe` |

### Why Multi-Targeting for Abstractions?

`DevOpsMigrationPlatform.Abstractions` and `DevOpsMigrationPlatform.Infrastructure` target both `net481` and `net10.0`. This is the key to safe code sharing:

- The **`TfsExportAgent`** (net481) references `Abstractions` compiled for `net481` — it uses `IWorkItemExportService`, `IArtefactStore`, `IStateStore`, `IProgressSink`, `MigrationWorkItemRevision`, cursor models, etc. natively.
- The **`MigrationAgent`** (net10.0) references `Abstractions` compiled for `net10.0` — same types, same contracts.
- There is **no runtime coupling**: neither binary references the other project's DLL at runtime. They share source-level contracts only.

Multi-targeting is handled via `<TargetFrameworks>net481;net10.0</TargetFrameworks>` in the project file.

The `DevOpsMigrationPlatform.CLI.TfsMigration` project MUST NOT be referenced by any .NET 10 project. It is built and deployed as a separate binary.

---

## Shared Abstractions Design

All types that cross the process boundary (models written to the package on disk, and types used in progress reporting) are defined in `DevOpsMigrationPlatform.Abstractions` targeting both frameworks.

Key shared types:

| Type | Purpose |
|---|---|
| `IArtefactStore` | Package read/write abstraction — used by both `MigrationAgent` and `TfsExportAgent`. `IAsyncEnumerable<T>` satisfied via `Microsoft.Bcl.AsyncInterfaces` on net481. |
| `IStateStore` | Cursor checkpoint abstraction — used by both executors for resume. |
| `IProgressSink` | Progress event abstraction. `StdoutProgressSink` (net481) writes NDJSON to stdout; `ControlPlaneProgressSink` (net10.0) reports to the control plane. |
| `MigrationWorkItemRevision` | Canonical work item revision model written to `revision.json` |
| `MigrationWorkItemField` | Field value in a revision |
| `MigrationWorkItemRelatedLink` | Related link in a revision |
| `MigrationWorkItemExternalLink` | External link in a revision |
| `MigrationWorkItemAttachment` | Attachment descriptor |
| `WorkItemMigrationProgress` | Progress event emitted per work item processed |

Types that are NOT shared (net10.0 host only):

- `AzureBlobArtefactStore` — Azure Blob SDK not available for net481; the subprocess always uses `FileSystemArtefactStore`

### Executor Symmetry

The two executors use the same abstractions. The process boundary and the blob store are the only differences:

| Executor | Runtime | Package writes | Progress reporting | Checkpoint |
|---|---|---|---|---|
| `MigrationAgent` | net10.0 | `IArtefactStore` (`FileSystemArtefactStore` or `AzureBlobArtefactStore`) | `IProgressSink` | `IStateStore` |
| `TfsExportAgent` | net481 | `IArtefactStore` (`FileSystemArtefactStore` only) | `IProgressSink` (`StdoutProgressSink` → NDJSON) | `IStateStore` |

`IArtefactStore`, `IStateStore`, and `IProgressSink` are all defined in `DevOpsMigrationPlatform.Abstractions` (multi-targeted `net481;net10.0`). The `IAsyncEnumerable<T>` dependency in `IArtefactStore` is satisfied on net481 via the `Microsoft.Bcl.AsyncInterfaces` NuGet package.

---

## ExternalToolRunner

`ExternalToolRunner` is the generic subprocess wrapper in `DevOpsMigrationPlatform.CLI.Migration` that spawns an external executable and streams its output. It has no knowledge of TFS — it is a general-purpose process bridge:

```csharp
public class ExternalToolRunner
{
    public static async Task<int> RunWithStreamingAsync(
        string exePath,
        string arguments,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        string? stdinJson = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinJson != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) onError?.Invoke(e.Data);
        };

        process.Start();

        // Write credentials to stdin, then close to signal end-of-input
        if (stdinJson != null)
        {
            await process.StandardInput.WriteAsync(stdinJson);
            process.StandardInput.Close();
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
```

The caller passes an `onOutput` callback. `TfsExporterProcessAdapter` in `CLI.Migration` wraps `ExternalToolRunner` and is responsible for parsing raw stdout lines as NDJSON `ProgressEvent` objects and forwarding them to the CLI's `IProgressSink` pipeline. `TfsExporterProcessAdapter` is the only permitted TFS-aware .NET 10 class.

### TfsExportRequest

```csharp
public sealed record TfsExportRequest
{
    /// <summary>Collection URL, e.g. http://tfs.internal:8080/tfs/DefaultCollection</summary>
    public required string CollectionUrl { get; init; }

    /// <summary>Project name.</summary>
    public required string Project { get; init; }

    /// <summary>Output (package root) path.</summary>
    public required string OutputPath { get; init; }

    /// <summary>WIQL scope query for work item export.</summary>
    public required string ScopeQuery { get; init; }

    /// <summary>Whether to include all revisions (true) or latest only (false).</summary>
    public required bool IncludeRevisions { get; init; }

    /// <summary>Whether to include links between work items.</summary>
    public required bool IncludeLinks { get; init; }

    /// <summary>Whether to include attachment files.</summary>
    public required bool IncludeAttachments { get; init; }

    /// <summary>
    /// Credentials — passed via stdin JSON only, never as CLI arguments.
    /// Null if using integrated Windows authentication (NTLM/Kerberos).
    /// </summary>
    public TfsCredentials? Credentials { get; init; }

    /// <summary>Path to the cancellation sentinel file. Subprocess polls and aborts when this file exists.</summary>
    public required string CancellationSentinelPath { get; init; }

    /// <summary>Resume position from a previous run. Null means start from the beginning.</summary>
    public string? ResumeFromCursor { get; init; }
}

public sealed record TfsCredentials
{
    public required string PersonalAccessToken { get; init; }
}
```

---

## Communication Protocol

### Step 1 — Non-sensitive config via CLI arguments

The adapter passes non-sensitive, non-secret parameters as command-line arguments to the subprocess. These are visible in process lists and must never include credentials:

```
TfsExport.exe export \
  --tfsserver http://tfs.internal:8080/tfs/DefaultCollection \
  --project MyProject \
  --output D:\exports\run-001 \
  --query "SELECT [System.Id] FROM WorkItems WHERE ..."
```

### Step 2 — Credentials via stdin

If credentials are required (PAT authentication), the adapter serialises them as JSON to the subprocess's standard input **after** process start, then closes stdin. The subprocess reads stdin before connecting to TFS.

```json
{ "personalAccessToken": "..." }
```

Credentials MUST NOT appear in CLI arguments — they are visible in process listings and event logs.

For integrated Windows authentication (NTLM/Kerberos), credentials are omitted entirely and the subprocess inherits the process identity.

### Step 3 — Progress via stdout (NDJSON)

The subprocess writes one JSON object per line to stdout as events occur. The adapter reads these asynchronously and converts them to `IProgressSink` events:

```json
{"type":"Started","module":"WorkItems","message":"Beginning export","timestamp":"2026-01-15T10:00:00Z"}
{"type":"Progress","module":"WorkItems","cursor":"WorkItems/2026-01-15/1234567890-42-0","processed":1,"total":500,"timestamp":"2026-01-15T10:00:01Z"}
{"type":"Progress","module":"WorkItems","cursor":"WorkItems/2026-01-15/1234567891-43-0","processed":2,"total":500,"timestamp":"2026-01-15T10:00:02Z"}
{"type":"Completed","module":"WorkItems","processed":500,"total":500,"timestamp":"2026-01-15T10:00:45Z"}
```

#### Stdout Message Types

| `type` | Required Fields | Description |
|---|---|---|
| `Started` | `module`, `message`, `timestamp` | Subprocess initialised and beginning work |
| `Progress` | `module`, `cursor`, `processed`, `total`, `timestamp` | One revision folder written; `cursor` is the relative path |
| `Warning` | `module`, `message`, `timestamp` | Non-fatal condition |
| `Completed` | `module`, `processed`, `total`, `timestamp` | All work done |
| `Failed` | `module`, `message`, `errorCode`, `timestamp` | Fatal error; subprocess will exit non-zero |

### Step 4 — Errors via stderr

The subprocess writes unstructured error detail to stderr. The adapter captures and logs it. Stderr is never parsed for progress — diagnostic output only.

### Step 5 — Exit code

| Exit Code | Meaning |
|---|---|
| `0` | Export completed successfully |
| `1` | General failure (details in stderr) |
| `2` | Cancelled (sentinel file detected) |
| `3` | Config / argument parse error |
| `4` | TFS connectivity failure |
| `5` | Package write failure |

---

## Cancellation

The adapter MUST NOT kill the subprocess directly. Instead:

1. When `CancellationToken` fires, the adapter writes a sentinel file at `CancellationSentinelPath`.
2. The subprocess polls for this file at regular intervals (≤ 1 second).
3. On detecting the file, the subprocess writes its current cursor to `.migration/Checkpoints/TfsExporter.cursor`, writes a `Failed` stdout line with `errorCode: "Cancelled"`, and exits with code `2`.
4. The adapter deletes the sentinel file after the subprocess exits.

This gives the subprocess a chance to flush its current cursor before stopping, enabling clean resume.

---

## Resume and Cursor Handoff

The subprocess writes a cursor file `.migration/Checkpoints/TfsExporter.cursor` inside the package after each revision folder is written. When the Job Engine resumes a TFS export job:

1. The adapter reads the cursor file and passes its value as `resumeFromCursor` in `TfsExportRequest`.
2. The adapter passes this to the subprocess via a `--resume` CLI argument (the cursor value contains no credentials).
3. The subprocess skips already-written revision folders and resumes from the cursor.

---

## Output Validation and Normalisation

After the subprocess exits with code `0`, the adapter MUST:

1. **Validate** that written revision folders conform to the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` layout.
2. **Validate** that each `revision.json` parses as valid JSON with required fields.
3. **Normalise** any field casing differences between TFS OM output and the canonical package schema.
4. **Reject** the export and fail the job if validation fails — do not proceed to import.

Validation uses the same package validation logic used by the `validate` command.

---

## Subprocess Binary Location

The TFS exporter binary path is resolved from configuration. It must never be auto-discovered or hardcoded to a development path in production:

```json
{
  "tfsExporter": {
    "executablePath": "tools/tfs-exporter/TfsExport.exe"
  }
}
```

The path may be absolute or relative to the working directory. The host fails fast if the binary is missing before attempting any export.

**MUST NOT:**
- Embed the .NET 4.8 binary as a resource in the .NET 10 host
- Auto-discover or probe for the binary in `PATH`
- Use a hardcoded relative development path (e.g., `..\..\bin\Debug\...`) except in local dev configuration

---

## Security Controls

- The subprocess inherits no ambient credentials from the .NET 10 host process environment.
- Credentials are passed via **stdin JSON only** — never via CLI arguments (visible in process listings and OS event logs).
- The subprocess MUST redact credential fields before writing any log or stderr output.
- The cancellation sentinel file path must be within the package directory — never a shared system temp path.
- The subprocess MUST NOT write any data outside `--output` path.

---

## Telemetry

The TFS subprocess has a full OpenTelemetry pipeline configured in `MigrationPlatformHost.CreateDefaultBuilder()`. This means all `System.Diagnostics.Metrics` instruments — including discovery metrics (`IDiscoveryMetrics`) and work item export metrics (`IWorkItemExportMetrics`, `IAttachmentDownloadMetrics`) — are recorded on the net481 runtime.

### Metric Export Paths

| Exporter | Activation | Configuration |
|---|---|---|
| **Azure Monitor** | Always (when connection string present) | `Telemetry:AzureMonitorConnectionString` in `appsettings.json` |
| **OTLP** | Opt-in | `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable |

Both exporters are registered for traces and metrics. When neither is configured, the OTel SDK instruments are still recorded but silently discarded (zero overhead).

### Registered Meters

| Meter | Instruments |
|---|---|
| `DevOpsMigrationPlatform.WorkItemExport` | Work item, revision, and link export counters and durations |
| `DevOpsMigrationPlatform.AttachmentDownload` | Attachment download counters and durations |
| `DevOpsMigrationPlatform.Discovery` | Organisation, project, inventory, dependency, and checkpoint counters and durations |

### Traces

Activity sources registered: `WorkItemExport`, `AttachmentDownload`.

### Serilog Integration

Logs are forwarded to the OTLP collector via `Serilog.Sinks.OpenTelemetry` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. File sinks (`.migration/Logs/`) are always active.

### Package Dependencies

The `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` project (net481) directly references `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`, and `Azure.Monitor.OpenTelemetry.Exporter`. The multi-targeted `Abstractions` and `Infrastructure` projects use `System.Diagnostics.DiagnosticSource` (provides `TagList`, `Meter`, etc.) on net481.

### NDJSON Bridge (Secondary Path)

In addition to direct OTel export, `MetricSnapshot` values are embedded in NDJSON `ProgressEvent` lines on stdout. The .NET 10 host reads these via `TfsExporterProcessAdapter` and forwards them to the CLI's progress pipeline. This provides in-process visibility even when no external collector is attached.

---

## CLI.TfsMigration Subprocess Contract (summary)

The `DevOpsMigrationPlatform.CLI.TfsMigration` project (net481) MUST:

- Accept non-sensitive config via CLI arguments (`--tfsserver`, `--project`, `--output`, `--query`, `--resume`).
- Read credentials from stdin as UTF-8 JSON before making any TFS connection.
- Write NDJSON progress lines to stdout, flushed after each line.
- Write error detail to stderr.
- Write package files to `--output` following canonical layouts.
- Write cursor to `.migration/Checkpoints/TfsExporter.cursor` after each revision folder.
- Poll the cancellation sentinel file and abort gracefully when it appears.
- Exit with the appropriate exit code.

The `DevOpsMigrationPlatform.CLI.TfsMigration` project MUST NOT:

- Be referenced by any .NET 10 project (no `<ProjectReference>` in any net10.0 project).
- Accept credentials via CLI arguments.
- Write to any path outside `--output`.
- Call the Control Plane or any API other than TFS OM.

---

---

## Future: TFS Import Agent

> **Not yet implemented.** This section documents the intended design so that a future implementer can build the TFS import capability without any structural rework to the platform.

Writing to an on-premises Team Foundation Server from the package faces the same .NET runtime constraint as reading from one: the TFS Object Model is a .NET Framework 3.x/4.x SOAP library that cannot run in .NET 10. The solution is the exact mirror of the exporter pattern.

### Isolation Principle (Import)

```
┌─────────────────────────────────────────────────────────────────┐
│  .NET 10 Host (Migration Agent)                                 │
│                                                                 │
│  WorkItemsModule                                                │
│       │                                                         │
│       │  calls                                                  │
│       ▼                                                         │
│  ITfsImporterAdapter                                            │
│       (interface in Abstractions — compiled for net10.0)        │
│                                                                 │
│  ExternalToolRunner  (CLI.Migration)                            │
│       │  spawns subprocess via ProcessStartInfo                 │
│       │  reads stdout (NDJSON progress lines) via callback      │
│       │  reads stderr (error messages) via callback             │
│       │  returns exit code                                      │
└───────┼─────────────────────────────────────────────────────────┘
        │  process execution only — no compiled reference
┌───────▼──────────────────────────────────────────────────────────┐
│  .NET 4.8 Subprocess (DevOpsMigrationPlatform.CLI.TfsMigration)  │
│                                                                  │
│  CLI entry point (ImportCommand)                                 │
│       │  reads CLI args + stdin credentials                      │
│       │  constructs job definition                               │
│       ▼                                                          │
│  TfsImportAgent                                                  │
│       ├─ IWorkItemImportService  (IArtefactStore → TFS OM)       │
│       ├─ IArtefactStore          (FileSystemArtefactStore, net481)│
│       ├─ IStateStore             (cursor checkpoint / resume)    │
│       └─ IProgressSink           (StdoutProgressSink → NDJSON)   │
│                                                                  │
│  Exits 0 (success) or non-zero (failure)                         │
└──────────────────────────────────────────────────────────────────┘
```

### Projects Required (when built)

No new projects are needed. All changes are additive to existing projects:

| Project | Change Required |
|---|---|
| `DevOpsMigrationPlatform.Abstractions` | Add `TfsImportRequest`, `IWorkItemImportService` |
| `DevOpsMigrationPlatform.CLI.Migration` | No change — `ExternalToolRunner` is already the generic subprocess bridge |
| `DevOpsMigrationPlatform.CLI.TfsMigration` | Add `ImportCommand` entry point + `TfsImportAgent` class |

`ExternalToolRunner`, the NDJSON stdout protocol, the stdin-credentials pattern, and the sentinel-file cancellation mechanism are **all reused without modification**.

### Intended Interface

```csharp
/// <summary>
/// Runs the .NET Framework TFS importer as an isolated subprocess and
/// streams progress events back to the caller.
/// </summary>
public interface ITfsImporterAdapter
{
    Task ImportAsync(TfsImportRequest request, IProgressSink progressSink, CancellationToken ct);
}

public sealed record TfsImportRequest
{
    /// <summary>Collection URL, e.g. http://tfs.internal:8080/tfs/DefaultCollection</summary>
    public required string CollectionUrl { get; init; }

    /// <summary>Project name.</summary>
    public required string Project { get; init; }

    /// <summary>Input (package root) path.</summary>
    public required string InputPath { get; init; }

    /// <summary>Whether to import links between work items.</summary>
    public required bool IncludeLinks { get; init; }

    /// <summary>Whether to import attachment files.</summary>
    public required bool IncludeAttachments { get; init; }

    /// <summary>
    /// Credentials — passed via stdin JSON only, never as CLI arguments.
    /// Null if using integrated Windows authentication (NTLM/Kerberos).
    /// </summary>
    public TfsCredentials? Credentials { get; init; }

    /// <summary>Path to the cancellation sentinel file.</summary>
    public required string CancellationSentinelPath { get; init; }

    /// <summary>Resume cursor from a previous run. Null means start from the beginning.</summary>
    public string? ResumeFromCursor { get; init; }
}
```

### Executor Symmetry (Export vs Import)

| Executor | Direction | Package access | TFS OM direction | Checkpoint file |
|---|---|---|---|---|
| `TfsExportAgent` | TFS → Package | Write via `IArtefactStore` | Read (TFS OM queries) | `.migration/Checkpoints/TfsExporter.cursor` |
| `TfsImportAgent` *(future)* | Package → TFS | Read via `IArtefactStore` | Write (TFS OM mutations) | `.migration/Checkpoints/TfsImporter.cursor` |

Both executors use the same `IArtefactStore`, `IStateStore`, `IProgressSink`, and NDJSON stdout protocol. The only substantive difference is TFS OM read vs write.

### Constraints (inherited from the exporter pattern)

The `TfsImportAgent` MUST:

- Accept non-sensitive config via CLI arguments (`--tfsserver`, `--project`, `--input`, `--resume`).
- Read credentials from stdin as UTF-8 JSON before making any TFS connection.
- Write NDJSON progress lines to stdout, flushed after each line.
- Write error detail to stderr.
- Read package files from `--input` following canonical layouts (streaming — never load all revisions into memory).
- Write cursor to `.migration/Checkpoints/TfsImporter.cursor` after each revision applied.
- Poll the cancellation sentinel file and abort gracefully when it appears.
- Exit with the same exit code scheme as the exporter.

The `TfsImportAgent` MUST NOT:

- Be referenced by any .NET 10 project.
- Accept credentials via CLI arguments.
- Read from any path outside `--input`.
- Call the Control Plane or any API other than TFS OM.
- Depend on any .NET 10-only packages.

---

## Inventory Mode

`DevOpsMigrationPlatform.CLI.TfsMigration` also accepts the `inventory` subcommand for work-item counting. It is invoked by `TfsInventoryProcessAdapter` in the .NET 10 host whenever `source.type == "TeamFoundationServer"`.

### Subcommand

```
tfsmigration.exe inventory --collection <url> [--project <name>] [--all-projects]
```

| Argument | Required | Description |
|---|---|---|
| `--collection <url>` | Yes | TFS collection URL (e.g. `http://tfs:8080/tfs/DefaultCollection`) |
| `--project <name>` | One of | Inventory a single named project |
| `--all-projects` | One of | Inventory all projects in the collection; mutually exclusive with `--project` |

### Credentials

Credentials are passed via a single JSON line on stdin immediately after the subprocess starts:

```json
{"pat":"<personal-access-token>"}
```

For Windows-integrated auth, pass an empty object or omit the `pat` property:

```json
{}
```

The subprocess reads exactly one line from stdin before making any TFS connection. Credentials **never** appear in CLI arguments.

### NDJSON Progress Output

The subprocess emits `InventoryProgressEvent` records as NDJSON on stdout — one JSON object per line, flushed immediately:

```json
{"projectName":"Alpha","url":"http://tfs:8080/tfs/DefaultCollection","workItemsCount":1500,"revisionsCount":0,"isComplete":false,"windowSize":"120.00:00:00","timestamp":"2026-01-15T10:00:01Z"}
{"projectName":"Alpha","url":"http://tfs:8080/tfs/DefaultCollection","workItemsCount":3200,"revisionsCount":0,"isComplete":true,"timestamp":"2026-01-15T10:00:05Z"}
```

Intermediate events have `isComplete: false`; the final event for each project has `isComplete: true`. Error events have `isComplete: true` and a non-null `error` field.

### Exit Codes

| Exit Code | Meaning |
|---|---|
| `0` | Inventory completed (per-project errors are emitted as NDJSON error events) |
| Non-zero | Fatal failure before any events could be emitted |

### Implementation Notes

`InventoryCommand` resolves `IWorkItemDiscoveryService` (backed by `TfsObjectModelWorkItemDiscoveryService`) from the DI host. It uses `CountWorkItemsAsync` with `TfsWorkItemQueryWindowStrategy` for date-windowed counting — the same 120-day initial window and 20,000-item halving algorithm as the ADO Services path. Each project is wrapped in an individual try/catch so a failure in one project emits an error event without aborting the remaining projects.

---

## See Also

- [Source Types](source-types.md) — `TeamFoundationServer` config schema
- [Artefact Store](../.agents/context/artefact-store.md) — Package file abstraction
- [Checkpointing](../.agents/context/checkpointing.md) — Cursor model
- [Validation](validation.md) — Post-export validation
- [.agents/guardrails/coding-standards.md](../.agents/guardrails/coding-standards.md) — .NET runtime isolation rules

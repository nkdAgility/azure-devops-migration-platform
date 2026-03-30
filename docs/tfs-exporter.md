# TFS Legacy Process Bridge

## Purpose

The TFS Object Model (TFS OM) is a .NET Framework 3.x/4.x SOAP library that cannot run in .NET 9/10. The entire platform runs on .NET 10 — with one narrowly bounded exception: when the source is an on-premises Team Foundation Server, extraction must delegate to an isolated external subprocess built against .NET Framework 4.x.

This document specifies the process isolation boundary, communication protocol, and adapter contract that allow the .NET 10 host to invoke the .NET 4 exporter safely, reliably, and without any runtime coupling.

---

## Isolation Principle

```
┌─────────────────────────────────────────────────────────────────┐
│  .NET 10 Host (Migration Agent / LocalJobRunner)                │
│                                                                 │
│  WorkItemsModule                                                │
│       │                                                         │
│       │  calls                                                  │
│       ▼                                                         │
│  ITfsExporterAdapter  ──────────────────────────────────────┐  │
│       (interface; defined in Abstractions)                  │  │
│                                                             │  │
│  TfsExporterProcessAdapter  (Infrastructure.TfsLegacy)      │  │
│       │  spawns subprocess                                  │  │
│       │  reads stdout (JSON progress lines)                 │  │
│       │  reads stderr (error messages)                      │  │
│       │  passes cancellation via cancel-token file          │  │
│       └──────────────────────────────────────────────────┐  │  │
└──────────────────────────────────────────────────────────│──┘  │
                                                           │      │
┌──────────────────────────────────────────────────────────│──────┘
│  .NET 4.x Subprocess (DevOpsMigrationPlatform.TfsExporter)│
│                                                           │
│       Reads config from stdin (JSON)                      │
│       Writes package files to path in config              │
│       Writes structured progress lines to stdout          │
│       Writes error details to stderr                      │
│       Exits 0 (success) or non-zero (failure)             │
└───────────────────────────────────────────────────────────┘
```

The .NET 10 host has **no compiled reference** to the .NET 4 project. The subprocess is an independent binary. The only coupling is the JSON message format on stdin/stdout and the package files written to disk.

---

## Projects

| Project | Runtime | Role |
|---|---|---|
| `DevOpsMigrationPlatform.Abstractions` | .NET 10 | Defines `ITfsExporterAdapter` interface |
| `DevOpsMigrationPlatform.Infrastructure.TfsLegacy` | .NET 10 | `TfsExporterProcessAdapter` — spawns subprocess, reads output |
| `DevOpsMigrationPlatform.TfsExporter` | .NET 4.8 | Standalone executable that calls the TFS Object Model |

The `DevOpsMigrationPlatform.TfsExporter` project MUST NOT be referenced by any .NET 10 project. It is built and deployed as a separate binary.

---

## ITfsExporterAdapter Interface

```csharp
/// <summary>
/// Runs the .NET Framework TFS exporter as an isolated subprocess and
/// streams progress events back to the caller.
/// </summary>
public interface ITfsExporterAdapter
{
    /// <summary>
    /// Invokes the TFS exporter for a single scope and streams progress.
    /// </summary>
    /// <param name="request">Export parameters serialised to the subprocess.</param>
    /// <param name="progressSink">Receives progress events emitted by the subprocess.</param>
    /// <param name="ct">Signals the subprocess to abort via a cancellation token file.</param>
    Task ExportAsync(TfsExportRequest request, IProgressSink progressSink, CancellationToken ct);
}
```

### TfsExportRequest

```csharp
public sealed record TfsExportRequest
{
    /// <summary>Collection URL, e.g. http://tfs.internal:8080/tfs/DefaultCollection</summary>
    public required string CollectionUrl { get; init; }

    /// <summary>Project name.</summary>
    public required string Project { get; init; }

    /// <summary>TFS REST API version pinned in config (e.g. "15.0").</summary>
    public required string ApiVersion { get; init; }

    /// <summary>WIQL scope query for work item export.</summary>
    public required string ScopeQuery { get; init; }

    /// <summary>Package root path (file:///).</summary>
    public required string PackageRootPath { get; init; }

    /// <summary>Whether to include all revisions (true) or latest only (false).</summary>
    public required bool IncludeRevisions { get; init; }

    /// <summary>Whether to include links between work items.</summary>
    public required bool IncludeLinks { get; init; }

    /// <summary>Whether to include attachment files.</summary>
    public required bool IncludeAttachments { get; init; }

    /// <summary>Path to the cancellation token file. If this file exists, the subprocess MUST abort.</summary>
    public required string CancellationTokenFilePath { get; init; }
}
```

---

## Communication Protocol

### Step 1 — Config via Stdin

The .NET 10 adapter serialises `TfsExportRequest` to JSON and writes it to the subprocess's standard input, then closes the stdin stream to signal end-of-input.

```json
{
  "collectionUrl": "http://tfs.internal:8080/tfs/DefaultCollection",
  "project": "MyProject",
  "apiVersion": "15.0",
  "scopeQuery": "SELECT [System.Id] FROM WorkItems WHERE ...",
  "packageRootPath": "D:\\exports\\run-001",
  "includeRevisions": true,
  "includeLinks": true,
  "includeAttachments": true,
  "cancellationTokenFilePath": "D:\\exports\\run-001\\cancel.tmp"
}
```

### Step 2 — Progress via Stdout

The subprocess writes one JSON line per event to stdout. The .NET 10 adapter reads these lines asynchronously as they arrive and converts them to `ProgressEvent` records delivered to `IProgressSink`.

Each line is a complete, self-contained JSON object (NDJSON / JSON Lines):

```json
{"type":"Started","module":"WorkItems","message":"Beginning export","timestamp":"2026-01-15T10:00:00Z"}
{"type":"Progress","module":"WorkItems","cursor":"WorkItems/2026-01-15/1234567890-42-0","message":"Wrote revision","processed":1,"total":500,"timestamp":"2026-01-15T10:00:01Z"}
{"type":"Progress","module":"WorkItems","cursor":"WorkItems/2026-01-15/1234567891-43-0","message":"Wrote revision","processed":2,"total":500,"timestamp":"2026-01-15T10:00:02Z"}
{"type":"Completed","module":"WorkItems","message":"Export complete","processed":500,"total":500,"timestamp":"2026-01-15T10:00:45Z"}
```

#### Stdout Message Types

| `type` | Required Fields | Description |
|---|---|---|
| `Started` | `module`, `message`, `timestamp` | Subprocess has initialised and is beginning work |
| `Progress` | `module`, `cursor`, `processed`, `total`, `timestamp` | One revision folder written; `cursor` is the relative path |
| `Warning` | `module`, `message`, `timestamp` | Non-fatal condition |
| `Completed` | `module`, `processed`, `total`, `timestamp` | All work done |
| `Failed` | `module`, `message`, `errorCode`, `timestamp` | Fatal error; subprocess will exit non-zero |

### Step 3 — Errors via Stderr

The subprocess writes unstructured error detail to stderr. The adapter captures stderr and appends it to the structured `ProgressEvent` failure record.

Stderr is never parsed for progress — it is diagnostic output only.

### Step 4 — Exit Code

| Exit Code | Meaning |
|---|---|
| `0` | Export completed successfully |
| `1` | General failure (details in stderr) |
| `2` | Cancelled (cancellation token file was detected) |
| `3` | Config parse error |
| `4` | TFS connectivity failure |
| `5` | Package write failure |

---

## Cancellation

The .NET 10 adapter must NOT send SIGTERM or kill the subprocess directly. Instead:

1. When `CancellationToken` fires, the adapter writes a sentinel file at `CancellationTokenFilePath`.
2. The subprocess polls for the existence of this file at regular intervals (≤ 1 second).
3. On detecting the file, the subprocess flushes its current cursor state, writes a `Failed` stdout line with `errorCode: "Cancelled"`, and exits with code `2`.
4. The adapter deletes the sentinel file after the subprocess exits.

This allows the subprocess to clean up and write a valid cursor before stopping, enabling resume.

```csharp
// In TfsExporterProcessAdapter
if (ct.IsCancellationRequested)
{
    File.WriteAllText(request.CancellationTokenFilePath, "cancel");
    await process.WaitForExitAsync(CancellationToken.None);  // wait for graceful stop
    File.Delete(request.CancellationTokenFilePath);
}
```

---

## Resume and Cursor Handoff

The subprocess writes cursor files into `Checkpoints/` inside the package as it processes each revision folder. This is identical to how the .NET 10 modules write cursors.

When the Job Engine resumes a TFS export job:

1. It reads the cursor file from `Checkpoints/TfsExporter.cursor`.
2. It passes the last cursor position to the subprocess via `TfsExportRequest` (an additional `resumeFromCursor` field).
3. The subprocess skips already-written revision folders and resumes from the last cursor.

The .NET 10 host does not need to understand cursor internals — it reads the file, passes the value, and the subprocess interprets it.

---

## Output Validation and Normalisation

After the subprocess exits with code `0`, the .NET 10 adapter MUST:

1. **Validate** that written revision folders conform to the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` layout.
2. **Validate** that each `revision.json` parses as valid JSON with required fields.
3. **Normalise** field casing differences between TFS OM output and the canonical package schema.
4. **Reject** the export if validation fails — do not proceed to import.

Validation must use the same package validation logic used by the `validate` command.

---

## Subprocess Binary Location

The TFS exporter binary must be discovered from configuration, not embedded or resolved via reflection:

```json
{
  "tfsExporter": {
    "executablePath": "tools/tfs-exporter/DevOpsMigrationPlatform.TfsExporter.exe"
  }
}
```

**MUST NOT:**
- Embed the .NET 4 binary as a resource
- Auto-discover or probe for the binary in PATH
- Run the binary from a network share or URI

The binary path must be absolute or relative to the package root. It is resolved before the subprocess is spawned, and the host fails fast if the binary is missing.

---

## Security Controls

- The subprocess inherits no ambient credentials from the .NET 10 host process.
- PAT or NTLM credentials for TFS are passed in the `TfsExportRequest` JSON (via stdin only — never via command-line arguments, which are visible in process lists).
- The subprocess MUST redact credential fields before writing any log or stderr output.
- The cancellation sentinel file path must be within the package directory — never a shared system temp path.

---

## TfsExporter Subprocess Contract (summary)

The `DevOpsMigrationPlatform.TfsExporter` project (.NET 4.8) MUST:

- Read `TfsExportRequest` from stdin as UTF-8 JSON.
- Write NDJSON progress lines to stdout as they occur (flush after each line).
- Write error detail to stderr.
- Write package files to `packageRootPath` following the canonical layouts.
- Write cursor files to `Checkpoints/TfsExporter.cursor` after each written revision.
- Honour the cancellation sentinel file.
- Exit with the appropriate exit code.

The `DevOpsMigrationPlatform.TfsExporter` project MUST NOT:

- Call the .NET 10 Control Plane or any API other than TFS OM.
- Write to any path outside `packageRootPath`.
- Accept credentials via command-line arguments.
- Depend on any .NET 10 projects (no project references to the main solution).
- Share NuGet packages with the .NET 10 host (separate package graph).

---

## See Also

- [Source Types](source-types.md) — `TeamFoundationServer` config schema
- [Artefact Store](artefact-store.md) — Package file abstraction
- [Checkpointing](checkpointing.md) — Cursor model
- [Validation](validation.md) — Post-export validation
- [agents/coding-standards.md](../agents/coding-standards.md) — .NET runtime isolation rules

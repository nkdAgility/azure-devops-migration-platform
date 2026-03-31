# TFS Legacy Process Bridge

## Purpose

The TFS Object Model (TFS OM) is a .NET Framework 3.x/4.x SOAP library that cannot run in .NET 9/10. The entire platform runs on .NET 10 — with one narrowly bounded exception: when the source is an on-premises Team Foundation Server, extraction must delegate to an isolated external subprocess built against .NET Framework 4.8.

This document specifies the process isolation boundary, the multi-targeting strategy for shared abstractions, the communication protocol, and the adapter contract that allow the .NET 10 host to invoke the .NET 4.8 exporter safely and without any runtime coupling.

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
│  ITfsExporterAdapter                                            │
│       (interface in Abstractions — compiled for net10.0)        │
│                                                                 │
│  TfsExporterProcessAdapter  (Infrastructure.TfsLegacy)          │
│       │  spawns subprocess via ExternalToolRunner               │
│       │  reads stdout (NDJSON progress lines)                   │
│       │  reads stderr (error messages)                          │
│       │  passes cancellation via sentinel file                  │
└───────┼─────────────────────────────────────────────────────────┘
        │  process execution only — no compiled reference
┌───────▼─────────────────────────────────────────────────────────┐
│  .NET 4.8 Subprocess (DevOpsMigrationPlatform.TfsExporter)      │
│                                                                 │
│       Reads non-sensitive config from CLI args                  │
│       Reads credentials from stdin (JSON, then closes)          │
│       Writes package files to --output path                     │
│       Writes NDJSON progress lines to stdout                    │
│       Writes error detail to stderr                             │
│       Exits 0 (success) or non-zero (failure)                   │
└─────────────────────────────────────────────────────────────────┘
```

The .NET 10 host has **no compiled reference** to the .NET 4.8 project. The subprocess is an independent binary. Coupling exists only through: CLI arguments (non-sensitive config), stdin JSON (credentials), NDJSON stdout lines (progress), and the package files written to disk.

---

## Projects

| Project | Target Frameworks | Role |
|---|---|---|
| `DevOpsMigrationPlatform.Abstractions` | `net481;net10.0` | Shared interfaces and models — compiles for both runtimes |
| `DevOpsMigrationPlatform.Infrastructure` | `net481;net10.0` | Shared infrastructure (SQLite repository, utilities) — compiles for both runtimes |
| `DevOpsMigrationPlatform.Infrastructure.TfsLegacy` | `net10.0` | `TfsExporterProcessAdapter` — spawns subprocess, reads output |
| `DevOpsMigrationPlatform.TfsExporter` | `net481` | Standalone executable that calls the TFS Object Model |

### Why Multi-Targeting for Abstractions?

`DevOpsMigrationPlatform.Abstractions` and `DevOpsMigrationPlatform.Infrastructure` target both `net481` and `net10.0`. This is the key to safe code sharing:

- The **subprocess** (`TfsExporter`, net481) references `Abstractions` compiled for `net481` — it uses `IWorkItemExportService`, `MigrationWorkItemRevision`, `WorkItemMigrationProgress`, etc., natively.
- The **host** (`Migration Agent`, net10.0) references `Abstractions` compiled for `net10.0` — same types, same contracts.
- There is **no runtime coupling**: neither binary references the other project's DLL at runtime. They share source-level contracts only.

Multi-targeting is handled via `<TargetFrameworks>net481;net10.0</TargetFrameworks>` in the project file.

The `DevOpsMigrationPlatform.TfsExporter` project MUST NOT be referenced by any .NET 10 project. It is built and deployed as a separate binary.

---

## Shared Abstractions Design

All types that cross the process boundary (models written to the package on disk, and types used in progress reporting) are defined in `DevOpsMigrationPlatform.Abstractions` targeting both frameworks.

Key shared types:

| Type | Purpose |
|---|---|
| `IWorkItemExportService` | Interface implemented by TFS OM service inside subprocess |
| `MigrationWorkItemRevision` | Canonical work item revision model written to `revision.json` |
| `MigrationWorkItemField` | Field value in a revision |
| `MigrationWorkItemRelatedLink` | Related link in a revision |
| `MigrationWorkItemExternalLink` | External link in a revision |
| `MigrationWorkItemAttachment` | Attachment descriptor |
| `WorkItemMigrationProgress` | Progress event emitted per work item processed |

Types that are NOT shared (live only in the net10.0 host):

- `ITfsExporterAdapter` — process spawn contract; meaningless inside the subprocess
- `TfsExporterProcessAdapter` — the process runner; only exists in the .NET 10 Infrastructure layer
- `IArtefactStore`, `IStateStore`, `IProgressSink` — package and checkpoint abstractions; only relevant to the .NET 10 orchestration layer

---

## ExternalToolRunner

`ExternalToolRunner` is the low-level wrapper in `DevOpsMigrationPlatform.Infrastructure.TfsLegacy` that spawns the subprocess and streams its output:

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

`TfsExporterProcessAdapter` wraps `ExternalToolRunner` and translates stdout lines into `IProgressSink` events.

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
3. On detecting the file, the subprocess writes its current cursor to `Checkpoints/TfsExporter.cursor`, writes a `Failed` stdout line with `errorCode: "Cancelled"`, and exits with code `2`.
4. The adapter deletes the sentinel file after the subprocess exits.

This gives the subprocess a chance to flush its current cursor before stopping, enabling clean resume.

---

## Resume and Cursor Handoff

The subprocess writes a cursor file `Checkpoints/TfsExporter.cursor` inside the package after each revision folder is written. When the Job Engine resumes a TFS export job:

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

## TfsExporter Subprocess Contract (summary)

The `DevOpsMigrationPlatform.TfsExporter` project (net481) MUST:

- Accept non-sensitive config via CLI arguments (`--tfsserver`, `--project`, `--output`, `--query`, `--resume`).
- Read credentials from stdin as UTF-8 JSON before making any TFS connection.
- Write NDJSON progress lines to stdout, flushed after each line.
- Write error detail to stderr.
- Write package files to `--output` following canonical layouts.
- Write cursor to `Checkpoints/TfsExporter.cursor` after each revision folder.
- Poll the cancellation sentinel file and abort gracefully when it appears.
- Exit with the appropriate exit code.

The `DevOpsMigrationPlatform.TfsExporter` project MUST NOT:

- Be referenced by any .NET 10 project (no `<ProjectReference>` in any net10.0 project).
- Accept credentials via CLI arguments.
- Write to any path outside `--output`.
- Call the Control Plane or any API other than TFS OM.
- Depend on any .NET 10-only packages.

---

## See Also

- [Source Types](source-types.md) — `TeamFoundationServer` config schema
- [Artefact Store](artefact-store.md) — Package file abstraction
- [Checkpointing](checkpointing.md) — Cursor model
- [Validation](validation.md) — Post-export validation
- [ai/guardrails/coding-standards.md](../ai/guardrails/coding-standards.md) — .NET runtime isolation rules

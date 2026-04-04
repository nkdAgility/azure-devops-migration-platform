# Contract: TFS Inventory Subprocess Protocol

**Feature Branch**: `003-inventory-command`  
**Subprocess binary**: `DevOpsMigrationPlatform.CLI.TfsMigration` (`.NET 4.8`)  
**Caller**: `ExternalToolRunner.RunWithStdinAsync` in `DevOpsMigrationPlatform.CLI.Migration`  
**Based on**: `docs/tfs-exporter.md` process bridge protocol

---

## Protocol Overview

```
.NET 10 host (InventoryCommand)
    │
    │  ProcessStartInfo { FileName = tfsmigration.exe, Arguments = "inventory" }
    │  RedirectStandardInput = true
    │  RedirectStandardOutput = true
    │  RedirectStandardError = true
    │
    ├── stdin ──► UTF-8 JSON TfsInventoryRequest (then EOF)
    │
    ├── stdout ◄── NDJSON lines (one JSON object per line, no buffering)
    │                └── { "projectName": "...", "workItemCount": N, "isComplete": true/false }
    │
    ├── stderr ◄── Unstructured error text (captured; displayed on non-zero exit)
    │
    └── exit code ◄── 0 = success, non-zero = failure
```

---

## stdin — `TfsInventoryRequest` (UTF-8 JSON)

The .NET 10 host writes the following JSON to the subprocess stdin, then closes the stream (EOF):

```json
{
  "collectionUrl": "http://tfs.internal:8080/tfs/DefaultCollection",
  "token": "RESOLVED-PAT-VALUE",
  "project": "MyProject",
  "apiVersion": "15.0"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `collectionUrl` | `string` | Yes | TFS collection URL |
| `token` | `string` | Yes | Resolved PAT (plain value — never `$ENV:` reference) |
| `project` | `string` | No | Single-project filter; omit for all projects |
| `apiVersion` | `string` | No | Defaults to `"15.0"` if omitted |

**Security**: The `token` field carries the PAT. It is passed via stdin JSON — never via command-line arguments (which are visible in the process table on Linux). The .NET 10 host must resolve `$ENV:` references before constructing this object.

---

## stdout — NDJSON Progress Lines

Each line of stdout is a complete JSON object (no line-spanning JSON):

```
{ "projectName": "Alpha", "workItemCount": 5000, "isComplete": false }
{ "projectName": "Alpha", "workItemCount": 20000, "isComplete": false }
{ "projectName": "Alpha", "workItemCount": 22500, "isComplete": true }
{ "projectName": "Beta", "workItemCount": 800, "isComplete": true }
```

| Field | Type | Description |
|---|---|---|
| `projectName` | `string` | Team project name |
| `workItemCount` | `int` | Accumulated count so far (partial if `isComplete=false`) |
| `isComplete` | `bool` | `true` when this project's full count has been obtained |

**Behaviour on the .NET 10 side**:
- Each NDJSON line is parsed by `ExternalToolRunner.RunWithStdinAsync`'s `onOutput` callback.
- Lines with `isComplete=false` are used to update the live progress table (intermediate counts).
- The last line per project (`isComplete=true`) is used as the final count for CSV and summary.
- Lines that fail JSON parsing are treated as warnings; the count is marked as partial.

---

## stderr — Error Output

- Unstructured free-form text.
- Captured in full by the `.NET 10` host.
- Displayed to the operator when exit code is non-zero (prefixed with the source label).
- Not parsed by the `.NET 10` host.

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success — all requested projects counted; stdout contains complete NDJSON |
| `1` | Authentication failure — invalid PAT or insufficient permissions |
| `2` | Connection failure — cannot reach the TFS collection URL |
| `3` | Collection not found — URL resolved but collection does not exist |
| `4` | Project not found — requested project does not exist in the collection |
| `5` | General / unexpected failure — see stderr for detail |

---

## `ExternalToolRunner.RunWithStdinAsync` — New Overload

**Location**: `DevOpsMigrationPlatform.CLI.Migration/ExternalToolRunner.cs`

```csharp
/// <summary>
/// Launches an external executable, writes <paramref name="stdinContent"/> to its
/// standard input (then closes stdin), and streams stdout/stderr via callbacks.
/// Returns the process exit code.
/// </summary>
/// <remarks>
/// This overload is TFS-agnostic. It is suitable for any subprocess that reads
/// a single JSON payload from stdin and emits NDJSON lines to stdout.
/// </remarks>
public static async Task<int> RunWithStdinAsync(
    string exePath,
    string arguments,
    string stdinContent,
    Action<string>? onOutput = null,
    Action<string>? onError = null,
    CancellationToken cancellationToken = default)
```

**Implementation notes**:
1. `ProcessStartInfo.RedirectStandardInput = true` (in addition to the existing output/error redirects).
2. After `process.Start()`: write `stdinContent` to `process.StandardInput`, call `process.StandardInput.Close()` (EOF signal).
3. Begin async output/error reads.
4. `await process.WaitForExitAsync(cancellationToken)`.
5. Return `process.ExitCode`.

**Deadlock avoidance**: Stdin must be closed before the async reads are awaited. If stdin is not closed, the subprocess may block waiting for more input, causing a deadlock.

---

## `TfsInventoryRequest` DTO

**Location**: `DevOpsMigrationPlatform.Abstractions/Models/TfsInventoryRequest.cs`  
**Target frameworks**: `net481;net10.0` (Abstractions is multi-targeted)

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Written as UTF-8 JSON to the stdin of the TFS inventory subprocess.
/// Credentials are passed here — never via command-line arguments.
/// </summary>
public sealed class TfsInventoryRequest
{
    public string CollectionUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string ApiVersion { get; set; } = "15.0";
}
```

---

## Caller-Side Usage (InventoryCommand sketch)

```csharp
var request = new TfsInventoryRequest
{
    CollectionUrl = source.OrgOrCollection,
    Token = resolvedToken,          // already resolved by ITokenResolver
    Project = effectiveProject,     // null means "all projects"
    ApiVersion = source.ApiVersion ?? "15.0"
};

var stdinJson = JsonSerializer.Serialize(request);
var stderr = new StringBuilder();
var projectLines = new List<string>();

var exitCode = await ExternalToolRunner.RunWithStdinAsync(
    exePath: _tfsMigrationExePath,
    arguments: "inventory",
    stdinContent: stdinJson,
    onOutput: line => projectLines.Add(line),
    onError: line => stderr.AppendLine(line),
    cancellationToken: ct);

if (exitCode != 0)
{
    // surface stderr, mark source as failed
    return new InventorySourceResult
    {
        SourceLabel = source.OrgOrCollection,
        ErrorMessage = $"TFS subprocess exited with code {exitCode}.\n  stderr: {stderr}"
    };
}

// Parse NDJSON lines
var summaries = projectLines
    .Select(line => TryParseNdjsonLine(line))
    .Where(s => s is { IsComplete: true })
    .Select(s => new ProjectDiscoverySummary
    {
        ProjectName = s!.ProjectName,
        WorkItemsCount = s.WorkItemCount,
        IsWorkItemComplete = true
    })
    .ToList();

return new InventorySourceResult
{
    SourceLabel = source.OrgOrCollection,
    ProjectSummaries = summaries
};
```

---

## Versioning and Backward Compatibility

- The `TfsInventoryRequest` schema is defined as version `1.0` (implied; no explicit version field in v1).
- The subprocess binary version must match the main CLI binary version. No cross-version compatibility is guaranteed.
- Future schema changes require a version field to be added to `TfsInventoryRequest`.

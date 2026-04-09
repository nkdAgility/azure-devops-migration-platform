# API Contracts: Three-Channel Observability

## Control Plane HTTP API

### Progress Endpoints (renamed from `/logs`)

#### GET /jobs/{jobId}/progress

Returns a JSON array of `ProgressEvent` records from the in-memory ring buffer.

**Request**: `GET /jobs/{jobId}/progress`  
**Auth**: Same as `GET /jobs/{jobId}` (submitter or admin)  
**Response 200**:
```json
[
  {
    "module": "WorkItems",
    "stage": "AppliedFields",
    "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
    "totalWorkItems": 1500,
    "workItemsProcessed": 312,
    "revisionsProcessed": 874,
    "workItemId": 12345,
    "message": "Exporting revision 17",
    "timestamp": "2026-02-25T18:12:34Z"
  }
]
```

#### GET /jobs/{jobId}/progress?follow=true

SSE stream of `ProgressEvent` records. Replays buffered events then streams live.

**Response**: `Content-Type: text/event-stream`
```
data: {"module":"WorkItems","stage":"AppliedFields",...}

data: {"module":"WorkItems","stage":"AppliedFields",...}

event: job-ended
data: {}
```

Heartbeat comment every 15s: `:\n\n`

---

### Diagnostics Endpoints (new)

#### POST /agents/lease/{leaseId}/diagnostics

Agent pushes a batch of `DiagnosticLogRecord` records.

**Request**: `POST /agents/lease/{leaseId}/diagnostics`  
**Body**:
```json
[
  {
    "timestamp": "2026-02-25T18:12:34Z",
    "level": "Warning",
    "category": "AzureDevOpsAttachmentBinarySource",
    "message": "Attachment WI#45678 rev 3: 403 Forbidden",
    "exception": null,
    "traceId": "abc123",
    "spanId": "def456"
  }
]
```
**Response 204**: Accepted  
**Response 404**: Lease not recognised

#### GET /jobs/{jobId}/diagnostics

Returns a JSON array of `DiagnosticLogRecord` records from the in-memory ring buffer.

**Request**: `GET /jobs/{jobId}/diagnostics`  
**Query params**:
- `level` (optional): Minimum log level filter. Default: all levels in the buffer. Values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`.

**Auth**: Same as `GET /jobs/{jobId}`  
**Response 200**:
```json
[
  {
    "timestamp": "2026-02-25T18:12:34Z",
    "level": "Warning",
    "category": "AzureDevOpsAttachmentBinarySource",
    "message": "Attachment WI#45678 rev 3: 403 Forbidden",
    "exception": null,
    "traceId": "abc123",
    "spanId": "def456"
  }
]
```

#### GET /jobs/{jobId}/diagnostics?follow=true

SSE stream of `DiagnosticLogRecord` records. Replays buffered events then streams live. Consumed by the TUI diagnostics panel and by `export --follow`. Not exposed via CLI `manage` commands.

**Query params**:
- `level` (optional): Minimum log level for streamed records. Default: `Warning`. The effective floor is the higher of the requested level and the control plane's deployment-level minimum.

**Response**: `Content-Type: text/event-stream`
```
data: {"timestamp":"...","level":"Warning","category":"...","message":"..."}

data: {"timestamp":"...","level":"Error","category":"...","message":"...","exception":"..."}

event: job-ended
data: {}
```

---

### Package Log Download Endpoint (new)

#### GET /jobs/{jobId}/logs/download

Downloads a persisted log file from the migration package.

**Query params**:
- `type` (required): `progress` or `diagnostics`

**Auth**: Same as `GET /jobs/{jobId}`  
**Response 200**: File contents with `Content-Type: application/x-ndjson`  
**Response 404**: File not found in package

The endpoint resolves the job's `packageUri` via `IPackageStoreFactory`, then reads:
- `type=progress` → `Logs/progress.jsonl`
- `type=diagnostics` → `Logs/agent.jsonl`

---

## CLI Commands

### export (updated)

```
devopsmigration export --config <path> [--url <cp-url>] [--follow] [--level <level>]
```

Submits an export job. In standalone mode (no `--url`), `--follow` is implicit and the locally-started control plane adopts `--level`.

| Option | Default | Description |
|---|---|---|
| `--follow` | `false` (remote), implicit (standalone) | Stream diagnostic logs to console during job execution |
| `--level` | `Warning` | Agent diagnostic log minimum level. Sets what's written to `agent.jsonl` and pushed to CP |

**Lifecycle**: With `--follow`, streams diagnostics via `GET /jobs/{jobId}/diagnostics?follow=true`. On job completion → prints summary and exits. On Ctrl+C → detaches (job continues), prints "Use TUI to watch".

### manage progress (new, replaces progress-event half of manage logs)

```
devopsmigration manage progress --job <jobId>
```

Fetches a snapshot of `ProgressEvent` records from the control plane ring buffer. No `--follow` — live streaming is TUI-only.

### manage diagnostics (replaces manage logs, download-only)

```
devopsmigration manage diagnostics --job <jobId> [--level <level>]
```

Downloads `Logs/agent.jsonl` from a completed job's package via `GET /jobs/{jobId}/logs/download?type=diagnostics`. Filters output by `--level`. No `--follow` — this is a post-mortem helper only.

| Option | Default | Description |
|---|---|---|
| `--job` | required | Job ID |
| `--level` | *(all)* | Minimum log level filter applied client-side to downloaded records |

---

## IArtefactStore Extension (new method)

```csharp
/// <summary>
/// Appends <paramref name="content"/> to the specified <paramref name="path"/> within the package.
/// Creates the file (and ancestor directories) if it does not exist.
/// </summary>
Task AppendAsync(string path, string content, CancellationToken cancellationToken);
```

Implementations:
- `FileSystemArtefactStore`: `File.AppendAllTextAsync(fullPath, content, Encoding.UTF8, ct)`
- `AzureBlobArtefactStore`: `AppendBlobClient.AppendBlockAsync(stream, ct)`

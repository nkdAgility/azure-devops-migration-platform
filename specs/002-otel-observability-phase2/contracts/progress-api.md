# Progress API Contract

**Feature**: `002-otel-observability-phase2`  
**Project**: `DevOpsMigrationPlatform.ControlPlane` → hosted by `DevOpsMigrationPlatform.ControlPlaneHost`  
**Controllers**: `ProgressController`, `WorkerEventsController`, `JobStreamController`

> **Note (2026-06-30 — Phases A-E):** The primary agent→CP transport is now `POST /workers/{workerId}/events` (`WorkerEventsController`). The legacy `POST /agents/lease/{leaseId}/progress` endpoint remains as a backward-compat shim. The primary CLI→CP stream is now `GET /jobs/{jobId}/stream` (`JobStreamController`). See sections below.

---

## POST /agents/lease/{leaseId}/progress

Called by `ControlPlaneProgressSink` (Migration Agent) to report a single progress event.

### Request

| Element | Value |
|---|---|
| Method | `POST` |
| Path | `/agents/lease/{leaseId}/progress` |
| Auth | Agent bearer token (same as `/agents/lease` endpoints) |
| Content-Type | `application/json` |

**Path parameters**:

| Name | Type | Description |
|---|---|---|
| `leaseId` | `Guid` | Active lease ID held by the calling Migration Agent |

**Body** — `ProgressEvent` object:

```json
{
  "module": "WorkItems",
  "stage": "AppliedFields",
  "lastProcessed": "WorkItems/2026-04-03/638760123456789012-12345-17",
  "totalWorkItems": 500,
  "workItemsProcessed": 42,
  "revisionsProcessed": 187,
  "workItemId": 12345,
  "message": "Applying fields for revision 17",
  "timestamp": "2026-04-03T14:22:18.123Z",
  "metrics": null
}
```

> `metrics` is `null` when emitted by the .NET 10 Migration Agent. It is non-null for TFS subprocess events.

### Responses

| Status | When | Body |
|---|---|---|
| `204 No Content` | Event stored and fanned out | empty |
| `400 Bad Request` | Body is not a valid `ProgressEvent` | `{ "error": "..." }` |
| `404 Not Found` | `leaseId` is unknown or expired | `{ "error": "Lease not found." }` |
| `401 Unauthorized` | No / invalid bearer token | empty |

---

## GET /jobs/{jobId}/logs

Returns the current snapshot of buffered `ProgressEvent` records for a job as a JSON array. Non-streaming.

### Request

| Element | Value |
|---|---|
| Method | `GET` |
| Path | `/jobs/{jobId}/logs` |
| Auth | Same visibility rules as `GET /jobs/{jobId}` (owner or admin) |

**Path parameters**:

| Name | Type | Description |
|---|---|---|
| `jobId` | `Guid` | Job to retrieve logs for |

### Responses

| Status | When | Body |
|---|---|---|
| `200 OK` | Success | JSON array of `ProgressEvent` objects (may be empty `[]`) |
| `404 Not Found` | `jobId` is unknown | `{ "error": "Job not found." }` |
| `403 Forbidden` | Caller lacks visibility of the job | `{ "error": "Access denied." }` |
| `401 Unauthorized` | No / invalid bearer token | empty |

**Example 200 body**:

```json
[
  {
    "module": "WorkItems",
    "stage": "AppliedFields",
    "lastProcessed": "WorkItems/2026-04-03/638760123456789012-12345-17",
    "totalWorkItems": 500,
    "workItemsProcessed": 42,
    "revisionsProcessed": 187,
    "workItemId": 12345,
    "message": null,
    "timestamp": "2026-04-03T14:22:18.123Z",
    "metrics": null
  }
]
```

---

## GET /jobs/{jobId}/logs?follow=true

Server-Sent Events (SSE) stream of `ProgressEvent` records. Delivers events in real time as they are posted by the Migration Agent.

### Request

| Element | Value |
|---|---|
| Method | `GET` |
| Path | `/jobs/{jobId}/logs?follow=true` |
| Auth | Same visibility rules as `GET /jobs/{jobId}` (owner or admin) |

**Path parameters**:

| Name | Type | Description |
|---|---|---|
| `jobId` | `Guid` | Job to stream logs for |

**Required response headers**:

```
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no
```

### Stream Protocol

Each progress event is sent as:

```
data: {"module":"WorkItems","stage":"AppliedFields",...}\n
\n
```

(Single `data:` line per event; compact JSON; double newline to terminate the SSE frame.)

**Heartbeat** (every 15 seconds of idle, to keep proxies alive):

```
:\n
\n
```

(SSE comment line — clients ignore it; it triggers a TCP write to keep the connection alive.)

**Stream end** (when job reaches a terminal state — `Completed`, `Failed`, or `Cancelled`):

```
event: job-ended\n
data: {}\n
\n
```

After sending this frame the server closes the response.

### Responses

| Status | When | Description |
|---|---|---|
| `200 OK` | Headers sent; stream open | SSE stream begins immediately |
| `404 Not Found` | `jobId` is unknown | JSON error body; connection not upgraded to SSE |
| `403 Forbidden` | Caller lacks visibility | JSON error body |
| `401 Unauthorized` | No / invalid bearer token | empty |

### Consumer pattern (C# example)

```csharp
using var response = await http.GetAsync(
    $"/jobs/{jobId}/logs?follow=true",
    HttpCompletionOption.ResponseHeadersRead,
    ct);

response.EnsureSuccessStatusCode();

await using var stream = await response.Content.ReadAsStreamAsync(ct);
using var reader = new StreamReader(stream);

await foreach (var line in reader.ReadLinesAsync(ct))
{
    if (line.StartsWith("data: "))
    {
        var json = line["data: ".Length..];
        var evt = JsonSerializer.Deserialize<ProgressEvent>(json, _options);
        yield return evt!;
    }
    else if (line == "event: job-ended")
    {
        break; // stream complete
    }
    // heartbeat comment lines (starting with ':') are ignored
}
```

---

---

## POST /workers/{workerId}/events

**Primary agent→CP telemetry channel (Phase C).** Accepts a batch of typed `WorkerEvent` records from a `UnifiedWorkerEventWriter` running inside the Migration Agent.

### Request body — `WorkerEventBatch`

```json
{
  "workerId": "3f4a7b...",
  "leaseId": "abc-123",
  "events": [
    { "seq": 1, "timestamp": "...", "kind": "Progress",    "payloadJson": "{...ProgressEvent...}" },
    { "seq": 2, "timestamp": "...", "kind": "Diagnostic",  "payloadJson": "[{...DiagnosticLogRecord...}]" },
    { "seq": 3, "timestamp": "...", "kind": "Tasks",       "payloadJson": "{...JobTaskList...}" },
    { "seq": 4, "timestamp": "...", "kind": "Terminal",    "payloadJson": "{\"failed\":false}" }
  ]
}
```

**`WorkerEventKind` values:** `Heartbeat`, `Progress`, `Diagnostic`, `Metrics`, `Snapshot`, `Tasks`, `Terminal`.

### Response — `WorkerEventAck`

```json
{ "lastAcceptedSeq": 4 }
```

Returns `429 Too Many Requests` if the CP is under load; agent retries the same batch after 2 s.

---

## GET /jobs/{jobId}/stream

**Primary CLI→CP unified SSE stream (Phase E).** Multiplexes progress and diagnostic events into one connection.

### Request

| Element | Value |
|---|---|
| Method | `GET` |
| Path | `/jobs/{jobId}/stream` |
| Query | `?from={seq}` — replay events with `seq > from` (default `0` = full history) |
| Auth | Same visibility rules as `GET /jobs/{jobId}` |

### Stream protocol

Each event is one of:

```
id: {seq}
event: progress
data: {...ProgressEvent JSON...}

event: diagnostic
data: {...DiagnosticLogRecord JSON...}

event: job-ended
data: {}

event: job-failed
data: {}
```

Heartbeat comment every 15 s:
```
:
```

The server subscribes to live channels **before** replaying history so no events are missed between the snapshot read and the subscription.

### C# consumer pattern

```csharp
await foreach (var evt in client.StreamJobAsync(jobId, ct))
{
    switch (evt.Kind)
    {
        case JobStreamEventKind.Progress:   Apply(evt.Progress); break;
        case JobStreamEventKind.Diagnostic: Render(evt.Diagnostic); break;
        case JobStreamEventKind.Terminal:   return evt.Failed == true ? Fail() : Complete();
    }
}
```

---

## Notes

- The append-only event log capacity is configurable via `JobProgressOptions.MaxEventsPerJob` (default 50,000). Reaching the cap emits a warning log; events are not silently dropped.
- Concurrent SSE subscribers for the same job are unlimited. Each subscriber gets its own bounded channel (5,000 capacity, DropOldest) so slow clients cannot block the append path.
- `UnifiedWorkerEventWriter` batches ≤50 events or 500 ms, whichever comes first. Terminal events bypass the batch timer and are flushed immediately.

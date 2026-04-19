# Control Plane Telemetry HTTP Contract

## Agent → Control Plane: Push Snapshot

### `POST /agents/lease/{leaseId}/telemetry`

Migration Agent pushes the current metric snapshot on a configurable interval (default 5 s).

**Authorization**: Same bearer/Negotiate token as all other `/agents/lease/*` endpoints.

**Request body** (`application/json`):
```json
{
  "timestamp": "2026-04-02T10:15:30Z",
  "workItemsExported": 1247,
  "revisionsExported": 8943,
  "revisionErrors": 2,
  "linksExported": 12034,
  "linkErrors": 0,
  "attachmentsAttempted": 543,
  "attachmentsSucceeded": 541,
  "attachmentsFailed": 2,
  "workItemDurationMeanMs": 95.4,
  "revisionDurationMeanMs": 18.7,
  "totalExportDurationMs": 847200.0
}
```

**Responses**:
| Code | Meaning |
|------|---------|
| `204 No Content` | Snapshot accepted and stored. |
| `404 Not Found` | `leaseId` is not a known active lease. |
| `401 Unauthorized` | Token missing or invalid. |

---

## TUI → Control Plane: Poll Latest Snapshot

### `GET /jobs/{jobId}/telemetry`

Returns the most recent `MetricSnapshot` received for the given job, or `204` if none has arrived yet.

**Authorization**: Same bearer/Negotiate token as `GET /jobs/{jobId}`.

**Response body** (`application/json`, `200 OK`):
```json
{
  "timestamp": "2026-04-02T10:15:30Z",
  "workItemsExported": 1247,
  "revisionsExported": 8943,
  "revisionErrors": 2,
  "linksExported": 12034,
  "linkErrors": 0,
  "attachmentsAttempted": 543,
  "attachmentsSucceeded": 541,
  "attachmentsFailed": 2,
  "workItemDurationMeanMs": 95.4,
  "revisionDurationMeanMs": 18.7,
  "totalExportDurationMs": 847200.0
}
```

**Responses**:
| Code | Meaning |
|------|---------|
| `200 OK` | Snapshot returned. |
| `204 No Content` | Job exists but no snapshot has been received yet. |
| `404 Not Found` | `jobId` is not known. |
| `403 Forbidden` | Caller lacks visibility to this job. |

---

## (Phase 2 Optional) Live Stream

### `GET /jobs/{jobId}/telemetry/stream`

Server-Sent Events stream. Pushes a new `MetricSnapshot` JSON object every time one is
received from the Migration Agent. This endpoint is optional in Phase 1 and documented
for Phase 2 implementation only.

**Content-Type**: `text/event-stream`

**Event format**:
```
event: snapshot
data: { "timestamp": "...", "workItemsExported": 1247, ... }

```

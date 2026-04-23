# Contract: Resumable Batching (Caller ↔ Strategy)

**Feature**: 020-resumable-batching-cursor  
**Date**: 2026-04-22

---

## Contract Parties

| Party | Role | Boundary |
|-------|------|----------|
| **Strategy** (`IWorkItemQueryWindowStrategy` / `IWorkItemFetchService`) | Emits batched work item windows with continuation checkpoints | Infrastructure layer |
| **Caller** (Discovery, Dependency, Export orchestrators) | Persists checkpoints, handles duplicates, decides mismatch recovery | Module / orchestrator layer |

---

## Inputs (Caller → Strategy)

### WorkItemQueryWindowOptions (extended)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `ResumeEnabled` | `bool` | `false` | Opt-in to resume behavior |
| `SavedContinuationToken` | `BatchContinuationToken?` | `null` | Token from prior run (caller-persisted) |
| `QueryParameters` | `IReadOnlyDictionary<string, string>?` | `null` | Parameters included in fingerprint computation |
| `BaseQuery` | `string?` | `null` | Existing field — WIQL WHERE clause |
| `InitialWindowDays` | `int` | `120` | Existing field |
| `LimitThreshold` | `int` | `20_000` | Existing field |

### WorkItemFetchScope (extended)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `ResumeEnabled` | `bool` | `false` | Pass-through to window options |
| `SavedContinuationToken` | `BatchContinuationToken?` | `null` | Token from prior run |
| `ContinuationCheckpointWriter` | `Func<BatchContinuationToken, CancellationToken, Task>?` | `null` | Callback invoked per-batch with checkpoint. If null and ResumeEnabled=true, a warning log is emitted and checkpoints are silently skipped. |

---

## Outputs (Strategy → Caller)

### ResumeDecision Delivery

The resume decision is evaluated once at the start of `FetchAsync()` in `AzureDevOpsWorkItemFetchService`:

- **Accepted**: Enumeration begins from saved position. Structured log + OTel counter emitted.
- **RejectedQueryMismatch**: `ResumeRejectedException` thrown (extends `InvalidOperationException`; carries `ResumeDecision` payload). Caller catches and decides recovery.
- **Unavailable**: Enumeration begins from start. Info-level log emitted. No exception.

### EvaluateResumeDecisionAsync (Pre-Check)

`IWorkItemFetchService` exposes `EvaluateResumeDecisionAsync(WorkItemFetchScope, CancellationToken)` returning `ResumeDecision`. Callers can invoke this BEFORE `FetchAsync()` to inspect the decision without catching exceptions. `ResumeRejectedException` in `FetchAsync()` remains as a safety net for callers that skip the pre-check.

### QueryFingerprintService Injection

`IQueryFingerprintService` is injected into `AzureDevOpsWorkItemFetchService` via constructor. The fingerprint is computed from `WorkItemFetchScope.BaseQuery` + `WorkItemQueryWindowOptions.QueryParameters` at the start of `FetchAsync()`, before window enumeration begins. The fingerprint is embedded in each `BatchContinuationToken` emitted to callers.

**Query Normalisation Rules** (applied before hashing):
1. Collapse all contiguous whitespace (spaces, tabs, newlines) to a single space.
2. Trim leading and trailing whitespace.
3. Uppercase WIQL keywords: `SELECT`, `FROM`, `WHERE`, `ORDER BY`, `ASC`, `DESC`, `AND`, `OR`, `NOT`, `IN`, `UNDER`, `EVER`, `CONTAINS`, `LIKE`.

This ensures semantically identical queries produce the same fingerprint regardless of formatting differences.

### BatchContinuationToken

Emitted per-batch (via `ContinuationCheckpointWriter` callback) and once at end-of-stream with `Completed = true`.

| Field | Type | Description |
|-------|------|-------------|
| `StrategyVersion` | `string` | Schema version for token compatibility |
| `ChangedDateUtc` | `DateTime` | Primary resume key — last processed ChangedDate |
| `WorkItemId` | `int` | Secondary resume key — last processed work item ID |
| `QueryFingerprint` | `string` | SHA-256 of (normalised query text + sorted parameters) |
| `GeneratedAtUtc` | `DateTime` | Diagnostic timestamp |
| `Completed` | `bool` | `true` = end-of-stream; safe to skip on next resume |

> **v1 note**: Fallback fields (`FallbackBatchSize`, `FallbackBatchIndex`, `FallbackChecksum`) are deferred to a future version. No concrete consumer exists today. The `StrategyVersion` field will signal when fallback fields are added.

> **Security**: Only the SHA-256 hash of query+parameters is stored in `QueryFingerprint`. Raw query text and parameter values MUST NOT appear in the serialised token.

### ResumeDecision

Returned before enumeration begins when `ResumeEnabled = true`.

| Status | Meaning | Diagnostic Fields |
|--------|---------|-------------------|
| `Accepted` | Token valid; enumeration continues from saved position | `TokenStrategyVersion` |
| `RejectedQueryMismatch` | Fingerprints differ; caller decides recovery | `SavedQueryFingerprint`, `CurrentQueryFingerprint`, `Reason` |
| `Unavailable` | No saved token; start from beginning | `Reason` |

---

## Behavioral Contract

### Ordering Guarantee

- Default: `ORDER BY [System.ChangedDate] ASC, [System.Id] ASC`
- Resume enumeration starts from `(ChangedDateUtc, WorkItemId)` in the saved token
- Non-resume callers retain existing traversal behavior (backward date windows)

### Resume Predicate (Boundary Cluster Correctness)

When resuming from a saved token `(savedDate, savedId)`:

1. The WIQL WHERE clause MUST use `[System.ChangedDate] >= @savedDate` (inclusive `>=`, not `>`) to avoid skipping items sharing the same ChangedDate.
2. The first resumed window MUST apply an in-memory post-filter excluding items where `(ChangedDate, WorkItemId) <= (savedDate, savedId)` to avoid double-processing the last completed item.
3. Subsequent windows (after the first) do NOT require the post-filter since the full window is new.

This ensures correctness even when hundreds of items share an identical ChangedDate (e.g. bulk imports or migrations).

### Checkpoint Emission Rules

1. Per-batch checkpoint: emitted after each `WorkItemQueryWindow` is yielded, containing the last item's `(ChangedDate, WorkItemId)` tuple
2. Completion checkpoint: emitted after the last window with `Completed = true`
3. Caller decides persistence cadence (persist every checkpoint, every N checkpoints, etc.)
4. `ContinuationCheckpointWriter` callback MUST be atomic or idempotent. Callers SHOULD use write-then-rename or `IStateStore` atomic semantics. Strategy propagates `OperationCanceledException` from callback without catching it.

### Duplicate Tolerance

- Strategy does NOT deduplicate
- Source drift may cause items to appear in multiple windows after resume
- Caller MUST handle duplicates via idempotent persistence or explicit dedup

### Mismatch Recovery

- Strategy rejects unsafe resume and returns `RejectedQueryMismatch`
- Caller chooses recovery: fail fast, discard token + fresh start, or log + continue
- Strategy does NOT auto-recover or auto-discard

---

## Error Model

| Condition | Strategy Behavior |
|-----------|-------------------|
| `ResumeEnabled = false` | Ignore token; normal traversal |
| `ResumeEnabled = true`, no token | Return `ResumeDecision.Unavailable`; start fresh |
| `ResumeEnabled = true`, token present, fingerprint match | Return `ResumeDecision.Accepted`; skip to position |
| `ResumeEnabled = true`, token present, fingerprint mismatch | Return `ResumeDecision.RejectedQueryMismatch` |
| Token with unknown `StrategyVersion` | Return `ResumeDecision.RejectedQueryMismatch` with reason |
| Malformed/corrupt token | Return `ResumeDecision.Unavailable` with reason; emit warning-level structured log with corruption details (RT-L1) |
| `CancellationToken` signaled | Propagate cancellation; no final checkpoint emitted |

---

## Observability Contract

| Event | Mechanism | Tags/Fields |
|-------|-----------|-------------|
| Resume decision | Structured log (`ILogger`) | `decision`, `module`, `fingerprint_match` |
| Resume decision counter | OTel metric `migration.resume.decision` | `decision`, `module` |
| Token staleness warning | Structured log (`ILogger`) | `token_age_days`, `module` — emitted when `GeneratedAtUtc` is >7 days old |
| Checkpoint emission | `IProgressSink` event | `window_index`, `changed_date`, `work_item_id` |
| Completion checkpoint | `IProgressSink` event | `completed=true`, `total_windows` |

---

## Backward Compatibility

- All new fields have defaults that preserve current behavior
- `ResumeEnabled = false` (default) → zero behavioral change
- New `ICheckpointingService` methods are additive; existing cursor methods unchanged
- `ContinuationFile()` path does not conflict with existing `CursorFile()` path
- `ContinuationFile()` MUST be scoped by caller/module name, e.g. `.migration/Checkpoints/<module>.continuation.json` (matching the per-module cursor convention). This prevents concurrent callers from corrupting each other's tokens.
- No breaking schema change; no upgrader required

# Resilience Patterns

This document describes the three retry and resilience strategies used in the platform and when to reach for each one.

---

## 1 — Polly HTTP Back-off (transient HTTP errors)

**When to use**: Retrying HTTP calls that may fail with transient `429 Too Many Requests` or `5xx` server errors.

**Used by**: `AzureDevOpsEmbeddedImageDownloader`

**Pattern**:
```csharp
Policy
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
    .ExecuteAsync(ct => httpClient.GetAsync(url, ct));
```

**Do not use for**: TFS COM calls (synchronous, no HTTP layer), WIQL query limits (use window halving instead).

---

## 2 — WIQL Window Halving (query result overflow)

**When to use**: A WIQL query may return more than the TFS/ADO 20,000-item limit. Split the date window in half and retry instead of returning truncated results.

**Used by**:
- `WorkItemQueryWindowStrategy` (ADO REST) — three-level algorithm: unbounded probe → backward date-window scan with halving → single-day ID-range pages
- `TfsWorkItemQueryWindowStrategy` (TFS OM) — wraps `WorkItemStoreExtensions.QueryAllByDateChunk` which halves the chunk on overflow

**Pattern**: The strategy detects overflow (`count >= limit`) and halves `chunkSize`, then retries the same window without advancing the cursor.

**Do not use for**: HTTP errors (use Polly), TFS COM call failures (log and skip).

---

## 3 — Log and Continue (TFS COM errors)

**When to use**: A single TFS COM call fails (e.g., a work item or attachment can not be loaded). The failure is non-fatal — log it and continue with the next item rather than aborting the entire export.

**Used by**:
- `TfsWorkItemRevisionSource.GetRevisionsAsync` — catches per-work-item and per-revision load failures
- `TfsAttachmentBinarySource.GetBytesAsync` — returns `null` (non-fatal) on download failure

**Pattern**: `catch (Exception ex) { _logger.LogError(ex, "…"); continue; }` — never rethrow unless it is an `OperationCanceledException`.

**Do not use for**: Structural failures (authentication errors, missing project) where retrying individual items will not help.

# Data Model: Work Item OpenTelemetry Metrics

**Feature Branch**: `018-workitem-otel-metrics`
**Date**: 2026-04-19

---

## 1. Metric Instruments

### 1.1 Execution Metrics (Counters)

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitems.attempted` | `Counter<long>` | `{work_item}` | Incremented when the system starts processing a work item | FR-007 |
| `migration.workitems.completed` | `Counter<long>` | `{work_item}` | Incremented on successful processing | FR-008 |
| `migration.workitems.failed` | `Counter<long>` | `{work_item}` | Incremented on terminal failure | FR-009 |
| `migration.workitems.retried` | `Counter<long>` | `{work_item}` | Incremented on each retry attempt | FR-010 |

### 1.2 Execution Metrics (Histograms)

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitem.duration.ms` | `Histogram<double>` | `ms` | Per work item processing duration | FR-011 |

### 1.3 Payload and Complexity Metrics (Histograms)

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitem.fields.count` | `Histogram<int>` | `{field}` | Field count per work item | FR-012 |
| `migration.workitem.attachments.count` | `Histogram<int>` | `{attachment}` | Attachment count per work item | FR-013 |
| `migration.workitem.links.count` | `Histogram<int>` | `{link}` | Link count per work item | FR-014 |
| `migration.workitem.revisions.count` | `Histogram<int>` | `{revision}` | Revision count per work item | FR-015 |
| `migration.workitem.payload.bytes` | `Histogram<long>` | `By` | Serialised payload size per work item | FR-016 |

### 1.4 Correctness Metrics — Count Parity (Tier 3 post-flight only)

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitem.revisions.source.count` | `Histogram<int>` | `{revision}` | Revision count from source/package per sampled item | FR-017 |
| `migration.workitem.revisions.target.count` | `Histogram<int>` | `{revision}` | Revision count from target per sampled item | FR-018 |
| `migration.workitem.revisions.delta` | `Histogram<int>` | `{revision}` | `target − source` revision count delta per sampled item | FR-019 |
| `migration.workitems.revisions.missing` | `Counter<long>` | `{work_item}` | Items with fewer target revisions than source | FR-020 |
| `migration.workitems.revision_order_errors` | `Counter<long>` | `{work_item}` | Items with non-chronological target revision ordering | FR-021 |
| `migration.workitems.broken_links` | `Counter<long>` | `{work_item}` | Items with fewer target links than source | FR-022 |
| `migration.workitems.missing` | `Counter<long>` | `{work_item}` | Package items absent from target | FR-023 |

### 1.5 In-Flight State Metrics

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitems.in_flight` | `UpDownCounter<int>` | `{work_item}` | Current number of items being processed | FR-030 |
| `migration.queue.workitems.depth` | `ObservableGauge<int>` | `{work_item}` | Current pending queue depth | FR-031 |

### 1.6 Idempotency and Resume Metrics (Deferred — instrument registration only)

| Instrument Name | Type | Unit | Description | FR |
|---|---|---|---|---|
| `migration.workitems.duplicated` | `Counter<long>` | `{work_item}` | Second TargetId for same SourceId (deferred) | FR-025 |
| `migration.workitems.changed_on_rerun` | `Counter<long>` | `{work_item}` | Re-run modified a completed target (deferred) | FR-026 |
| `migration.workitems.reprocessed_after_resume` | `Counter<long>` | `{work_item}` | Item processed again after resume (deferred) | FR-027 |
| `migration.workitems.duplicated_after_resume` | `Counter<long>` | `{work_item}` | Resume created second target item (deferred) | FR-028 |
| `migration.workitems.missing_after_resume` | `Counter<long>` | `{work_item}` | Mapped item absent from target after resume (deferred) | FR-029 |

---

## 2. Dimension Tags (Attributes)

### 2.1 Mandatory Tags (every instrument)

| Tag Key | Type | Values | Source | FR |
|---|---|---|---|---|
| `job.id` | `string` | UUID from `MigrationJob.JobId` | Job context | FR-004 |
| `operation` | `string` | `export` \| `import` \| `validation` | Module execution context | FR-004 |
| `module` | `string` | e.g., `WorkItems`, `Identities`, `Teams` | Module name | FR-004 |

### 2.2 Optional Tags

| Tag Key | Type | Values | Source | FR |
|---|---|---|---|---|
| `source.type` | `string` | `AzureDevOps` \| `Tfs` \| `Simulated` | Source connector type | FR-005 |

### 2.3 Forbidden Tags (FR-006)

- Work item IDs (high cardinality → traces/logs)
- User names/emails (high cardinality + PII → traces/logs)
- Revision paths (unbounded cardinality → traces/logs)

---

## 3. Meter Organisation

### 3.1 New Consolidated Meter

| Meter Name | Version | FR |
|---|---|---|
| `DevOpsMigrationPlatform.Migration` | `2.0` | FR-035 |

### 3.2 Deprecated Meters

| Meter Name | Status | Replacement |
|---|---|---|
| `DevOpsMigrationPlatform.WorkItemExport` | Deprecated | `DevOpsMigrationPlatform.Migration` |
| `DevOpsMigrationPlatform.AttachmentDownload` | Deprecated | `DevOpsMigrationPlatform.Migration` |

---

## 4. Key Entities

### 4.1 IMigrationMetrics Interface

The unified interface for recording all migration metrics. Defined in `DevOpsMigrationPlatform.Abstractions/Telemetry/`.

```csharp
public interface IMigrationMetrics
{
    // --- Execution ---
    void RecordWorkItemAttempted(in TagList tags);
    void RecordWorkItemCompleted(in TagList tags);
    void RecordWorkItemFailed(in TagList tags);
    void RecordWorkItemRetried(in TagList tags);
    void RecordWorkItemDuration(double milliseconds, in TagList tags);

    // --- Payload / Complexity ---
    void RecordFieldCount(int count, in TagList tags);
    void RecordAttachmentCount(int count, in TagList tags);
    void RecordLinkCount(int count, in TagList tags);
    void RecordRevisionCount(int count, in TagList tags);
    void RecordPayloadBytes(long bytes, in TagList tags);

    // --- Correctness (Tier 3 only) ---
    void RecordRevisionSourceCount(int count, in TagList tags);
    void RecordRevisionTargetCount(int count, in TagList tags);
    void RecordRevisionDelta(int delta, in TagList tags);
    void RecordRevisionsMissing(in TagList tags);
    void RecordRevisionOrderError(in TagList tags);
    void RecordBrokenLink(in TagList tags);
    void RecordMissingWorkItem(in TagList tags);

    // --- In-Flight ---
    void IncrementInFlight(in TagList tags);
    void DecrementInFlight(in TagList tags);

    // --- Idempotency (deferred — counters registered, not yet incremented) ---
    void RecordDuplicated(in TagList tags);
    void RecordChangedOnRerun(in TagList tags);
    void RecordReprocessedAfterResume(in TagList tags);
    void RecordDuplicatedAfterResume(in TagList tags);
    void RecordMissingAfterResume(in TagList tags);
}
```

### 4.2 MigrationTagList Helper

A static helper for constructing `TagList` values with the mandatory dimension tags.

```csharp
public static class MigrationTagList
{
    public static TagList Create(string jobId, string operation, string module)
    {
        var tags = new TagList
        {
            { "job.id", jobId },
            { "operation", operation },
            { "module", module }
        };
        return tags;
    }

    public static TagList Create(string jobId, string operation, string module, string sourceType)
    {
        var tags = Create(jobId, operation, module);
        tags.Add("source.type", sourceType);
        return tags;
    }
}
```

### 4.3 MetricSnapshot (Expanded)

The flat DTO capturing point-in-time aggregate values. All new properties use `init`-only setters.

```csharp
public record MetricSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // --- Execution counters ---
    public long WorkItemsAttempted   { get; init; }
    public long WorkItemsCompleted   { get; init; }
    public long WorkItemsFailed      { get; init; }
    public long WorkItemsRetried     { get; init; }

    // --- Execution duration ---
    public double? WorkItemDurationMeanMs { get; init; }

    // --- Payload / Complexity means ---
    public double? FieldCountMean       { get; init; }
    public double? AttachmentCountMean  { get; init; }
    public double? LinkCountMean        { get; init; }
    public double? RevisionCountMean    { get; init; }
    public double? PayloadBytesMean     { get; init; }

    // --- Correctness (Tier 3) ---
    public double? RevisionSourceCountMean  { get; init; }
    public double? RevisionTargetCountMean  { get; init; }
    public double? RevisionDeltaMean        { get; init; }
    public long RevisionsMissing            { get; init; }
    public long RevisionOrderErrors         { get; init; }
    public long BrokenLinks                 { get; init; }
    public long MissingWorkItems            { get; init; }

    // --- In-Flight ---
    public int WorkItemsInFlight     { get; init; }
    public int QueueDepth            { get; init; }

    // --- Idempotency (deferred — nullable until mapping store available) ---
    public long? Duplicated                 { get; init; }
    public long? ChangedOnRerun             { get; init; }
    public long? ReprocessedAfterResume     { get; init; }
    public long? DuplicatedAfterResume      { get; init; }
    public long? MissingAfterResume         { get; init; }
}
```

**Note**: The existing legacy properties (`WorkItemsExported`, `RevisionsExported`, `RevisionErrors`, `LinksExported`, `LinkErrors`, `AttachmentsAttempted`, `AttachmentsSucceeded`, `AttachmentsFailed`, `RevisionDurationMeanMs`, `TotalExportDurationMs`) are **removed outright** — not deprecated with `[Obsolete]`. The system is pre-production with no external consumers, so no backward-compatibility transition is needed.

---

## 5. WellKnownMetricNames (Updated Constants)

```csharp
public static class WellKnownMetricNames
{
    // --- Execution ---
    public const string WorkItemsAttempted      = "migration.workitems.attempted";
    public const string WorkItemsCompleted      = "migration.workitems.completed";
    public const string WorkItemsFailed         = "migration.workitems.failed";
    public const string WorkItemsRetried        = "migration.workitems.retried";
    public const string WorkItemDurationMs      = "migration.workitem.duration.ms";

    // --- Payload / Complexity ---
    public const string FieldCount              = "migration.workitem.fields.count";
    public const string AttachmentCount         = "migration.workitem.attachments.count";
    public const string LinkCount               = "migration.workitem.links.count";
    public const string RevisionCount           = "migration.workitem.revisions.count";
    public const string PayloadBytes            = "migration.workitem.payload.bytes";

    // --- Correctness (Tier 3) ---
    public const string RevisionSourceCount     = "migration.workitem.revisions.source.count";
    public const string RevisionTargetCount     = "migration.workitem.revisions.target.count";
    public const string RevisionDelta           = "migration.workitem.revisions.delta";
    public const string RevisionsMissing        = "migration.workitems.revisions.missing";
    public const string RevisionOrderErrors     = "migration.workitems.revision_order_errors";
    public const string BrokenLinks             = "migration.workitems.broken_links";
    public const string MissingWorkItems        = "migration.workitems.missing";

    // --- In-Flight ---
    public const string WorkItemsInFlight       = "migration.workitems.in_flight";
    public const string QueueDepth              = "migration.queue.workitems.depth";

    // --- Idempotency (deferred) ---
    public const string Duplicated              = "migration.workitems.duplicated";
    public const string ChangedOnRerun          = "migration.workitems.changed_on_rerun";
    public const string ReprocessedAfterResume  = "migration.workitems.reprocessed_after_resume";
    public const string DuplicatedAfterResume   = "migration.workitems.duplicated_after_resume";
    public const string MissingAfterResume      = "migration.workitems.missing_after_resume";
}
```

---

## 6. WellKnownMeterNames (Updated Constants)

```csharp
public static class WellKnownMeterNames
{
    /// <summary>Consolidated meter for all migration work item metrics (v2.0).</summary>
    public const string Migration = "DevOpsMigrationPlatform.Migration";

    [Obsolete("Use Migration. Will be removed in next major version.")]
    public const string WorkItemExport     = "DevOpsMigrationPlatform.WorkItemExport";

    [Obsolete("Use Migration. Will be removed in next major version.")]
    public const string AttachmentDownload = "DevOpsMigrationPlatform.AttachmentDownload";
}
```

---

## 7. State Transitions

### 7.1 Work Item Processing Lifecycle (metric emission points)

```
┌─────────────────────┐
│   Queue enqueued     │  → QueueDepth gauge reads current count
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│  Processing started  │  → RecordWorkItemAttempted + IncrementInFlight
└──────────┬──────────┘
           ▼
     ┌─────┴──────┐
     ▼            ▼
┌──────────┐  ┌──────────┐
│ Success  │  │ Failure  │
│          │  │          │
│ Completed│  │ Failed   │  → RecordWorkItemFailed + DecrementInFlight
│ + payload│  │          │    (or RecordWorkItemRetried if retryable)
│ metrics  │  └──────────┘
└──────────┘
     │
     ▼
  RecordWorkItemCompleted
  RecordWorkItemDuration
  RecordFieldCount / AttachmentCount / LinkCount / RevisionCount / PayloadBytes
  DecrementInFlight
```

### 7.2 Tier 3 Post-Flight Validation (correctness metric emission)

```
For each sampled work item (controlled by sampleRate):
  1. Read source revision count from package
  2. Read target revision count from target API
  3. Compute delta = target - source
  4. RecordRevisionSourceCount(source)
  5. RecordRevisionTargetCount(target)
  6. RecordRevisionDelta(delta)
  7. If delta < 0 → RecordRevisionsMissing
  8. Check target revision ordering → if disordered → RecordRevisionOrderError
  9. Compare link counts → if target < source → RecordBrokenLink
  10. If work item absent from target → RecordMissingWorkItem
```

---

## 8. Validation Rules

- All metric names MUST match the constants in `WellKnownMetricNames` exactly.
- All metric names MUST start with `migration.` (FR-001).
- No metric name may use underscores as separators (dots only) — except `revision_order_errors` which uses underscore within a compound word (FR-001 allows this as it's a word separator within a segment, not a namespace separator).
- Every recording call MUST include the three mandatory tags (FR-004).
- Deferred metrics MUST be registered as instruments at startup but MUST NOT be incremented until the mapping store is available (FR-025–FR-029).
- Correctness metrics MUST only be called from Tier 3 post-flight validation code (FR-024).

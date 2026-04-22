# Data Model: Resumable Work Item Batching

## Entity: BatchContinuationToken
- Purpose: Opaque state emitted by strategy and persisted by caller.
- Fields:
  - `strategyVersion` (string): Version marker for compatibility checks.
  - `primary.changedDateUtc` (string, ISO 8601 UTC): Primary chronological resume key.
  - `primary.workItemId` (integer): Secondary deterministic tie-breaker.
  - `queryFingerprint` (string): Stable fingerprint of enumeration query+parameters (SHA-256; raw query text MUST NOT be stored).
  - `generatedAtUtc` (string, ISO 8601 UTC): Diagnostic timestamp.
  - `completed` (boolean): End-of-stream marker.
  - *(v1 note: Fallback fields `batchSize`, `batchIndex`, `checksum` are deferred to a future version when a concrete consumer exists. `strategyVersion` will signal when they are added.)*
- Validation rules:
  - `primary.changedDateUtc` must parse as UTC timestamp.
  - `primary.workItemId` must be > 0.
  - `queryFingerprint` required and non-empty when resume is enabled.

## Entity: EffectiveQueryFingerprint
- Purpose: Current run fingerprint to compare against token fingerprint.
- Fields:
  - `value` (string): Deterministic hash from query text + query parameters only.
  - `algorithm` (string): Hash algorithm identifier.
- Validation rules:
  - Computation excludes caller post-fetch filters.
  - Equal input query text+params must produce equal fingerprint.

## Entity: ResumeDecision
- Purpose: Strategy decision surfaced to caller before continuation.
- Fields:
  - `status` (enum): `Accepted`, `RejectedQueryMismatch`, `Unavailable`.
  - `reason` (string): Optional machine-readable reason code.
  - `savedQueryFingerprint` (string, optional): Previous fingerprint for diagnostics.
  - `currentQueryFingerprint` (string, optional): Current fingerprint for diagnostics.
  - `tokenStrategyVersion` (string, optional): Version from supplied token.
- Validation rules:
  - `RejectedQueryMismatch` requires both saved/current fingerprint values.
  - `Unavailable` indicates missing/invalid prior token, not hard error.

## Entity: CallerResumeState
- Purpose: Caller-owned persisted state container.
- Fields:
  - `continuationToken` (`BatchContinuationToken`)
  - `itemsProcessed` (integer)
  - `duplicatesEncountered` (integer)
  - `errorsEncountered` (integer)
  - `lastSavedAtUtc` (string, ISO 8601 UTC)
- Validation rules:
  - Strategy does not persist this entity directly.
  - Caller persistence cadence is configurable at caller boundary.

## Entity: DuplicateHandlingPolicy
- Purpose: Caller-owned duplicate behavior during resume.
- Fields:
  - `mode` (enum): `DedupById`, `IdempotentWrite`, `LogOnly`, `FailOnDuplicate`.
  - `enabled` (boolean)
- Validation rules:
  - Strategy remains duplicate-tolerant regardless of mode.
  - Caller applies policy in persistence/output stage.

## State Transitions

### Resume decision flow
1. Input: `resumeEnabled=false` -> begin fresh traversal.
2. Input: `resumeEnabled=true`, token missing/invalid -> `ResumeDecision.Unavailable`.
3. Input: `resumeEnabled=true`, token present, fingerprint matches -> `ResumeDecision.Accepted`.
4. Input: `resumeEnabled=true`, token present, fingerprint mismatch -> `ResumeDecision.RejectedQueryMismatch`.

### Checkpoint progression flow
1. Strategy emits incremental continuation checkpoints during enumeration.
2. Caller persists checkpoints according to caller-defined cadence.
3. Strategy emits final completion checkpoint (`completed=true`) at end-of-stream.
4. Caller persists completion checkpoint as terminal state for safe no-op resume.

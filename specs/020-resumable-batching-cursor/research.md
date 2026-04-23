# Research: Resumable Work Item Batching

## Decision 1: Continuation token carries ordered tuple plus fallback
- Decision: Use a continuation token with primary resume key `(ChangedDateUtc, WorkItemId)`, plus fallback batch token (`batchSize`, `batchIndex`) and checksum.
- Rationale: The tuple key aligns with deterministic chronological ordering and drift tolerance. Fallback + checksum preserves compatibility for callers still relying on batch windows.
- Alternatives considered: Batch-index-only token (rejected: too fragile under source drift), work-item-ID-only token (rejected: ignores chronology and same-date ordering).

## Decision 2: Query fingerprint gates unsafe resume
- Decision: Compute and persist a query fingerprint from enumeration query text and parameters only; exclude caller post-fetch filters.
- Rationale: Enumeration contract changes must invalidate continuation safely; caller post-processing should not alter strategy-level compatibility.
- Alternatives considered: Include all caller filters in fingerprint (rejected: couples strategy to caller internals), no fingerprint check (rejected: unsafe continuation risk).

## Decision 3: Strategy returns explicit resume decision to caller
- Decision: Resume checks return a deterministic `ResumeDecision` (`Accepted`, `RejectedQueryMismatch`, `Unavailable`).
- Rationale: Caller owns mismatch policy (fail, fresh start, or log-and-continue), so strategy must expose decision details without enforcing one recovery behavior.
- Alternatives considered: Strategy auto-resets on mismatch (rejected: hides safety-critical state transition), strategy throws only (rejected: prevents policy choice).

## Decision 4: Caller owns save cadence and duplicate handling
- Decision: Strategy emits continuation checkpoints; caller chooses persistence cadence and duplicate/idempotency policy, with one mandatory completion checkpoint emitted at end-of-stream.
- Rationale: Existing module-specific persistence boundaries and policies differ by caller. Contract must enable reuse without imposing one persistence model.
- Alternatives considered: Strategy persists checkpoints directly (rejected: violates caller-owned save policy and boundary separation), strategy deduplicates internally (rejected: conflicts with caller-owned persistence semantics).

## Decision 5: Deterministic oldest-first ordering for drift tolerance
- Decision: Default traversal order is `ChangedDate` ascending, then `WorkItemId` ascending.
- Rationale: Oldest-first traversal plus tuple continuation is deterministic and naturally picks up newly edited items later in sequence.
- Alternatives considered: Newest-first ordering (rejected: higher replay churn around head of stream), unstable source-native ordering (rejected: non-deterministic resume behavior).

## Decision 6: Preserve non-resume caller behavior
- Decision: Resume behavior is opt-in via caller-controlled resume mode; non-resume callers continue with current traversal semantics.
- Rationale: Backward compatibility is mandatory (SC-004 / FR-011).
- Alternatives considered: Global switch to resumable behavior (rejected: regression risk for existing callers).

## Decision 7: Observability includes resume decisions and checkpoint progression
- Decision: Emit observable progress events for resume accepted/rejected/unavailable and checkpoint emission milestones.
- Rationale: Operations and diagnostics need explicit visibility into resume outcomes (FR-012 / SC-005).
- Alternatives considered: Log-only local diagnostics (rejected: inconsistent operational visibility across hosts).

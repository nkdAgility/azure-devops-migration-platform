# Architecture Discrepancies

**Feature**: Resumable Work Item Batching  
**Flagged by**: speckit.specify  
**Status**: Resolved

## Discrepancies

### Resumable batching continuation token contract missing
- **Source doc**: `docs/work-item-iteration-pattern.md`
- **Section**: Section 11 — Resumable Batching Contract
- **Issue**: The specification introduces caller-driven resumable batching with a continuation token and explicit resume decision outcomes, but the document did not previously define this contract.
- **Suggested update**: Add a section defining resumable batching token semantics, caller responsibilities, and backward-compatible behavior for non-resume callers.
- **Status**: Resolved — Section 11 added to `docs/work-item-iteration-pattern.md` covering BatchContinuationToken, ResumeDecision delivery, caller responsibilities, query fingerprint, ordering, and backward compatibility.

### Query fingerprint guard behavior undocumented
- **Source doc**: `.agents/context/checkpointing.md`
- **Section**: Query Fingerprint Compatibility (Resumable Batching)
- **Issue**: The specification requires query-fingerprint mismatch detection to reject unsafe continuation, but checkpointing documentation previously described only path/stage cursor semantics.
- **Suggested update**: Extend checkpointing guidance with a query-fingerprint compatibility rule and expected mismatch handling outcome.
- **Status**: Resolved — "Query Fingerprint Compatibility (Resumable Batching)" section added to `.agents/context/checkpointing.md` covering fingerprint scope, mismatch decision table, strategy version handling, and continuation token storage paths.

### Caller resume persistence responsibilities unspecified
- **Source doc**: `docs/configuration.md`
- **Section**: Resumable Batching — Operational Responsibilities
- **Issue**: The specification requires caller-owned save strategy and duplicate handling, but configuration guidance did not previously describe caller persistence expectations for continuation state.
- **Suggested update**: Document configuration and operational expectations for caller persistence cadence, duplicate handling strategy, and safe restart behavior.
- **Status**: Resolved — "Resumable Batching — Operational Responsibilities" section added to `docs/configuration.md` covering persistence cadence options, duplicate handling strategies, and safe restart guidance table.

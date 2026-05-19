# Architecture Discrepancies

**Feature**: Resumable Work Item Batching  
**Flagged by**: speckit.specify  
**Status**: Partially Resolved (re-opened by reconciliation 2026-05-16)

## Discrepancies

### Resumable batching continuation token contract missing
- **Source doc**: `docs/work-item-iteration-guide.md`
- **Section**: Section 11 — Resumable Batching Contract
- **Issue**: The specification introduces caller-driven resumable batching with a continuation token and explicit resume decision outcomes, but the document did not previously define this contract.
- **Suggested update**: Add a section defining resumable batching token semantics, caller responsibilities, and backward-compatible behavior for non-resume callers.
- **Status**: Re-opened — Section 11 exists, but currently overstates behavior versus implementation (notably `FetchAsync` mismatch-rejection safety net and fingerprint wiring in fetch path).

### Query fingerprint guard behavior undocumented
- **Source doc**: `.agents/30-context/domains/checkpointing-summary.md`
- **Section**: Query Fingerprint Compatibility (Resumable Batching)
- **Issue**: The specification requires query-fingerprint mismatch detection to reject unsafe continuation, but checkpointing documentation previously described only path/stage cursor semantics.
- **Suggested update**: Extend checkpointing guidance with a query-fingerprint compatibility rule and expected mismatch handling outcome.
- **Status**: Re-opened — checkpointing summary does not currently provide the full compatibility guidance claimed here.

### Caller resume persistence responsibilities unspecified
- **Source doc**: `docs/configuration-reference.md`
- **Section**: Resumable Batching — Operational Responsibilities
- **Issue**: The specification requires caller-owned save strategy and duplicate handling, but configuration guidance did not previously describe caller persistence expectations for continuation state.
- **Suggested update**: Document configuration and operational expectations for caller persistence cadence, duplicate handling strategy, and safe restart behavior.
- **Status**: Re-opened — section exists but includes mismatch behavior claims not currently reflected in runtime behavior.

### Resume decision safety-net wiring incomplete
- **Source doc**: `specs/020-resumable-batching-cursor/contracts/resumable-batching-contract.md`
- **Section**: ResumeDecision delivery and FR-014 safety net
- **Issue**: `EvaluateResumeDecisionAsync` exists, but `FetchAsync` does not reuse that decision path to enforce `ResumeRejectedException` on mismatch.
- **Suggested update**: Share decision evaluation logic between `EvaluateResumeDecisionAsync` and `FetchAsync`, then align docs to actual runtime behavior.
- **Status**: Re-opened — contract and tasks claim a safety-net enforcement path that is not yet implemented.


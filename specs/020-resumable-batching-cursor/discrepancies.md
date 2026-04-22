# Architecture Discrepancies

**Feature**: Resumable Work Item Batching  
**Flagged by**: speckit.specify  
**Status**: Open

## Discrepancies

### Resumable batching continuation token contract missing
- **Source doc**: `docs/work-item-iteration-pattern.md`
- **Section**: Work item iteration overview and required interfaces
- **Issue**: The specification introduces caller-driven resumable batching with a continuation token and explicit resume decision outcomes, but the document does not yet define this contract.
- **Suggested update**: Add a section defining resumable batching token semantics, caller responsibilities, and backward-compatible behavior for non-resume callers.
- **Status**: Open

### Query fingerprint guard behavior undocumented
- **Source doc**: `.agents/context/checkpointing.md`
- **Section**: Cursor schema and resume logic
- **Issue**: The specification requires query-fingerprint mismatch detection to reject unsafe continuation, but checkpointing documentation currently describes only path/stage cursor semantics.
- **Suggested update**: Extend checkpointing guidance with a query-fingerprint compatibility rule and expected mismatch handling outcome.
- **Status**: Open

### Caller resume persistence responsibilities unspecified
- **Source doc**: `docs/configuration.md`
- **Section**: Module scopes/extensions and resume-related behavior
- **Issue**: The specification requires caller-owned save strategy and duplicate handling, but configuration guidance does not yet describe caller persistence expectations for continuation state.
- **Suggested update**: Document configuration and operational expectations for caller persistence cadence, duplicate handling strategy, and safe restart behavior.
- **Status**: Open

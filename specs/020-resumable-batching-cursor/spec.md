# Feature Specification: Resumable Work Item Batching

**Feature Branch**: `020-resumable-batching-cursor`  
**Created**: 2026-04-22  
**Status**: Draft  
**Input**: User description: "Make the batching strategy resumable so callers can pass a resume flag, protect resume with a query hash, let callers own duplicate handling and save strategy, and account for data drift by ordering oldest to newest by changed date."

## Clarifications

### Session 2026-04-22

- Q: Which inputs should the resume fingerprint hash include? -> A: Query only (exclude post-fetch filters).
- Q: What key should define resume position? -> A: Tuple (ChangedDate, WorkItemId) with checksum fallback; requires ORDER BY ChangedDate, WorkItemId.
- Q: On fingerprint mismatch, what should happen? -> A: Return mismatch decision to caller (caller decides fail vs fresh-start).
- Q: What duplicate contract should spec require? -> A: Duplicates allowed; caller must handle dedup/idempotent persistence.
- Q: What persistence cadence should caller follow? -> A: Caller-defined save cadence plus mandatory completion checkpoint.

## Architecture References

- `agents.md` - Confirmed for mission, guardrail binding, and reject conditions.
- `docs/architecture.md` - Confirmed for determinism, package-first flow, and cursor-based resumability.
- `docs/work-item-iteration-guide.md` - Discrepancy logged (does not yet describe resumable batching token contract shared by callers).
- `docs/module-development-guide.md` - Confirmed for module boundary and dependency rules.
- `docs/configuration-reference.md` - Discrepancy logged (does not yet define caller-level resume token persistence expectations for resumable batching).
- `.agents/20-guardrails/core/architecture-boundaries.md` - Confirmed as applicable (rules on determinism, checkpointing, reuse patterns, and no hidden state).
- `.agents/20-guardrails/domains/workitems-rules.md` - Confirmed as applicable (cursor semantics and stage consistency).
- `.agents/20-guardrails/domains/migration-rules.md` - Confirmed as applicable (streaming, deterministic ordering, and checkpoint constraints).
- `.agents/20-guardrails/core/coding-standards.md` - Confirmed as applicable (async/cancellation, immutability, and abstraction boundaries).
- `.agents/20-guardrails/workflow/testing-rules.md` - Confirmed as applicable (system-test and deterministic test expectations).
- `.agents/20-guardrails/domains/module-rules.md` - Confirmed as applicable (cursor schema and resume behavior requirements).
- `.agents/20-guardrails/domains/control-plane-rules.md` - Confirmed as applicable for topology neutrality.
- `.agents/20-guardrails/workflow/test-first-workflow.md` - Confirmed as applicable for downstream implementation workflow.
- `.agents/20-guardrails/workflow/acceptance-test-format.md` - Confirmed as applicable for feature acceptance formatting.
- `.agents/30-context/domains/cli-commands.md` - Confirmed as applicable for CLI-level exposure boundaries.
- `.agents/30-context/domains/migration-package-concept.md` - Confirmed as applicable for checkpoint location and package invariants.
- `.agents/30-context/domains/job-lifecycle.md` - Confirmed as applicable for durable job contract expectations.
- `.agents/30-context/domains/telemetry-model.md` - Confirmed as applicable for observable resume progress.
- `.agents/30-context/domains/workitems-format-summary.md` - Confirmed as applicable for canonical ordering behavior.
- `.agents/30-context/domains/import-streaming.md` - Confirmed as applicable for streaming and idempotent stage processing.
- `.agents/30-context/domains/checkpointing-summary.md` - Discrepancy logged (does not yet define resumable batching continuation token semantics).
- `.agents/30-context/domains/package-manager.md` - Confirmed as applicable for state and artifact boundary rules.
- `.agents/30-context/domains/identity-and-mapping.md` - Confirmed as applicable for idempotency and duplicate-safe import behavior.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume long-running iteration safely (Priority: P1)

As an operator running dependency or discovery operations over very large projects, I need interrupted batching iteration to resume close to where it stopped so that I do not lose many hours of progress.

**Why this priority**: This directly addresses the largest current operational pain (full-project restart after interruption).

**Independent Test**: Can be tested by stopping a long-running iteration midway, restarting with resume enabled, and verifying that processing continues near the saved continuation point instead of reprocessing the full project.

**Acceptance Scenarios**:

1. **Given** a job with a saved continuation token for a project, **When** the caller restarts with resume enabled, **Then** batching continues from the saved continuation position.
2. **Given** no continuation token exists, **When** the caller runs with resume enabled, **Then** batching starts from the beginning without error.

---

### User Story 2 - Prevent unsafe resume after query changes (Priority: P1)

As an operator, I need resume attempts to be rejected when the effective query has changed so that old checkpoints are not applied to different result sets.

**Why this priority**: Query mismatch would silently corrupt progress semantics and can lead to missed or duplicated processing.

**Independent Test**: Can be tested by saving a continuation token, changing query inputs, and verifying the next run refuses continuation and starts fresh (or explicitly reports mismatch).

**Acceptance Scenarios**:

1. **Given** a saved continuation token with query hash H1, **When** a caller invokes resume with an effective query hash H2 where H2 != H1, **Then** continuation is rejected and caller receives a deterministic mismatch outcome.
2. **Given** a saved continuation token with query hash H1, **When** the caller invokes resume with effective query hash H1, **Then** continuation is accepted.

---

### User Story 3 - Handle duplicates and data drift predictably (Priority: P2)

As a module owner, I need deterministic ordering and explicit duplicate-tolerant behavior so that resumable processing remains correct even when source data changes between runs.

**Why this priority**: Source work items can be edited during long runs; correctness must be preserved despite drift.

**Independent Test**: Can be tested by processing part of an ordered stream, mutating some source items, resuming, and verifying no missed records while caller deduplication or idempotent handling prevents incorrect outcomes.

**Acceptance Scenarios**:

1. **Given** ordered processing by oldest changed data first, **When** source items are edited after the cursor has advanced, **Then** new updates appear later in order and are processed on subsequent continuation.
2. **Given** resumed processing may surface already-seen item identifiers, **When** caller duplicate handling is enabled, **Then** persisted outputs remain logically correct and free from harmful duplication.
3. **Given** 500 work items with an identical ChangedDate, **When** resume is requested at item 250, **Then** all 500 items are eventually processed with none skipped (FR-013 boundary cluster correctness).
4. **Given** >20,000 items exist between the saved continuation date and now, **When** resume starts, **Then** the window strategy subdivides correctly and all items are enumerated without exceeding the WIQL result limit.

### Edge Cases

- Resume requested with malformed or non-decodable continuation token.
- Resume requested with valid token but incompatible strategy version.
- Caller enables resume but has no durable state save from prior run.
- Source edits reorder some candidates around the continuation boundary.
- Multiple interruptions happen before a full project completes.
- Resume position falls within a cluster of hundreds of work items sharing an identical ChangedDate (e.g. bulk migration). The strategy MUST NOT skip any items in the cluster.
- Resume with >20,000 items between the saved date and the current date — window subdivision must handle the ADO WIQL 20K result limit correctly from the resume position.
- Continuation token persisted weeks ago — source data may have changed significantly (items deleted, project renamed).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide resumable batching strategy behavior that can continue iteration from a caller-supplied continuation token.
- **FR-002**: The system MUST support a caller-controlled resume mode switch so callers can explicitly choose resume or fresh traversal.
- **FR-003**: The system MUST include a deterministic query fingerprint in continuation state based on the query text and parameters used for enumeration. Fingerprint MUST exclude post-fetch filters applied by the caller (filters are caller-level post-processing, not enumeration contract).
- **FR-004**: The system MUST reject continuation when the current query fingerprint does not match the saved fingerprint and MUST return a `ResumeDecision` outcome (containing mismatch marker, original query, and current query details) to the caller. Caller decides whether to fail, discard checkpoint and start fresh, or log and continue.
- **FR-005**: The system MUST support caller-defined mismatch recovery policy. Rejection of unsafe resume is mandatory; the form of recovery (fail, fresh-start, log-only) is delegated to the caller.
- **FR-006**: The system MUST emit continuation progress in a form the caller can persist at its chosen checkpoint frequency. Strategy does not save state; caller is responsible for persisting checkpoints. Strategy MUST guarantee that a final completion checkpoint (emitted after iteration concludes) contains unambiguous end-of-stream markers sufficient for the caller to detect and resume from completion.
- **FR-007**: The system MUST preserve deterministic traversal ordering compatible with existing streaming and checkpointing guardrails. Default ordering: ChangedDate ascending, then WorkItemId ascending (oldest first; handles source drift by continuing to recent updates).
- **FR-013**: When resuming, the WIQL resume predicate MUST use `[System.ChangedDate] >= @savedDate` (inclusive) to avoid skipping items that share the same ChangedDate as the resume position. The first resumed window MUST apply an in-memory post-filter to exclude items with `(ChangedDate, WorkItemId) <= (savedDate, savedId)` so that no items are skipped or double-processed in the boundary cluster. This ensures correctness even when hundreds of items share an identical timestamp.
- **FR-014**: The system MUST provide a non-throwing pre-check method (`EvaluateResumeDecisionAsync`) that returns `ResumeDecision` before enumeration begins, so callers can inspect the decision and choose recovery without catching exceptions. `ResumeRejectedException` remains as a safety net for callers that skip the pre-check.
- **FR-015**: The `ContinuationCheckpointWriter` callback MUST be treated as an atomic-or-idempotent operation by callers. The strategy MUST document that callers are responsible for ensuring checkpoint writes are atomic (e.g. write-then-rename) or idempotent, so that cancellation during the callback does not corrupt persisted state.
- **FR-008**: The system MUST allow caller-owned duplicate handling. Strategy does not deduplicate; duplicates (item IDs appearing multiple times under resume + source drift) MUST be tolerated. Caller is responsible for idempotent persistence, deduplication, or explicit duplicate-handling policy to prevent incorrect outcomes.
- **FR-009**: The system MUST tolerate source data drift between runs without creating non-deterministic resume decisions.
- **FR-010**: The system MUST remain memory-safe for large datasets and MUST NOT require loading full result sets to resume.
- **FR-011**: The system MUST preserve existing behavior for callers that do not opt into resumable batching.
- **FR-012**: The system MUST provide observable progress and resume decisions (accepted, rejected, fresh-start) for diagnostics and operations.

### Key Entities *(include if feature involves data)*

- **BatchContinuationToken**: Opaque continuation state emitted by the batching strategy containing:
  - **PrimaryKey**: Tuple of (ChangedDate, WorkItemId) ordered chronologically; fast-forwards resume position to this key on subsequent enumeration.
  - **QueryFingerprint**: Stable hash of the enumeration query (WIQL + parameters).
  - **Timestamp**: ISO 8601 generation time for diagnostic ordering.
  - **Completed**: End-of-stream marker (boolean).
  - *(Fallback fields `batchSize`, `batchIndex`, `checksum` removed from v1 — will be added in a future version when a concrete consumer exists.)*
- **EffectiveQueryFingerprint**: Deterministic hash derived from query text and parameters used for enumeration (excluding post-fetch filters). Updated on each resume check.
- **CallerResumeState**: Caller-side reference model (not implemented by the strategy layer). Caller-managed persisted state including:
  - Continuation token (primary key).
  - Query fingerprint from prior run.
  - Caller-specific counters (items processed, duplicates encountered, errors).
- **ResumeDecision**: Outcome model indicating:
  - `Accepted`: Token is valid, continuation allowed.
  - `Rejected (QueryMismatch)`: Query fingerprint mismatch; includes original and current query details.
  - `Unavailable`: No prior token exists; begin from start.
- **DuplicateHandlingPolicy**: Caller-side reference model (not implemented by the strategy layer). Caller-selected behavior (dedup-by-ID, idempotent-write, log-only, fail-on-duplicate) applied during resume processing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In interruption tests on large projects, resumed runs process at least 90% fewer previously completed items than fresh reruns. *(Note: The window-skipping mechanism skips all prior windows entirely, yielding 100% skip of already-processed batches. This is verified by mechanism tests in T018/T019 — no separate percentage-threshold test is required.)*
- **SC-002**: 100% of query-change resume attempts are detected and rejected with explicit mismatch signaling.
- **SC-003**: In source-drift test scenarios, 100% of expected items are eventually processed with no missed items attributable to resume position logic.
- **SC-004**: Existing non-resume callers show no behavioral regression in functional and system tests.
- **SC-005**: Resume-related progress and decision events are available for operational troubleshooting in all test runs.

## Assumptions

- The initial feature scope is resumable batching support for work-item iteration callers that already own module-level checkpoints.
- The continuation token is for fetch-level callers (inventory, dependency analysis, discovery) only. The `WorkItemExportOrchestrator` continues to use folder-level cursors (`workitems.cursor.json`) and does NOT use continuation tokens.
- The TFS Object Model subprocess (`.NET 4.8`) is out of scope for resumable batching. TFS export uses its own process model and does not participate in continuation token semantics.
- Continuation token file paths (`ContinuationFile()`) MUST be scoped by caller/module name (e.g. `inventory.continuation.json`, `dependency.continuation.json`) to prevent concurrent callers from corrupting each other's tokens.
- Caller systems are responsible for persisting continuation state and implementing duplicate-safe output behavior.
- Callers MUST ensure `ContinuationCheckpointWriter` callback writes are atomic (e.g. write-then-rename) or idempotent, so that cancellation during the callback does not produce corrupt persisted tokens.
- When a continuation token's `GeneratedAtUtc` is older than a configurable threshold (default 7 days), the strategy SHOULD emit a warning-level log indicating potential source data staleness. This does not block resume but makes the risk observable.
- A malformed or corrupt continuation token results in `ResumeDecision.Unavailable` with a warning-level log (not silent) to alert the operator of potential storage issues.
- Ordering by oldest changed data first remains the canonical traversal direction for drift-tolerant continuation.
- Query fingerprinting uses query text used for enumeration and explicitly excludes post-fetch filters.
- This feature does not alter canonical package layout, stage ordering, or checkpoint storage location rules.
- Guardrails applied for this feature: deterministic ordering, streaming processing, cursor-based progress via existing state boundaries, abstraction reuse, and no hidden state outside checkpoint stores.
- Explicitly rejected approaches:
  - Any design that resumes across changed query semantics without fingerprint validation.
  - Any design that requires full in-memory result buffering to compute resume position.
  - Any design that introduces hidden progress state outside approved checkpoint/state storage.
  - Any design that bypasses existing work-item iteration abstraction boundaries.
- Files reviewed for this specification include:
  - `agents.md`
  - `docs/architecture.md`
  - `docs/work-item-iteration-guide.md`
  - `docs/module-development-guide.md`
  - `docs/configuration-reference.md`
  - `.agents/20-guardrails/core/architecture-boundaries.md`
  - `.agents/20-guardrails/domains/workitems-rules.md`
  - `.agents/20-guardrails/domains/migration-rules.md`
  - `.agents/20-guardrails/core/coding-standards.md`
  - `.agents/20-guardrails/workflow/testing-rules.md`
  - `.agents/20-guardrails/domains/module-rules.md`
  - `.agents/20-guardrails/domains/control-plane-rules.md`
  - `.agents/20-guardrails/workflow/test-first-workflow.md`
  - `.agents/20-guardrails/workflow/acceptance-test-format.md`
  - `.agents/30-context/domains/cli-commands.md`
  - `.agents/30-context/domains/migration-package-concept.md`
  - `.agents/30-context/domains/job-lifecycle.md`
  - `.agents/30-context/domains/telemetry-model.md`
  - `.agents/30-context/domains/workitems-format-summary.md`
  - `.agents/30-context/domains/import-streaming.md`
  - `.agents/30-context/domains/checkpointing-summary.md`
  - `.agents/30-context/domains/package-manager.md`
  - `.agents/30-context/domains/identity-and-mapping.md`
- Gaps identified and captured in discrepancies:
  - No explicit documented contract yet for resumable batching continuation token behavior.
  - No explicit documented query-fingerprint mismatch handling for caller-driven resume.


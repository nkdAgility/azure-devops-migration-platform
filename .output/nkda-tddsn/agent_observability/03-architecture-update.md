# Architecture Update Proposal: agent_observability

## 1. Established Architecture

agent_observability is a cross-cutting subsystem responsible for progress events, diagnostics, traces, and metric snapshots. The established architecture routes runtime agent observability through package sinks and Control Plane sinks, while clients consume Control Plane APIs for progress, diagnostics, and telemetry. DiagnosticLogStore mirrors JobProgressStore: per-job bounded ring buffers, live SSE subscribers, and terminal stream completion.

## 2. Proposed Clarifications

No canonical architecture document edit is required for this pass. The existing architecture document already names diagnostics transport as a subsystem responsibility, and the client/TUI docs describe diagnostics streaming. The tests clarify the executable contract that DiagnosticLogStore applies deployment-level minimum filtering before buffering and client-level filtering when snapshots are requested.

## 3. Behavioural Contracts

- DiagnosticLogStore retains only the newest configured number of diagnostic records per job.
- DiagnosticLogStore discards records below DiagnosticLogStoreOptions.MinimumLevel before buffering or notifying subscribers.
- DiagnosticLogStore.GetSnapshot(jobId, levelFilter) returns only records at or above the requested level.
- DiagnosticLogStore.Subscribe(jobId) receives newly-added records without requiring polling.
- DiagnosticLogStore.CompleteJob(jobId, failed) records terminal state and causes late subscribers to complete immediately.

## 4. State Transitions

- Missing job entry -> active diagnostic entry on first Add or Subscribe.
- Active diagnostic entry -> terminal completed state on CompleteJob.
- Terminal completed state -> immediate completed channel for future subscribers.
- Active buffer with capacity N -> eviction of oldest retained record when adding record N+1.

## 5. Failure and Rejection Behaviour

- Unparseable diagnostic log levels are ignored by the store.
- Records below deployment minimum are rejected before retention.
- Failed terminal jobs are distinguishable through WasFailed(jobId) for terminal SSE event selection.

## 6. Observability Behaviour

The store itself is an observability transport component, so the key behaviour is preserving operator-visible diagnostics while preventing excessive memory growth and low-severity/customer-risk leakage. These tests do not add new telemetry emission points; they protect the in-memory diagnostic transport that feeds GET /jobs/{jobId}/diagnostics and follow=true streams.

## 7. Testability Seams

DiagnosticLogStore already has a deterministic seam through IOptions<DiagnosticLogStoreOptions>. No new production seam is required. Tests use fixed job IDs and real in-memory channels.

## 8. Open Questions

- Should DiagnosticsController receive feature-level Reqnroll coverage parallel to ProgressController for authorization and unknown jobs? This pass leaves that as follow-up because the target gap is store behaviour.
- Should invalid client level query strings return 400 instead of silently disabling filtering? Current implementation silently ignores invalid values; this pass treats that as established behaviour until a product decision changes it.

# Architecture Update: agent_lease_coordination
## 1. Architecture Narrative
The lease coordination architecture is a shared base-worker lifecycle. `AgentWorkerBase` owns polling, active lease publication, active job publication, dispatch, post-job flush, and cleanup. Concrete workers own job-kind dispatch and terminal signalling details.
The critical invariant is that active lease/package state is scoped to exactly one acquired job. Any exit from dispatch other than host cancellation must release that state before the worker loop retries, otherwise telemetry, progress, logs, package run ids, and follow-up jobs can observe stale context.
## 2. Proposed Documentation Change
No canonical architecture documentation file is changed in this pass. The existing architecture summary already states that `ActiveLeaseState` is cleared after complete/fail. This rebuild strengthens implementation and tests to match that documented lifecycle for failure exits too.
## 3. Boundary Review
- Clean Architecture: production change remains inside infrastructure worker lifecycle; no domain dependency is introduced.
- Hexagonal: no SDK or external API calls are added; the fake HTTP boundary is test-only.
- Modular Monolith: no cross-module coupling is added.
- Vertical Slice: tests target the lease-coordination behaviour directly.
- Screaming Architecture: test and artifact names use `agent_lease_coordination` and `AgentWorkerBaseLeaseCoordination` language.
## 4. Minimal Change Gate Input
The only production change should be in `AgentWorkerBase.PollAndExecuteAsync`: wrap dispatch/post-job flush/cleanup in `try/finally` so cleanup runs on success and unexpected dispatch failure.

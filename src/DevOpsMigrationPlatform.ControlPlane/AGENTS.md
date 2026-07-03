# Control Plane — Directory Rules

This project coordinates jobs, leases, and telemetry. It never migrates anything.

## ⛔ Blocking rules

1. No migration logic here — no module calls, no phase execution, no connector SDK usage. (ADR-0004)
2. Never write to or cache the migration package. The Control Plane is read-only toward customer data. (ADR-0005)
3. The sole agent-telemetry ingestion point is `POST /workers/{workerId}/events` (`WorkerEventsController`). Do not add per-signal telemetry endpoints — the seven legacy `/agents/lease/{leaseId}/*` telemetry routes were deleted and must not reappear. (ADR-0020)
4. Client job streaming is only `GET /jobs/{jobId}/stream?from={seq}` — append-only stores, full replay, no eviction below the warned cap.
5. Job/lease state changes go through the store interfaces (`IJobStore`); no ad-hoc state elsewhere.
6. Config payloads are routed opaquely — never inspect or proxy configuration fields. (ADR-0008)

## Authority

- Rules: `.agents/20-guardrails/domains/control-plane-rules.md`, `.agents/20-guardrails/domains/security-rules.md`
- Wire contract: `.agents/10-contracts/specs/observability-transport-contract.md`, `.agents/10-contracts/specs/lease-coordination-contract.md`
- Explanation: `docs/control-plane.md`

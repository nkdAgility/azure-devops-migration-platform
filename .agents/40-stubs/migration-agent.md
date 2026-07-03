# Migration Agent Host — Directory Rules

The agent host: leases jobs, materialises config, runs the plan executor, ships telemetry.

## ⛔ Blocking rules

1. This is the only component (with the TFS agent) that writes the package. Config is materialised from `Job.ConfigPayload` to `migration-config.json` after lease acquisition, before any module runs. (ADR-0005/0008)
2. All telemetry leaves through `UnifiedWorkerEventWriter` — batched, acknowledged, sequence-numbered `POST /workers/{workerId}/events`. Never add a direct HTTP telemetry path. Terminal signals flush immediately. (ADR-0020)
3. The only other agent-originated calls are lease acquisition and heartbeat.
4. Execution is plan-driven: build from `IModule.DependsOn`, persist `plan.json` before the first module, resume from persisted state, push the task list before work begins. (ADR-0010)
5. Job dispatch switches on `Job.Kind` — no parallel job type hierarchies. (ADR-0009)

## Authority

- Rules: `.agents/20-guardrails/domains/migration-rules.md`
- Contracts: `.agents/10-contracts/specs/observability-transport-contract.md`, `.agents/10-contracts/specs/task-execution-contract.md`, `.agents/10-contracts/specs/lease-coordination-contract.md`
- Explanation: `docs/agent-hosting.md`

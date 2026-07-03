# Azure DevOps Connector — Directory Rules

ADO REST SDK mechanics live here, behind adapter interfaces. Nothing else does.

## ⛔ Blocking rules

1. Adapters own SDK mechanics only — no orchestration, no sequencing, no transformation logic, no phase policy. One adapter type carries both read (`Get{Thing}Async`) and write (`Create/Update{Thing}Async`) methods.
2. Adapter interfaces (`I{Domain}Adapter`) live in Abstractions; implementations here. Modules and orchestrators never call the SDK directly.
3. Stream API results — enumerate pages lazily; never materialise full result sets for unbounded queries.
4. Credentials come from configuration/job contract — never hard-coded, never logged. Safe logging rules apply to all request/response logging.
5. Every capability implemented here needs its Simulated counterpart and a `SystemTest`-family test proving real side effects (exists → create → exists). Completion without runtime evidence is blocked.
6. High-cardinality identifiers (work item IDs, revision indexes) never appear as metric tags. (ADR-0011)

## Authority

- Rules: `.agents/20-guardrails/domains/connector-rules.md`, `.agents/20-guardrails/domains/security-rules.md`
- Explanation: `docs/connector-development-guide.md`, `docs/client-integration-guide.md`

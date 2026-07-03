# TFS / net481 Boundary — Directory Rules

This subtree is the only place .NET Framework 4.8.1 and the TFS Object Model may live.

## ⛔ Blocking rules

1. .NET Framework use is isolated here — `TfsMigrationAgent` and `Infrastructure.TfsObjectModel` must never be referenced from .NET 10 projects. (ADR-0007)
2. All features are implemented on net481 — never skipped, degraded, or guarded away. Guard clauses exist only for genuine cross-runtime crash prevention; narrow differing implementations to the smallest seam instead. (ADR-0018)
3. The TFS agent uses the same `IModule` dispatch and writes the package only via `IPackageAccess`.
4. Telemetry ships through `UnifiedWorkerEventWriter` exactly like the .NET 10 agent — no legacy HTTP paths. (ADR-0020)
5. TFS connectors that do not support a concern register `ConnectorCapability.None` and no adapter — extensions skip via the capability flag, never via null-guards.
6. Jobs with `source.type: TeamFoundationServer` route via capability matching (`capabilities=tfs`); the agent is Windows-only and not containerised.
7. TFS exemptions (API genuinely absent) are documented with a structured warning in code — silent gaps are non-compliant.

## Authority

- Rules: `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md`, `.agents/20-guardrails/domains/connector-rules.md`
- Explanation: `docs/agent-hosting.md`, `docs/capabilities-guide.md`

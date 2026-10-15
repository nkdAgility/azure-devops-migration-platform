# TFS / net481 Boundary — Directory Rules

This subtree is the only place .NET Framework 4.8.1 and the TFS Object Model may live.

## ⛔ Blocking rules

1. .NET Framework use is isolated here — `TfsMigrationAgent` and `Infrastructure.TfsObjectModel` must never be referenced from .NET 10 runtime projects. (ADR-0007) The only exception is the net10 `SchemaGenerator` build tool, which references `Infrastructure.TfsObjectModel` for options-schema generation.
2. All features are implemented on net481 — never skipped, degraded, or guarded away. Guard clauses exist only for genuine cross-runtime crash prevention; narrow differing implementations to the smallest seam instead. (ADR-0018) **Known current violations** (tracked as backlog items; do not add more): `#if !NET481` blocks in shared code skip WorkItems comments/filters/WIQL on export, embedded images, and Nodes/Identities/Teams import.
3. The TFS agent uses the same `IModule` dispatch and writes the package only via `IPackageAccess`.
4. Telemetry ships through `UnifiedWorkerEventWriter` exactly like the .NET 10 agent — no legacy HTTP paths. (ADR-0020)
5. TFS connectors that do not support a concern register `ConnectorCapability.None` and no adapter — extensions skip via the capability flag, never via null-guards. **Known current violation:** `TfsNullBoardAdapter` (board settings) — tracked as a backlog item.
6. The TFS agent advertises the single capability `TeamFoundationServer` (`?capabilities=TeamFoundationServer`) and receives only jobs whose every connector is `TeamFoundationServer`; the CLI derives a job's connectors from both `Source.Type` and `Target.Type`, so a TFS-source/ADO-target config is not routable to either agent today. It handles `Export`, `Import` (work items only), `Inventory` and `Dependencies`. Windows-only and not containerised; spawned once by `AgentLifecycleService` (no restart).
7. TFS exemptions (API genuinely absent) are documented with a structured warning in code — silent gaps are non-compliant.

## Authority

- Rules: `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md`, `.agents/20-guardrails/domains/connector-rules.md`
- Explanation: `docs/agent-hosting.md`, `docs/capabilities-guide.md`

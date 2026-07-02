# Infrastructure.Agent — Directory Rules

Modules, orchestrators, extensions, and tools live here. This is where migration logic executes.

## ⛔ Blocking rules

1. Follow the execution hierarchy: Module (thin, builds extension list) → Orchestrator (entity loop, cursor, metrics) → Extension (one capability, own `IOptions<T>`) → Adapter/Tool. Never blur the layers. Full model: `docs/execution-model.md`.
2. Stream, never materialize — one revision at a time; no loading all items into memory, no global in-memory sorting.
3. All package I/O goes through `IPackageAccess` — never the filesystem directly, never rebuilt path logic over `IArtefactStore`/`IStateStore`. (ADR-0016)
4. Cursor-based resume for every loop: write cursor state after each unit of work; re-runs seek to cursor. (ADR-0003)
5. One orchestrator per concern — never split by phase; one type carries both `ExportAsync` and `ImportAsync` at every layer.
6. Extensions must pass the Extension Seam Ethos ("if this is absent, is the entity still whole?"); intrinsic concerns go inline in the core pipeline. (ADR-0019)
7. Capability guard: check `IConnectorCapabilityProvider.Has(...)` before adapter calls — absence returns `Skipped`, never throws, never null-guards the adapter.
8. Telemetry obligations O-1..O-4 at every layer (span, metrics, structured logs, progress events).
9. Guard clauses only for net481-vs-modern-runtime crash prevention — no defensive null-service or enablement guards. (ADR-0018)

## Authority

- Rules: `.agents/20-guardrails/domains/module-rules.md`, `.agents/20-guardrails/domains/migration-rules.md`, `.agents/20-guardrails/core/capability-ethos-rules.md`
- Contract: `.agents/10-contracts/specs/execution-contract.md`
- Explanation: `docs/execution-model.md`, `docs/module-development-guide.md`

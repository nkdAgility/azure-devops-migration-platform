# CLI / TUI — Directory Rules

This project submits jobs and presents progress. It never migrates anything and never touches the package.

## ⛔ Blocking rules

1. Never write package artefacts — no `IArtefactStore`/`IStateStore` write calls from CLI/TUI code. (ADR-0005)
2. Commands delegate to services; no business logic in `Command` classes.
3. Read counters from `GET /jobs/{id}/telemetry`, never from `ProgressEvent.Metrics` (null on .NET 10 agents = silent zeros).
4. Subscribe to the unified stream `GET /jobs/{jobId}/stream?from={seq}`; never wire an in-process `IProgressSink`, never call removed per-signal endpoints. (ADR-0020)
5. UI is mode-driven: job `Kind` selects the view family; `queue --follow` and `manage status` share the mapping. Evaluate presentation changes against `docs/ui-mode-contract.md` before completion. (ADR-0015)
6. Tier 0/1 validation runs before submission — structural checks locally, connectivity checks before any Control Plane call. (ADR-0021)
7. Project references stay within the compiler-enforced topology — never reference MigrationAgent, ControlPlane, or connector assemblies. (ADR-0007)
8. Every CLI command has a `SystemTest`-family test asserting observable output.

## Authority

- Rules: `.agents/20-guardrails/domains/cli-tui-rules.md`
- Contract: `docs/ui-mode-contract.md`
- Explanation: `docs/cli-guide.md`, `docs/tui-guide.md`

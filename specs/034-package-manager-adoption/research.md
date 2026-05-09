# Research: Package Manager Adoption

## Decision 1: Adopt a typed package boundary (`IPackage`) as the caller-facing contract

**Decision**: Introduce and standardize runtime package access through `IPackage` with typed contexts for content, metadata, and run logs.

**Rationale**: Current runtime code composes package paths directly in multiple places (`CheckpointingService`, `JobExecutionPlanBuilder`, `JobPlanExecutor`, `PackageProgressSink`, `PackageLoggerProvider`, orchestrators). A typed boundary centralizes canonical routing and removes duplicated path logic.

**Alternatives considered**:

- Keep direct `IArtefactStore`/`IStateStore` usage and only share helper methods — rejected because it preserves fragmented path ownership and drift risk.
- Wrap only `IArtefactStore` — rejected because authoritative state also lives in `IStateStore`.

## Decision 2: Keep `IArtefactStore` and `IStateStore` as persistence primitives, not caller APIs

**Decision**: `IPackage` composes existing stores; modules and orchestrators migrate to package intents while persistence internals continue using raw stores.

**Rationale**: This preserves connector/storage backend behavior and avoids rewriting stable persistence implementations while still achieving boundary ownership of package semantics.

**Alternatives considered**:

- Replace store interfaces entirely — rejected due to high migration risk and unnecessary churn.

## Decision 3: Preserve canonical routing with explicit categories

**Decision**: Route by typed intent:

- Authoritative package metadata/state: root `.migration/` and project `/{org}/{project}/.migration/`
- Run-scoped audit copies: `.migration/runs/<runId>/...` only where required
- Run log streams: `.migration/runs/<runId>/logs/progress.jsonl` and `agent*.jsonl`

**Rationale**: Matches existing guardrails and avoids conflating authoritative state with run-scoped audit data.

**Alternatives considered**:

- Store all metadata under run scope only — rejected (breaks resume/phase-gate authority).
- Collapse logs into metadata persistence APIs — rejected (append stream semantics differ).

## Decision 4: Migrate high-impact runtime seams first

**Decision**: Prioritize migration of these seams:

1. Plan and checkpoint persistence (`JobExecutionPlanBuilder`, `JobPlanExecutor`, `CheckpointingService`)
2. Package config materialization (`PackageConfigStore`)
3. Diagnostics/progress append sinks (`PackageLoggerProvider`, `PackageProgressSink`)
4. Remaining module/orchestrator raw path composition

**Rationale**: These seams define authoritative state and observability behavior; moving them first gives maximum architecture and correctness value.

**Alternatives considered**:

- Migrate low-risk leaf modules first — rejected because architectural drift remains in core orchestration paths.

## Decision 5: Validate against connector parity and no-regression guarantees

**Decision**: Require test coverage ensuring equivalent semantics for Simulated, AzureDevOpsServices, and TeamFoundationServer execution paths (where capability applies), with unchanged resume and phase-gate behavior.

**Rationale**: Connector parity and deterministic resume are constitution/guardrail red lines.

**Alternatives considered**:

- Defer TFS parity validation to follow-up — rejected by connector coverage guardrails.

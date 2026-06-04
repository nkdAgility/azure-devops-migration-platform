# Discrepancies: Close DSL Migration Gaps

**Feature**: [spec.md](spec.md)
**Purpose**: Track discrepancies discovered between the specification/plan/tasks and the
actual codebase during implementation. The Spec-Completion Gate (constitution Governance)
requires every entry here to be `Resolved` or `N/A` before the branch may merge.

## Status

| ID | Discrepancy | Discovered During | Status | Resolution |
|----|-------------|-------------------|--------|------------|
| D-000 | No implementation-time discrepancies recorded yet. | `/speckit-analyze` (2026-06-04) | N/A | Cross-artifact analysis findings (F1–F10) were resolved in spec.md, plan.md, and tasks.md prior to implementation. Add new rows here as code-vs-spec discrepancies surface during Phases 2–8. |
| D-003 | data-model/T010 specified `string? Match(sourceIdentity, sourceDisplayName, candidates, ILogger logger)` — the strategy takes an `ILogger` and logs ambiguity itself. | WP2.1 (2026-06-04) | Resolved | **Resolution:** Strategy kept **pure** (no I/O, no logging — SRP, constitution IX/X). Signature is `IdentityMatch Match(string sourceUpn, string sourceDisplayName, IReadOnlyList<IdentityCandidate> candidates)`, returning `IdentityMatch(Descriptor, MatchCount)`. Ambiguity is surfaced via `MatchCount > 1` and **logged by the orchestrator** (where `ILogger` already lives), not the strategy. Avoids adding a `Microsoft.Extensions.Logging` ref to `Abstractions.Agent`. Strategy unit tests assert `MatchCount`/`IsAmbiguous` instead of a logger mock. |
| D-002 | T005 specified an explicit net481 `ImportAsync` impl (e.g. `TfsIdentitiesOrchestratorAdapter`) returning `Task.CompletedTask` + Warning. In practice `IdentitiesOrchestrator` (Infrastructure.Agent) multi-targets net481 and its `ImportAsync` body is net481-safe, so a separate adapter is unnecessary. | WP1b (2026-06-04) | Resolved | **Resolution:** FR-020 satisfied by removing the interface-level `#if !NET481` guard (now unconditional) and the DI-hiding field/param guards in `IdentitiesModule` (FR-018). The reduced net481 import capability is modelled at the call site: `IdentitiesModule.ImportAsync` keeps a *compliant* `#if NET481` capability branch returning `Skipped` (graceful degradation, not interface/DI hiding). No separate net481 orchestrator adapter created. Build green on net10 + net481. |
| D-001 | `IIdentityLookupTool` is referenced in **16 source files**, not the 4 consumers enumerated in FR-016 / tasks T029–T033. Additional consumers: `IRevisionFolderProcessorFactory.cs` and `IWorkItemsOrchestratorFactory.cs` (Abstractions.Agent interfaces), `RevisionFolderProcessorFactory.cs`, `WorkItemsImportRuntime.cs`, `WorkItemsOrchestratorFactory.cs`, `ModuleServiceCollectionExtensions.cs`, plus `IdentitiesOrchestrator.cs`. | Phase 2 prep (2026-06-04) | Resolved | **Resolution (operator decision):** FR-016 changed from delete-and-recreate to a **rename** (`git mv` + symbol rename) to preserve git history. The rename mechanically covers all 16 files in one pass, keeping the build green, instead of a breaking deletion. FR-016 and WP1 updated accordingly. Verified when the WP1 build gate confirms zero `IIdentityLookupTool` references and a green build. |

> Add a row for every discrepancy found while implementing tasks (e.g. an interface that
> differs from the data-model, a file path that does not exist, a behaviour the spec did not
> anticipate). Each row MUST reach `Resolved` or `N/A` before T086 is checked off.

# Discrepancies: Close DSL Migration Gaps

**Feature**: [spec.md](spec.md)
**Purpose**: Track discrepancies discovered between the specification/plan/tasks and the
actual codebase during implementation. The Spec-Completion Gate (constitution Governance)
requires every entry here to be `Resolved` or `N/A` before the branch may merge.

## Status

| ID | Discrepancy | Discovered During | Status | Resolution |
|----|-------------|-------------------|--------|------------|
| D-000 | No implementation-time discrepancies recorded yet. | `/speckit-analyze` (2026-06-04) | N/A | Cross-artifact analysis findings (F1–F10) were resolved in spec.md, plan.md, and tasks.md prior to implementation. Add new rows here as code-vs-spec discrepancies surface during Phases 2–8. |
| D-001 | `IIdentityLookupTool` is referenced in **16 source files**, not the 4 consumers enumerated in FR-016 / tasks T029–T033. Additional consumers: `IRevisionFolderProcessorFactory.cs` and `IWorkItemsOrchestratorFactory.cs` (Abstractions.Agent interfaces), `RevisionFolderProcessorFactory.cs`, `WorkItemsImportRuntime.cs`, `WorkItemsOrchestratorFactory.cs`, `ModuleServiceCollectionExtensions.cs`, plus `IdentitiesOrchestrator.cs`. | Phase 2 prep (2026-06-04) | Resolved | **Resolution (operator decision):** FR-016 changed from delete-and-recreate to a **rename** (`git mv` + symbol rename) to preserve git history. The rename mechanically covers all 16 files in one pass, keeping the build green, instead of a breaking deletion. FR-016 and WP1 updated accordingly. Verified when the WP1 build gate confirms zero `IIdentityLookupTool` references and a green build. |

> Add a row for every discrepancy found while implementing tasks (e.g. an interface that
> differs from the data-model, a file path that does not exist, a behaviour the spec did not
> anticipate). Each row MUST reach `Resolved` or `N/A` before T086 is checked off.

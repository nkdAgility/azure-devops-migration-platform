# ADR-0030: Team orchestrator unification and phase-neutral WorkItem renames

**Status:** Accepted — pure refactor, no behaviour change

**Date:** 2026-07-03

## Context

The archcheck naming sweep (`analysis/archcheck/naming-remediation-proposals.md`
§3) applied the ruling that *matching Import and Export halves must be unified
into ONE phase-symmetric entity* — the migration pipeline's phases are dispatch
context, not identity. Deep per-type analysis found that after the
`IWorkItemsOrchestrator` precedent, only **one** genuine export/import peer pair
remained, plus a small set of component types carrying a stray `Export` phase
word that is not part of their identity.

The operator granted Class C consent (Abstractions-touching contract renames)
for this ADR to record the unification and the phase-neutral renames. These are
**behaviour-preserving**: type/file renames, references, DI registrations, XML
doc `<see cref>`s, `nameof`s, test fixtures, and net481-guarded-type string
literals were updated; no method signatures, logic, artefact formats, or wiring
semantics changed.

## Decision

### 1. Team orchestrator unification (Group A)

`TeamExportOrchestrator` and `TeamImportOrchestrator` (both in
`Infrastructure.Agent/Teams/`) were merged into one phase-symmetric seam,
**`TeamMigrationOrchestrator`**, exposing both `ExportTeamAsync(...)` and
`ImportTeamAsync(...)`. Method names keep the phase word — a method names the
phase it runs, which is legitimate. The two peer implementations were already
dispatched by the module-level `TeamsOrchestrator`; it now holds one
`TeamMigrationOrchestrator` and calls its two methods.

- The unified type takes the union of both dependency sets. The phase-specific
  halves (`ITeamSource`/`ISourceEndpointInfo` for export;
  `ITeamTarget`/`ITargetEndpointInfo`/`INodeTranslationTool`/`IIdentityTranslationTool`
  for import) are nullable so the net481 source-only agent and phase-scoped tests
  can supply only the half they exercise; the required dep for each phase is
  checked at method entry.
- `TeamMigrationOrchestrator` is registered once in DI (all four production seams
  are available on both target frameworks). The import **dispatch** in
  `TeamsOrchestrator.ImportAsync` remains guarded under `#if !NET481` exactly as
  before — TFS is a source-only connector.

### 2. Phase-neutral WorkItem renames (Group B)

The following component types dropped the stray `Export` phase word (they name a
component, not a phase):

| # | Old name | New name |
| --- | --- | --- |
| 1 | `IExportProgressStore` | `IWorkItemProgressStore` |
| 2 | `IExportProgressStoreFactory` | `IWorkItemProgressStoreFactory` |
| 3 | `SqliteExportProgressStore` | `SqliteWorkItemProgressStore` |
| 4 | `ExportProgressStoreFactory` | `WorkItemProgressStoreFactory` |
| 5 | `IWorkItemExportMetrics` | `IWorkItemMetrics` |
| 6 | `WorkItemExportMetrics` | `WorkItemMetrics` |
| 7 | `WorkItemExportProgress` | `WorkItemProgress` |
| 8 | `WorkItemRevisionExportContext` | `WorkItemRevisionContext` |

The store holds per-work-item revision progress for resume (not export-only); the
metrics/progress/context types are not export artefacts.

### Explicitly out of scope (left as-is)

The `IWorkItemExportOrchestrator` family (subordinate revision-streaming seam),
`ExportContext`/`ImportContext` (asymmetric by design),
`EmbeddedImageExportService`/`EmbeddedImageReplayService` (different pipeline
stages), the `Import*Readiness`/`Import*Failure` family, `ImportWorkItemStateStore`,
`IImportCreatedNodeStateStore`, `ImportedWorkItemResult`, `BoardConfigImportMode`,
the OTel `*Exporter` types, and `SimulatedExport*Options` are correct as-is per
the analysis and were not touched.

## Consequences

- The Teams module now presents one phase-symmetric per-team seam; WorkItem
  component names no longer imply an export-only scope they do not have.
- No runtime, wiring, artefact-format, or test-outcome change — the full suite
  passes unchanged (`dotnet build` and `dotnet build -c Release` both 0 errors;
  full `dotnet test -c Release` green).
- Executed as two buildable commits (Teams unification; WorkItem phase-neutral
  renames).

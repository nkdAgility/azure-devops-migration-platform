# ADR-0029: Taxonomy suffix renames

**Status:** Accepted — pure rename, no behaviour change

**Date:** 2026-07-03

## Context

The taxonomy-naming glossary (`.agents/20-guardrails/core/taxonomy-naming.md`,
amended commit `892a7e50`) canonicalises a fixed set of role suffixes —
`Source`, `Registry`, `Store`, `Sink`, `Processor`, `Lifecycle`, `Orchestrator`,
and others. The archcheck naming sweep
(`analysis/archcheck/naming-remediation-proposals.md` §2) identified a small set
of types whose suffix was off-taxonomy, where the type's behaviour maps cleanly
onto a sanctioned role.

The operator granted Class C consent (Abstractions-touching contract renames) for
this ADR to record the following ten renames. These are **pure renames — zero
behaviour change**: type declarations, references, DI registrations, XML doc
`<see cref>`s, `nameof`s, test fixtures, and fully-qualified string literals were
updated; no method signatures, logic, or wiring semantics changed.

## Decision

The following ten types were renamed to the role their behaviour satisfies:

| # | Old name | New name | Role satisfied |
| --- | --- | --- | --- |
| 1 | `IClassificationTreeReader` | `IClassificationTreeSource` | Source (read-side connector seam) |
| 2 | `TfsClassificationTreeReader` | `TfsClassificationTreeSource` | Source impl |
| 3 | `SimulatedClassificationTreeReader` | `SimulatedClassificationTreeSource` | Source impl |
| 4 | `CompositeClassificationTreeReader` (+ record `KeyedClassificationTreeReader` → `KeyedClassificationTreeSource`) | `CompositeClassificationTreeSource` | Source composite/dispatcher |
| 5 | `IWorkItemRevisionMapper` / `TfsWorkItemRevisionMapper` | `IWorkItemRevisionProcessor` / `TfsWorkItemRevisionProcessor` | Processor (pure delta transform) |
| 6 | `IAzureDevOpsWorkItemRevisionMapper` / `AzureDevOpsWorkItemRevisionMapper` | `IAzureDevOpsWorkItemRevisionProcessor` / `AzureDevOpsWorkItemRevisionProcessor` | Processor (pure delta transform) |
| 7 | `TfsAttachmentRegistry` | `TfsAttachmentIdStore` | Store (keyed attachment-id data) |
| 8 | `IReferencedPathTracker` / `ReferencedPathTracker` | `IReferencedPathLifecycle` / `ReferencedPathLifecycle` | Lifecycle (load → record → persist state progression) |
| 9 | `IWorkerEventWriter` | `IWorkerEventSink` | Sink (terminal write seam) |
| 10 | `IJobPlanExecutor` / `JobPlanExecutor` | `IJobPlanOrchestrator` / `JobPlanOrchestrator` | Orchestrator (coordinates plan execution) |

Notes:

- `UnifiedWorkerEventWriter` (the implementation of `IWorkerEventSink`) keeps its
  name — it is correctly a Worker; only the interface renamed.
- `AzureDevOpsClassificationTreeReader` keeps its type name (out of scope for this
  ADR); only its `IClassificationTreeSource` interface reference updated.
- Method names were not renamed (e.g.
  `IClassificationTreeSource.EnumerateAreaNodesAsync`, `.Map`).

## Consequences

- Type names now signal their glossary role, satisfying the taxonomy-naming
  guardrail.
- No runtime, wiring, or test-outcome change — the full suite passes unchanged
  (`dotnet build` and `dotnet build -c Release` both 0 errors; full
  `dotnet test` green).
- Executed as three buildable commits (ClassificationTree Reader→Source;
  WorkItemRevision Mapper→Processor; Tracker→Lifecycle / Registry→Store /
  EventWriter→Sink / PlanExecutor→Orchestrator).

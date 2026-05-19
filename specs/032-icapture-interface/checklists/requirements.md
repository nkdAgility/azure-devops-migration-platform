# Specification Quality Checklist: ICapture Interface — Unified Capture Contract

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-06
**Updated**: 2026-05-17 (reconciliation against repository truth)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
  > *Note: This is an internal architectural refactor — the code-level contracts (interface names, method signatures) ARE the feature. Technology-specificity here is intentional and appropriate.*
- [x] Focused on user value and business needs
  > *The users are platform engineers; the business value is eliminating the `IProjectAnalyser` workaround and unifying capture dispatch.*
- [x] Written for non-technical stakeholders
  > *Audience for this spec is platform engineers — technical language is correct for this internal/architectural feature.*
- [x] All mandatory sections completed
  > *User Scenarios & Testing, Observability, Connector Coverage, Requirements, Success Criteria, and Assumptions are all present and complete.*

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous — FR-001 through FR-016 each have verifiable outcomes
- [x] Success criteria are measurable — SC-001 through SC-005 all have concrete verification steps
- [x] Success criteria are technology-agnostic — SC-003 formula uses variable K with documented baseline
- [x] All acceptance scenarios are defined — US1 (3), US2 (4), US3 (2)
- [x] Edge cases are identified — 5 edge cases covering boundary conditions
- [x] Scope is clearly bounded — "no user-facing CLI or behaviour changes" stated explicitly
- [x] Dependencies and assumptions identified — 7 assumptions documented

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows — dispatch via ICapture, pure capture handlers, IProjectAnalyser removal
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Observability

- [x] `## Observability` section present — injected by observability-contract hook (2026-05-06)
- [x] Operations table populated — `dependency.capture` and `capture.dispatch`
- [x] Operator decisions defined — 6 decisions across both operations
- [x] New metrics defined — 4 new `platform.dependencies.capture.*` constants with new `WellKnownAgentMetricNames` entries
- [x] Existing module metrics (`platform.*.inventory.*`) confirmed unchanged
- [x] Trace hierarchy defined — root span + 3 child spans for `DependencyCapture`
- [x] Structured logging defined — 6 log events with fields and levels
- [x] Correlation model defined — `traceId`, `job.id`, `org.url`, `project.name`, `capture.handler`
- [x] All 5 validation query categories present (Failure ID, Latency, Load, E2E Trace, Error Diagnosis)
- [x] CLI/TUI `ProgressEvent` requirement stated for `DependencyCapture`

## Connector Coverage

- [x] `## Connector Coverage` section present — injected by connector-coverage-check hook (2026-05-06)
- [x] All three connectors assessed — Simulated, AzureDevOps, TFS
- [x] `icapture.rename` and `capture.dispatch`: PASS for all connectors (carry-over, no new connector work)
- [x] `dependency.capture` — AzureDevOps: Required ✅ (`DependencyDiscoveryServiceFactory` exists)
- [x] `dependency.capture` — TFS: Exempt ✅ (documented rationale: REST-only API surface)
- [x] `dependency.capture` — Simulated: Gap identified and resolved — FR-016 adds `SimulatedDependencyDiscoveryServiceFactory`; US2 Scenario 4 adds Simulated acceptance scenario
- [x] Verdict: **PASS** (all gaps closed within this spec)

## Post-Hook Review Summary

| Hook | Status | Key Actions Taken |
|---|---|---|
| `red-team-review` | ✅ Complete | Added FR-012 (IJobPlanExecutor signature), FR-013 (DI registration constraint), corrected FR-006 to name `JobAgentWorker`; SC-002 and SC-003 revised; `SupportsInventory` elevated to FR-014; US2 Scenario 3 reframed; partial capture failure edge case added |
| `observability-contract` | ✅ Complete | `## Observability` section injected; 4 new metrics; trace hierarchy; correlation model; 5 validation queries |
| `connector-coverage-check` | ✅ Complete | `## Connector Coverage` section injected; FR-016 added; US2 Scenario 4 added; TFS exemption documented |

## Notes

- All checklist items pass. Spec is **cleared for `/speckit.plan`**.
- This feature grows from 11 FRs to 16 FRs after the three hooks. All additions are substantive and address real implementation risks.
- The most significant red team finding was the `JobAgentWorker` absence from all FRs — the wiring site for `captureHandlersByName` — which would have caused the refactor to compile but not work end-to-end.
- The most significant connector finding was the missing Simulated `IDependencyDiscoveryServiceFactory` — pre-existing gap surfaced and closed within this spec.

## Reconciliation Delta (Current State)

- [x] Core architecture landed: `ICapture`, `IModule : ICapture`, unified `captureHandlersByName`, and `IProjectAnalyser` removal are present in source.
- [x] Simulated dependency factory exists and is DI-registered.
- [ ] Full clean build (zero warnings) currently fails this checklist expectation (`dotnet build ...` succeeds with warnings in baseline).
- [ ] Fresh full-solution `dotnet test` evidence is not recorded in this reconciliation run.
- [ ] Fresh `.vscode/launch.json` simulated dependency-capture scenario evidence is not recorded in this reconciliation run.
- [ ] Spec/task artifact drift exists and should be retained as explicit supersession notes (path/name changes and `CaptureAsync` return-type drift).


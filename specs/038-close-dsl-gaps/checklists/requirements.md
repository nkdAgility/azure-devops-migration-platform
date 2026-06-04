# Specification Quality Checklist: Close DSL Migration Gaps

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-06-03
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- All 9 open gaps (GAP-001 through GAP-009) are explicitly named in SC-001 and covered
  by user stories. GAP-002 and GAP-003 are grouped in User Story 2 with both named.
- Architecture clarified (2026-06-03): identity domain uses IIdentityTranslationTool (Tool),
  IIdentitiesOrchestrator (Orchestrator), IIdentityAdapter (connector Adapter), and
  IIdentityMatchingStrategy (ordered Strategy list). Matching happens in PrepareAsync only.
  Translate() remains synchronous. IIdentityLookupTool is superseded.
- RT-C1 resolved: async promotion removed — Translate() stays synchronous; PrepareAsync
  owns all live I/O via IIdentityAdapter.
- RT-C2 resolved: Connector-specific scenarios added to US1 (scenarios 8–10); FR-005
  requires all three connector Adapter implementations.
- RT-H1 resolved: Display-name collision scenario added (US1 scenario 3).
- RT-H2 resolved: FR-009 requires full caller audit; US3 scenario 4 verifies it.
- RT-H3 resolved: US6 commits to Option 1 (fail-fast) only.
- RT-H4 resolved: US1 scenario 6 covers adapter query failure path during PrepareAsync.
- RT-H5 resolved: Caching model clarified — PrepareAsync results cached by Orchestrator;
  Translate() reads cache only.
- RT-H6 resolved: US5 scenario 1 specifies structured log fields.
- RT-H7 resolved: US2 scenario 2 covers Enabled=false case.
- RT-H8 resolved: US7 scenario 4 requires per-test MeterProvider scope; FR-013/014.
- RT-H9 resolved: US2 scenario 4 rewrites the non-testable AutoCreateNodes scenario.
- RT-H10 resolved: US1 scenario 7 covers IIdentityTranslationTool.IsEnabled=false path.
- RT-H11 resolved: User Story 2 title explicitly names GAP-002 and GAP-003.
- Naming corrected: INodeTranslationTool (not INodeTransformTool) throughout.
- Metric naming corrected: platform.identities.prepare.* for new prepare-phase metrics.
- FR-016 added: IIdentityLookupTool deleted; all usages replaced with IIdentityTranslationTool;
  field _identityLookupTool renamed to _identityTranslationTool. Explicit and mandatory.
- FR-017 added: _NodeTransformTool field in TeamImportOrchestrator renamed to
  _nodeTranslationTool to match INodeTranslationTool. Explicit and mandatory.
- FR-018 added: Refactor-First mandatory — all #if guards in touched files assessed
  against runtime-compatibility-net10-net481.md before feature edits. Non-compliant
  guards refactored first with evidence for all seven guardrail review questions.
- FR-019 added: TfsIdentityAdapter lives in TFS agent project (project-boundary seam);
  no #if guards; reduced capability modeled explicitly as empty-list + Warning.
- FR-020 added: IIdentitiesOrchestrator #if !NET481 guard on ImportAsync removed;
  net481 explicit implementation provided in TFS agent project.
- Operations table expanded: identity.export and identity.import added for IdentitiesModule
  ExportAsync/ImportAsync lifecycle delegation to IIdentitiesOrchestrator.
- Logging table corrected: all identity log events now attributed to identity.prepare
  (not the non-existent identity.resolve); identity.translate gets its own log event.
- /speckit-analyze (2026-06-04) resolutions:
  - F1/F8: plan.md stale `#if NET481` references removed; TfsIdentityAdapter placed in the
    TFS agent project (project-boundary seam); duplicate adapter tree entries cleaned.
  - F2: `discrepancies.md` created (Spec-Completion Gate); T084a verifies it.
  - F3: stale `cli.queue.config-check` row removed from Connector Coverage.
  - F4: operator-doc tasks added — operator-guide.md (default team), configuration-reference.md
    (config-export resume semantics) (T083a/T083b).
  - F5b: empty/whitespace path treated as untranslatable → null (FR-009, T056).
  - F5a/F6: configured default identity modelled via IdentityTranslationOptions.DefaultIdentity;
    target-existence validation owned by PrepareAsync; null/empty default → source unchanged (T012).
  - F7: plan↔tasks phase-mapping note added.
  - F9: IdentitiesOrchestratorPrepareTests.cs unit-test task added (T019a).

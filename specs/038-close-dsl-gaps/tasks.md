# Tasks: Close DSL Migration Gaps

**Input**: Design documents from `specs/038-close-dsl-gaps/`
**Branch**: `038-close-dsl-gaps`
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

**Constitution**: ATDD-First is NON-NEGOTIABLE (Principle VIII). Gherkin `.feature` file and failing step bindings MUST exist before any production implementation code is written for each acceptance scenario.

## Work-Package Progress

Implementation is grouped into committed, green-build work packages (operator decision 2026-06-04):

- **WP1a ✅ (commit `refactor(038): rename …`)** — Phase 1 baseline (T001/T002) green; FR-016 reworked from delete→**rename** (preserve history). `IIdentityLookupTool`→`IIdentityTranslationTool`, impl/options/extensions renamed, namespace + config section `…:IdentityTranslation`, `_identityLookupTool`→`_identityTranslationTool` across all consumers. Behaviour-neutral. Satisfies T001, T002, and the rename portions of T026–T031.
- **WP1b ✅** — Phase 2 guard refactor (T003, T004, T006, T007; T005 superseded — see D-002). Removed the non-compliant interface-level `#if !NET481` on `IIdentitiesOrchestrator.ImportAsync` and the DI-hiding field/param guards in `IdentitiesModule`. Build green on net10 + net481. `Resolve()`→`Translate()` method rename **deferred to WP2** (reshaped onto the PrepareAsync cache there; avoids rippling through test mocks twice).
- **WP2** — US1 identity matching pipeline (T008–T045) → GAP-001.
- **WP3** — US2/US3/US4/US5 (T046–T071) → GAP-002/003/005/006/004.
- **WP4** — US6/US7 + docs (T072–T086+) → GAP-007/008/009.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no pending dependencies)
- **[Story]**: User story label (US1–US7) — required for all story-phase tasks
- Each task includes the exact file path to create or modify

---

## Phase 1: Setup

**Purpose**: Confirm tooling and verify the build baseline before any edits.

- [X] T001 Confirm `dotnet clean && dotnet build --no-incremental` passes on `main` before any changes — establish green baseline
- [X] T002 Confirm `dotnet test` passes on baseline — record failing test count (expected: 0)

---

## Phase 2: Foundational — Guard Refactoring (FR-018, FR-020)

**Purpose**: Mandatory Refactor-First per `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md` Rule 11. Non-compliant `#if` guards in touched files MUST be remediated before any feature edits in those files. No user story work may begin until this phase is complete.

**⚠️ CRITICAL**: Blocks all US1 work on `IIdentitiesOrchestrator` and `IdentitiesOrchestrator`.

- [X] T003 Audit all `#if` / `#if !NET481` guards in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IIdentitiesOrchestrator.cs`, `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs`, and `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs` — record each guard, classify as compliant (crash-prevention only) or non-compliant (DI hiding / architectural exclusion), answer all seven Required Review Questions from the guardrail
- [X] T004 Remove `#if !NET481` guard from `IIdentitiesOrchestrator.ImportAsync` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IIdentitiesOrchestrator.cs` — `ImportAsync` becomes unconditionally present on the interface
- [~] T005 SUPERSEDED (see discrepancies D-002): no separate net481 orchestrator adapter needed. `IdentitiesOrchestrator` multi-targets net481 and its `ImportAsync` is net481-safe; reduced capability modelled by the compliant `#if NET481` Skipped branch in `IdentitiesModule.ImportAsync`. FR-018/FR-020 satisfied by removing the interface + DI-hiding guards.
- [X] T006 Remediate any non-compliant guards identified in T003 within `IdentitiesOrchestrator.cs` — guards used for DI hiding or optional enablement must be replaced with target-specific implementations or removed; guards for crash-prevention-only API differences may remain
- [X] T007 Verify `dotnet build --no-incremental` passes on both net10 and net481 targets after guard remediation — record evidence for guardrail Required Evidence items 1–8

**Checkpoint**: All non-compliant guards removed. Build green on both runtimes. Evidence recorded. US1 edits to touched files may now begin.

---

## Phase 3: User Story 1 — Identity Matching (Priority: P1) 🎯 MVP

**Goal**: Implement the full four-step identity resolution pipeline: `IIdentityAdapter` (connector-specific target query), `IIdentityMatchingStrategy[]` (ordered UPN → display-name fallback), `IIdentityTranslationTool` (synchronous cross-cutting seam), and `IdentitiesOrchestrator.PrepareAsync` (cache population). Delete `IIdentityLookupTool` and update all consumers. Close GAP-001.

**Independent Test**: `dotnet test --filter "FullyQualifiedName~IdentitiesOrchestrator|IdentityTranslationTool|IdentityMatchingStrategy|IdentityAdapter"`

### Abstractions — New Interfaces (parallelizable)

- [ ] T008 [P] [US1] Create `IIdentityAdapter` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Identity/IIdentityAdapter.cs` — methods: `FindByUpnAsync(string upn, string projectName, CancellationToken ct)` and `FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)`, both returning `Task<IReadOnlyList<IdentityCandidate>>`
- [ ] T009 [P] [US1] Create `IdentityCandidate` immutable record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Identity/IdentityCandidate.cs` — properties: `string Descriptor`, `string? Upn`, `string? DisplayName`
- [ ] T010 [P] [US1] Create `IIdentityMatchingStrategy` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Identity/IIdentityMatchingStrategy.cs` — method: `string? Match(string sourceIdentity, string sourceDisplayName, IReadOnlyList<IdentityCandidate> candidates, ILogger logger)`
- [ ] T011 [P] [US1] Create `IIdentityTranslationTool` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IIdentityTranslationTool.cs` — properties/methods: `bool IsEnabled`, `string Translate(string sourceIdentity)`
- [ ] T012 [P] [US1] Create `IdentityTranslationOptions` sealed options class in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IdentityTranslationOptions.cs` — `public static string SectionName => "MigrationPlatform:Tools:IdentityTranslation"`, `bool IsEnabled { get; init; } = true`, and `string? DefaultIdentity { get; init; }` (carried over from `IdentityLookupOptions.DefaultIdentity`; when null/empty, `Translate()` returns the source unchanged — target-existence validation is owned by `PrepareAsync`, not this default)
- [ ] T013 [US1] Modify `IIdentitiesOrchestrator` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IIdentitiesOrchestrator.cs` — add `Task PrepareAsync(string projectName, ImportContext context, CancellationToken ct)`; confirm `ImportAsync` no longer takes `IIdentityLookupTool?` parameter (guard removed in T004)

### ATDD — Strategy Tests (write failing tests first)

- [ ] T014 [P] [US1] Write failing unit tests for `UpnIdentityMatchingStrategy` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/UpnIdentityMatchingStrategyTests.cs` — cover: exact UPN match (case-insensitive), no-match returns null, multiple candidates with one UPN match
- [ ] T015 [P] [US1] Write failing unit tests for `DisplayNameIdentityMatchingStrategy` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/DisplayNameIdentityMatchingStrategyTests.cs` — cover: single match (Unicode NFC, case-insensitive), ambiguous match (>1 result) returns null and logs warning, no-match returns null

### Strategy Implementations

- [ ] T016 [P] [US1] Implement `UpnIdentityMatchingStrategy` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/Strategies/UpnIdentityMatchingStrategy.cs` — exact case-insensitive UPN match against `IdentityCandidate.Upn`; make T014 tests pass
- [ ] T017 [P] [US1] Implement `DisplayNameIdentityMatchingStrategy` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/Strategies/DisplayNameIdentityMatchingStrategy.cs` — Unicode NFC normalisation, case-insensitive exact match; ambiguous match logs structured warning with displayName and matchCount, returns null; make T015 tests pass

### ATDD — Orchestrator PrepareAsync (write failing feature file and bindings first)

- [ ] T018 [US1] Update Gherkin scenarios in `features/import/identities/identity-mapping-resolution.feature` — add/update scenarios for UPN match (scenario 1), display-name match (scenario 2), ambiguous display-name (scenario 3), no-match fallback (scenario 4), override priority (scenario 5), adapter failure (scenario 6), IsEnabled=false passthrough (scenario 7)
- [ ] T019 [US1] Write failing Reqnroll step bindings for PrepareAsync scenarios in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/IdentityMappingResolutionSteps.cs` and `IdentityMappingResolutionContext.cs` — use `MockBehavior.Strict` mocks for `IIdentityAdapter`, `IIdentityMatchingStrategy[]`
- [ ] T019a [US1] Write failing unit tests for `IdentitiesOrchestrator.PrepareAsync` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/IdentitiesOrchestratorPrepareTests.cs` — cover: UPN match path, display-name match path, adapter-failure-continues path, unresolved recorded in `prepare-report.json`, cache populated for `Translate()` reads (use `MockBehavior.Strict`)

### Orchestrator Implementation

- [ ] T020 [US1] Add constructor parameters to `IdentitiesOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs` — inject `IIdentityAdapter`, `IIdentityMatchingStrategy[]` (ordered), `IIdentityMappingService`; add `ConcurrentDictionary<string, string>` resolution cache field
- [ ] T021 [US1] Implement `IdentitiesOrchestrator.PrepareAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs` — enumerate source identity descriptors from package; for each: step 1 via `IIdentityMappingService.Resolve()`, step 2 via `FindByUpnAsync` + `UpnIdentityMatchingStrategy`, step 3 via `FindByDisplayNameAsync` + `DisplayNameIdentityMatchingStrategy`, step 4 default fallback; populate cache; write `Identities/prepare-report.json`; emit `identity.prepare` ActivitySource span; emit `platform.identities.prepare.*` metrics; log structured Info start/complete and Warning for adapter failures
- [ ] T022 [US1] Update `IdentitiesOrchestrator.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs` — remove `IIdentityLookupTool?` method parameter; load cached prepare results for use in import phase
- [ ] T023 [US1] Make T019 feature-file scenarios pass — run `dotnet test --filter "FullyQualifiedName~IdentityMappingResolution"` and confirm all green

### ATDD — IIdentityTranslationTool (write failing tests first)

- [ ] T024 [US1] Write failing unit tests for `IdentityTranslationTool` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/IdentityTranslationToolTests.cs` — cover: `IsEnabled=false` returns source unchanged, `Translate()` returns cached Orchestrator result, `Translate()` returns default when not in cache

### IIdentityTranslationTool Implementation

- [ ] T025 [US1] Implement `IdentityTranslationTool` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityTranslation/IdentityTranslationTool.cs` — constructor-inject `IIdentitiesOrchestrator`, `IOptions<IdentityTranslationOptions>`; `Translate()` reads orchestrator cache (synchronous); make T024 tests pass

### Delete IIdentityLookupTool and Update All Consumers (FR-016)

- [X] T026 [US1] Delete `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IIdentityLookupTool.cs`
- [X] T027 [US1] Delete `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityLookup/IdentityLookupTool.cs`
- [X] T028 [US1] Delete `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityLookup/IdentityLookupToolServiceCollectionExtensions.cs`
- [X] T029 [US1] Update `TeamImportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — replace `IIdentityLookupTool` field and constructor param with `IIdentityTranslationTool _identityTranslationTool`; update all usages of `_identityLookupTool` to `_identityTranslationTool`
- [X] T030 [P] [US1] Update `RevisionFolderProcessor` (locate file path via grep for `IIdentityLookupTool`) — replace field and constructor param with `IIdentityTranslationTool`
- [X] T031 [P] [US1] Update `WorkItemsModule` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` — replace `IIdentityLookupTool` field and constructor param with `IIdentityTranslationTool`
- [ ] T032 [US1] Update `IdentitiesModule` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs` — remove `IIdentityLookupTool` field and `#if !NET481` guards (now deleted), inject `IIdentityTranslationTool`, wire `PrepareAsync` call in the module's Prepare phase
- [ ] T033 [US1] Update `IdentityServiceCollectionExtensions` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityServiceCollectionExtensions.cs` — remove `AddIdentityLookupToolServices()` call; register `IdentityTranslationTool` as `IIdentityTranslationTool` singleton; register `IOptions<IdentityTranslationOptions>`
- [ ] T034 [US1] Build gate — run `dotnet build --no-incremental`; run `Select-String -Recurse -Pattern "IIdentityLookupTool"` and confirm zero results; run `Select-String -Recurse -Pattern "_identityLookupTool"` and confirm zero results

### IIdentityAdapter Implementations — Three Connectors (FR-005, FR-019)

- [ ] T035 [P] [US1] Write failing unit tests for `SimulatedIdentityAdapter` in `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Identity/SimulatedIdentityAdapterTests.cs` — cover: FindByUpnAsync returns matching candidate from in-memory store, FindByDisplayNameAsync returns candidate, no-match returns empty list
- [ ] T036 [P] [US1] Implement `SimulatedIdentityAdapter` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Identity/SimulatedIdentityAdapter.cs` — in-memory deterministic candidates matching the `SimulatedIdentitySource` data set; implements `IIdentityAdapter`; make T035 tests pass
- [ ] T037 [P] [US1] Write failing unit tests for `AzureDevOpsIdentityAdapter` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Identity/AzureDevOpsIdentityAdapterTests.cs` — cover: UPN query maps REST response to IdentityCandidate, display-name query maps response, HTTP failure returns empty list + logs warning
- [ ] T038 [P] [US1] Implement `AzureDevOpsIdentityAdapter` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/Adapters/AzureDevOpsIdentityAdapter.cs` — query `_apis/graph/users` via `IAzureDevOpsClientFactory`; map response to `IReadOnlyList<IdentityCandidate>`; make T037 tests pass
- [ ] T039 [P] [US1] Write failing unit tests for `TfsIdentityAdapter` in `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/Identity/TfsIdentityAdapterTests.cs` — cover: successful query maps response, degradation test proves empty list + Warning log when TFS Identity Service returns no results (explicit contract result, not a guard)
- [ ] T040 [P] [US1] Implement `TfsIdentityAdapter` in `src/DevOpsMigrationPlatform.TfsMigrationAgent/Identity/TfsIdentityAdapter.cs` (TFS agent project, net481 — no `#if` guards) — query `_apis/identities`; when endpoint returns no results or is unavailable return `Array.Empty<IdentityCandidate>()` and log structured `Warning` with TFS version information; make T039 tests pass including degradation test
- [ ] T041 [US1] Implement `CompositeIdentityAdapter` dispatcher in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/Adapters/CompositeIdentityAdapter.cs` — dispatches to the correct `IIdentityAdapter` implementation based on `ITargetEndpointInfo.ConnectorType`
- [ ] T042 [US1] Add `AddIdentityAdapter<T>(this IServiceCollection, string typeKey)` DI extension method; register `SimulatedIdentityAdapter`, `AzureDevOpsIdentityAdapter` in their connector-specific `ServiceCollectionExtensions` files; register `TfsIdentityAdapter` in `TfsMigrationAgentServiceExtensions`
- [ ] T043 [US1] Write Gherkin connector scenarios (scenarios 8–10 in `features/import/identities/identity-mapping-resolution.feature`) — AzureDevOpsServices, TeamFoundationServer, Simulated connector paths with same scenarios 1–7 logic
- [ ] T044 [US1] Run full US1 test gate — `dotnet test --filter "FullyQualifiedName~Identity"` — all tests green; confirm GAP-001 scenarios pass
- [ ] T045 [US1] Mark GAP-001 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`

**Checkpoint**: US1 complete. Identity resolution pipeline fully functional and tested across all three connectors. `IIdentityLookupTool` deleted.

---

## Phase 4: User Story 2 — NodesModule Configuration Conflict (Priority: P2)

**Goal**: Add skip guard to `NodesModule.ImportAsync`, replace all `INodeEnsurer` references with `INodesOrchestrator`, rename `_NodeTransformTool` field, delete the wrong `AutoCreateNodes` feature scenario. Close GAP-002 and GAP-003.

**Independent Test**: `dotnet test --filter "FullyQualifiedName~NodesModule"`

### ATDD (write failing tests first)

- [ ] T046 [P] [US2] Update `features/import/nodes/import-classification-tree.feature` — add/verify Gherkin scenarios for: skip when `ReplicateSourceTree=false`, skip when `Enabled=false`, call orchestrator when `ReplicateSourceTree=true`
- [ ] T047 [P] [US2] Write failing Reqnroll step bindings in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Nodes/NodesModuleSkipGuardSteps.cs` and `NodesModuleSkipGuardContext.cs`

### Implementation

- [ ] T048 [US2] Add skip guard to `NodesModule.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` — return `Skipped` result when `!options.Enabled` or `!options.ReplicateSourceTree`, without calling `INodesOrchestrator`; make T047 tests pass
- [ ] T049 [US2] Search-and-replace all `INodeEnsurer` occurrences with `INodesOrchestrator` across the entire codebase — run `Select-String -Recurse -Pattern "INodeEnsurer"` before and after; confirm zero remaining occurrences
- [ ] T050 [US2] Rename field `_NodeTransformTool` to `_nodeTranslationTool` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` (FR-017) — update field declaration, constructor assignment, and all usages; run `Select-String -Pattern "_NodeTransformTool"` to confirm zero remaining
- [ ] T051 [US2] Delete the incorrect `AutoCreateNodes` scenario from `features/import/nodes/import-classification-tree.feature`
- [ ] T052 [US2] Mark GAP-002 and GAP-003 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md` with rationale: AutoCreateNodes belongs on NodeTranslationOptions; INodeEnsurer replaced by INodesOrchestrator

**Checkpoint**: US2 complete. NodesModule skip guard works, INodeEnsurer eliminated, field renamed.

---

## Phase 5: User Story 3 — TeamImportOrchestrator Path Translation (Priority: P2)

**Goal**: Change `TranslatePath()` to return `null` for untranslatable paths; update all callers to handle null explicitly; increment unresolvable counters and log structured warnings. Close GAP-005.

**Independent Test**: `dotnet test --filter "FullyQualifiedName~TeamImportOrchestrator"`

### ATDD (write failing tests first)

- [ ] T053 [US3] Update `features/import/teams/import-team-area-paths.feature` — add/verify scenarios 2 and 3: untranslatable included path skipped with warning, untranslatable default path skips SetAreaPathsAsync entirely
- [ ] T054 [US3] Update `features/import/teams/import-team-iterations.feature` — add/verify scenario 2: unresolvable iteration path skipped with warning
- [ ] T055 [US3] Write failing Reqnroll step bindings in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/TeamImportOrchestratorPathTranslationSteps.cs` and `TeamImportOrchestratorPathTranslationContext.cs`

### Implementation

- [ ] T056 [US3] Change `TeamImportOrchestrator.TranslatePath()` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — remove `?? sourcePath` fallback; return `result.TargetPath` (nullable `string?`); also return `null` immediately when the input path is null, empty, or whitespace-only (all treated as untranslatable per FR-009)
- [ ] T057 [US3] Update included area-path caller loop in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — when `TranslatePath` returns `null`: log structured `Warning` including `sourcePath`, increment `platform.teams.import.areas.unresolvable` counter, skip path
- [ ] T058 [US3] Update default area-path handling in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — when `TranslatePath` returns `null` for the default area path: do not call `SetAreaPathsAsync`, log structured `Warning` including `pathType=DefaultArea`
- [ ] T059 [US3] Update iteration-path caller loop in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — when `TranslatePath` returns `null`: log structured `Warning` including `sourcePath`, skip iteration
- [ ] T060 [US3] Full caller audit — run `Select-String -Recurse -Pattern "TranslatePath"` across the codebase; for every call site found outside `TeamImportOrchestrator` itself, update the caller to explicitly handle null return (skip or documented fallback); verify no `NullReferenceException` can result
- [ ] T061 [US3] Make T055 tests pass — `dotnet test --filter "FullyQualifiedName~TeamImportOrchestratorPathTranslation"`
- [ ] T062 [US3] Mark GAP-005 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`

**Checkpoint**: US3 complete. Untranslatable paths are explicitly skipped and warned, not silently passed through.

---

## Phase 6: User Story 4 + User Story 5 — Team Member Skip and Default Team (Priority: P3)

**Goal**: Skip `AddMemberAsync` when identity resolves to default; verify default-team structured warning. Close GAP-006 (US4) and GAP-004 (US5).

**Independent Test**: `dotnet test --filter "FullyQualifiedName~TeamImportOrchestrator"`

### ATDD — US4 Member Skip (write failing tests first)

- [ ] T063 [P] [US4] Update `features/import/teams/import-team-members.feature` — add/verify scenario 2: unresolvable member identity is skipped with warning (not imported under default identity)
- [ ] T064 [P] [US4] Write failing Reqnroll step bindings in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/TeamImportOrchestratorMemberSteps.cs` and `TeamImportOrchestratorMemberContext.cs`

### Implementation — US4

- [ ] T065 [US4] Add default-identity check before `AddMemberAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — when `IIdentityTranslationTool.Translate()` returns the configured default identity: log structured `Warning` including `memberDescriptor`, do not call `AddMemberAsync`; when a non-default resolved identity is returned: call `AddMemberAsync` as before
- [ ] T066 [US4] Make T064 tests pass — `dotnet test --filter "FullyQualifiedName~TeamImportOrchestratorMember"`
- [ ] T067 [US4] Mark GAP-006 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`

### ATDD — US5 Default Team Warning (write failing tests first)

- [ ] T068 [P] [US5] Update `features/import/teams/import-default-team-detection.feature` — verify scenario 1: structured warning logged with team name and exact message `"target API does not support explicit default team assignment"`, import continues
- [ ] T069 [P] [US5] Write failing Reqnroll step bindings in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/TeamImportOrchestratorDefaultTeamSteps.cs` and `TeamImportOrchestratorDefaultTeamContext.cs`

### Implementation — US5

- [ ] T070 [US5] Verify and tighten the `IsDefault=true` warning in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — structured log must include `teamName` and exact string `"target API does not support explicit default team assignment"` as a named field (not interpolated); make T069 tests pass
- [ ] T071 [US5] Mark GAP-004 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`

**Checkpoint**: US4 and US5 complete. Unresolvable members skipped; default team limitation documented and warned.

---

## Phase 7: User Story 6 — Close GAP-007 (Priority: P3)

**Goal**: Delete the architecturally impossible CLI scenario. No production code change. Close GAP-007.

**Independent Test**: `Select-String -Recurse -Pattern "us1-write-idempotency"` returns zero results.

- [ ] T072 [US6] Delete `@us1-write-idempotency` scenario from `features/export/config-in-package/config-applied-on-export.feature`
- [ ] T073 [US6] Mark GAP-007 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md` with rationale: "CLI has no access to the package filesystem by architectural design (Principle VI, Separation of Planes). Pre-submission config-exists check is architecturally impossible. Agent applies resume semantics: overwrites if endpoints unchanged, rejects with InvalidOperationException if endpoints changed."

**Checkpoint**: US6 complete. Scenario gone, rationale recorded.

---

## Phase 8: User Story 7 — OTel Counter Test Infrastructure (Priority: P4)

**Goal**: Wire OTel in-memory exporter per test scope; make export metric counters and histograms assertable in deterministic unit tests. Close GAP-008 and GAP-009.

**Independent Test**: `dotnet test --filter "FullyQualifiedName~ExportMetrics"`

### Setup

- [ ] T074 [US7] Check `Directory.Packages.props` for `OpenTelemetry` in-memory exporter package — if `OpenTelemetry.Testing.InMemory` or equivalent is absent, add and pin it; verify `dotnet build` still passes

### ATDD (write failing tests first)

- [ ] T075 [US7] Write failing OTel counter tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/ExportMetricsTests.cs` — `migration.workitems.attempted` asserted via `AddInMemoryExporter` scoped `MeterProvider`; each test creates fresh `MeterProvider` via `Sdk.CreateMeterProviderBuilder().AddMeter(...).AddInMemoryExporter(exportedItems).Build()`
- [ ] T076 [P] [US7] Write failing OTel counter test for `migration.workitems.retried` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/ExportMetricsTests.cs` — simulates transient failure + retry, asserts counter increments once per retry
- [ ] T077 [P] [US7] Write failing OTel histogram test for `migration.workitem.duration.ms` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/ExportMetricsTests.cs` — asserts histogram record exists after export run
- [ ] T078 [P] [US7] Write failing `MetricSnapshot` histogram tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Export/ExportMetricsTests.cs` — assert `RevisionCountMean`, `FieldCountMean`, `PayloadBytesMean` reflect aggregated values from known-size export batch
- [ ] T079 [US7] Write test-isolation verification test — run all four metric tests sequentially; assert counter values are independent (no bleed-through from prior tests)

### Implementation

- [ ] T080 [US7] Make all T075–T079 export metric tests pass — if any instrumentation is missing from the export orchestrator (missing counter increment, histogram record), add it now; verify tests are green
- [ ] T081 [US7] Mark GAP-008 and GAP-009 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`

**Checkpoint**: US7 complete. OTel counters and histograms assertable in per-test-scoped unit tests.

---

## Phase 9: Documentation Sync

**Purpose**: Update canonical context docs, verify all gaps resolved, satisfy Spec-Completion Gate.

- [ ] T082 Update `.agents/30-context/domains/identity-and-mapping.md` — document `IIdentityAdapter` (connector-specific target-tenant query during PrepareAsync), `IIdentityMatchingStrategy[]` (ordered fallback chain), `IIdentityTranslationTool` (synchronous cross-cutting seam), `PrepareAsync` role and cache lifecycle, deletion of `IIdentityLookupTool`
- [ ] T083 [P] Update `.agents/30-context/domains/connector-model.md` — add `IIdentityAdapter` to the connector abstraction list with note that it lives in each connector's project at the project-boundary seam (no `#if` guards)
- [ ] T083a [P] Update `docs/operator-guide.md` (US5 Scenario 2) — state that default-team assignment is NOT performed automatically; instruct the operator to set the default team via Project Settings → Teams in the target Azure DevOps project
- [ ] T083b [P] Update `docs/configuration-reference.md` (US6 Scenario 2) — document config-applied-on-export resume semantics: the agent overwrites `migration-config.json` if endpoints are unchanged and rejects with `InvalidOperationException` if endpoints changed
- [ ] T084 [P] Verify all 9 gaps in `analysis/dsl-gaps-detected.md` are `Status: RESOLVED` — run `Select-String -Pattern "Status: OPEN"` and confirm zero results
- [ ] T084a Create/verify `specs/038-close-dsl-gaps/discrepancies.md` exists and every entry is marked `Resolved` or `N/A` (Spec-Completion Gate, constitution Governance) — if no discrepancies were found, record an explicit "No discrepancies — N/A" entry
- [ ] T085 [P] Review `analysis/pending-actions.md` — remove any entries that are now implemented by this spec
- [ ] T086 Run full final gate: `dotnet clean && dotnet build --no-incremental && dotnet test` — all must pass with zero failures and zero build warnings from changed files

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all US1 edits to `IIdentitiesOrchestrator`, `IdentitiesOrchestrator`, `IdentitiesModule`**
- **Phase 3 (US1)**: Depends on Phase 2 completion; US1 is the critical path — longest phase
- **Phase 4 (US2)**: Depends on Phase 1 only — can run in parallel with Phase 3 (different files: `NodesModule.cs`, `TeamImportOrchestrator._NodeTransformTool`)
- **Phase 5 (US3)**: Depends on T029 (TeamImportOrchestrator consumer update for `IIdentityTranslationTool`) before T056; otherwise independent
- **Phase 6 (US4, US5)**: Depends on T029 (TeamImportOrchestrator `IIdentityTranslationTool`); US4 and US5 are independent of each other
- **Phase 7 (US6)**: No dependencies — can run any time after Phase 1
- **Phase 8 (US7)**: No dependencies — can run any time after Phase 1
- **Phase 9 (Docs)**: Depends on all prior phases complete

### User Story Dependencies

- **US1 (P1)**: Phase 2 must complete first (guard refactoring). US1 itself has no dependencies on US2–US7.
- **US2 (P2)**: Independent of US1 for most tasks. `_NodeTransformTool` rename (T050) can run immediately.
- **US3 (P2)**: T056–T061 depend on T029 (TeamImportOrchestrator `IIdentityTranslationTool` consumer update). T053–T055 (feature/bindings) can start immediately.
- **US4 (P3)**: T065 depends on T029. T063–T064 can start immediately.
- **US5 (P3)**: Independent of US1 for implementation (warning already exists).
- **US6 (P3)**: Fully independent — pure deletion, no code.
- **US7 (P4)**: Fully independent — test infrastructure only.

### Within Each User Story (ATDD ordering)

1. Feature file / Gherkin scenarios updated → MUST FAIL
2. Reqnroll step bindings written → MUST FAIL
3. Implementation code written → tests pass
4. Gap marked RESOLVED

---

## Parallel Opportunities

### Phase 3 (US1) — Parallelizable groups

```
Group A (all parallelizable after T013):
  T008: IIdentityAdapter.cs
  T009: IdentityCandidate.cs
  T010: IIdentityMatchingStrategy.cs
  T011: IIdentityTranslationTool.cs
  T012: IdentityTranslationOptions.cs

Group B (after T016, T017 pass):
  T014: UpnStrategy tests  →  T016: UpnStrategy impl
  T015: DisplayName tests  →  T017: DisplayName impl

Group C (adapter implementations — after T041, T042):
  T035+T036: SimulatedIdentityAdapter
  T037+T038: AzureDevOpsIdentityAdapter
  T039+T040: TfsIdentityAdapter

Group D (consumer updates — after T026–T028):
  T030: RevisionFolderProcessor
  T031: WorkItemsModule
```

### Cross-story parallelism

```
Phase 2 (guard refactor) + Phase 7 (US6 deletion) + Phase 8 (US7 OTel) — fully independent, can run concurrently
Phase 4 (US2) — runs parallel to Phase 3 (US1) on different files
Phase 6 US5 tasks — independent of US4 tasks
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational guard refactoring (**critical gate**)
3. Complete Phase 3: US1 Identity Matching
4. **STOP and VALIDATE**: `dotnet test --filter "FullyQualifiedName~Identity"` — all green
5. GAP-001 resolved — identity pipeline fully functional

### Incremental Delivery

1. Phase 1 + Phase 2 → green baseline with clean guards
2. Phase 3 (US1) → identity pipeline working → GAP-001 closed
3. Phase 4 (US2) → NodesModule fixed → GAP-002, GAP-003 closed
4. Phase 5 (US3) → path translation correct → GAP-005 closed
5. Phase 6 (US4, US5) → member skip + default team → GAP-006, GAP-004 closed
6. Phase 7 (US6) → scenario deleted → GAP-007 closed
7. Phase 8 (US7) → OTel tests wired → GAP-008, GAP-009 closed
8. Phase 9 (Docs) → all 9 gaps RESOLVED, docs updated, branch ready to merge

---

## Notes

- `[P]` tasks touch different files with no incomplete dependencies — safe to run concurrently
- ATDD order is non-negotiable per constitution Principle VIII: feature file → failing bindings → implementation
- Each gap must be marked `Status: RESOLVED` in `analysis/dsl-gaps-detected.md` as the final task in its user story phase
- `dotnet clean && dotnet build --no-incremental && dotnet test` must pass before declaring any phase complete
- SPDX header block required on every new `.cs` file:
  ```
  // SPDX-License-Identifier: AGPL-3.0-only
  // Copyright (c) Naked Agility Limited
  ```
- `TfsIdentityAdapter` has NO `#if` guards — project boundary (net481 project) is the runtime isolation seam
- `IIdentitiesOrchestrator` has NO `#if` guards after Phase 2 — interface-level guards are non-compliant

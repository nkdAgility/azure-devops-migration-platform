# Tasks: OrganisationEndpoint

**Input**: Design documents from `/specs/016-organisation-endpoint/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, quickstart.md

**Tests**: Not explicitly requested in the feature specification. Test compilation fixes are included as cleanup tasks only.

**Organization**: Tasks are grouped by user story. Note: this is a structural refactor — stories are logically separable but the build only compiles after all type changes land.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No new projects or folders needed — this is a refactor within existing project structure.

*(No tasks — existing structure is sufficient)*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create the three new immutable types that ALL user stories depend on. These are leaf types with no callers yet, so the build passes after this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T001 Create `OrganisationEndpointAuthentication` sealed class with `AuthenticationType Type` and `string? ResolvedAccessToken` init-only properties in `src/DevOpsMigrationPlatform.Abstractions/Models/OrganisationEndpointAuthentication.cs`
- [ ] T002 Create `OrganisationEndpoint` sealed class with `string ResolvedUrl`, `string Type`, `OrganisationEndpointAuthentication Authentication`, and `string? ApiVersion` init-only properties in `src/DevOpsMigrationPlatform.Abstractions/Models/OrganisationEndpoint.cs`
- [ ] T003 Create `ScopedOrganisationEndpoint` sealed class with `OrganisationEndpoint Endpoint` and `List<string> Projects` init-only properties in `src/DevOpsMigrationPlatform.Abstractions/Models/ScopedOrganisationEndpoint.cs`

**Checkpoint**: Three new types compile. Existing code unchanged — `dotnet build` passes.

---

## Phase 3: User Story 1 — All service calls use a single connection context object (Priority: P1) 🎯 MVP

**Goal**: Eliminate all `(string url, string pat)` parameter pairs from Abstractions-level service interfaces. Every service method that connects to an organisation accepts `OrganisationEndpoint` instead.

**Independent Test**: Grep for `string url.*string pat` across all Abstractions-level interfaces returns zero matches. All existing unit tests pass after implementation updates.

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [ ] T004 [US1] Create `features/services/organisation-endpoint/organisation-endpoint-service-interfaces.feature` — translate spec.md User Story 1 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 1

- [ ] T005 [P] [US1] Update `IWorkItemDiscoveryService`, `IWorkItemQueryWindowStrategy`, `IProjectDiscoveryService`, `ICatalogService`, `IWorkItemLinkAnalysisService`, and `IWorkItemCommentSourceFactory` — replace `(string url, string pat)` parameters with `OrganisationEndpoint endpoint` in `src/DevOpsMigrationPlatform.Abstractions/Services/`
- [ ] T006 [P] [US1] Update `IAzureDevOpsClientFactory` — replace URL/PAT parameters with `OrganisationEndpoint endpoint` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/IAzureDevOpsClientFactory.cs`
- [ ] T007 [US1] Update `AzureDevOpsClientFactory` implementation to match new `OrganisationEndpoint` parameter on all methods in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsClientFactory.cs`
- [ ] T008 [US1] Update all concrete service implementations (`IWorkItemDiscoveryService`, `IWorkItemQueryWindowStrategy`, `IProjectDiscoveryService`, `ICatalogService`, `IWorkItemLinkAnalysisService`, `IWorkItemCommentSourceFactory` impls) to match new `OrganisationEndpoint` parameter signatures in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/`

**Checkpoint**: All 6 service interfaces and their implementations accept `OrganisationEndpoint`. Callers (factories, CLI) may not compile until US2 phase completes.

---

## Phase 4: User Story 3 — Config-layer OrganisationEntry converts to OrganisationEndpoint (Priority: P2)

**Goal**: Provide a type-safe conversion from mutable config-layer `OrganisationEntry` to immutable `OrganisationEndpoint`.

**Independent Test**: Unit test creates an `OrganisationEntry`, calls the conversion, and asserts the resulting `OrganisationEndpoint` has matching resolved values.

> **Note**: Placed before US2 because US2 CLI commands depend on `ToOrganisationEndpoint()` for constructing `ScopedOrganisationEndpoint`.

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 3 acceptance scenarios before any step definitions or production code.**

- [ ] T009 [US3] Create `features/services/organisation-endpoint/organisation-entry-conversion.feature` — translate spec.md User Story 3 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 3

- [ ] T010 [US3] Add `ToOrganisationEndpoint()` method to `OrganisationEntry` — resolve `$ENV:VARNAME` tokens in URL and AccessToken via `TokenResolver.Resolve()`, map `EndpointAuthenticationOptions` to `OrganisationEndpointAuthentication`, copy `ApiVersion`, return `OrganisationEndpoint` in `src/DevOpsMigrationPlatform.Abstractions/Options/OrganisationEntry.cs`

**Checkpoint**: `OrganisationEntry` has a clean conversion path to `OrganisationEndpoint`. Method compiles and is callable.

---

## Phase 5: User Story 2 — DiscoveryJob uses OrganisationEndpoint for its organisation list (Priority: P1)

**Goal**: `DiscoveryJob.Organisations` is typed as `List<ScopedOrganisationEndpoint>`. CLI commands and factory implementations use the new types end-to-end.

**Independent Test**: Deserialise an existing discovery job JSON — `Organisations` list populates as `ScopedOrganisationEndpoint` with `Endpoint` and `Projects`.

**Dependencies**: US1 (interface signatures updated), US3 (conversion method for CLI construction)

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 2 acceptance scenarios before any step definitions or production code.**

- [ ] T011 [US2] Create `features/services/organisation-endpoint/discovery-job-organisation-scope.feature` — translate spec.md User Story 2 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 2

- [ ] T012 [US2] Update `DiscoveryJob.Organisations` property from `List<DiscoveryJobOrganisation>` to `List<ScopedOrganisationEndpoint>` in `src/DevOpsMigrationPlatform.Abstractions/Models/DiscoveryJob.cs`
- [ ] T013 [P] [US2] Update `IInventoryServiceFactory` and `IDependencyDiscoveryServiceFactory` interfaces — replace `IReadOnlyList<DiscoveryJobOrganisation>` with `IReadOnlyList<ScopedOrganisationEndpoint>` in `src/DevOpsMigrationPlatform.Abstractions/Services/`
- [ ] T014 [US2] Update `InventoryServiceFactory` and `DependencyDiscoveryServiceFactory` implementations — extract `Endpoint` from each `ScopedOrganisationEndpoint` for service calls, `Projects` for scope filtering in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Factories/`
- [ ] T015 [P] [US2] Update `InventoryCommand.cs` — construct `ScopedOrganisationEndpoint` using `entry.ToOrganisationEndpoint()` instead of `DiscoveryJobOrganisation` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs`
- [ ] T016 [P] [US2] Update `DependencyCommand.cs` — construct `ScopedOrganisationEndpoint` using `entry.ToOrganisationEndpoint()` instead of `DiscoveryJobOrganisation` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs`

**Checkpoint**: Full compilation should pass. `DiscoveryJob` carries new types. CLI constructs `ScopedOrganisationEndpoint` end-to-end.

---

## Phase 6: Cleanup & Test Fixes

**Purpose**: Remove old types and fix any test compilation errors caused by the refactor.

- [ ] T017 Delete `DiscoveryJobOrganisation.cs` and `DiscoveryJobAuthentication.cs` from `src/DevOpsMigrationPlatform.Abstractions/Models/`
- [ ] T018 Update test mocks and fakes to use `OrganisationEndpoint` and `ScopedOrganisationEndpoint` in place of deleted types across `tests/`

**Checkpoint**: Zero references to `DiscoveryJobOrganisation` or `DiscoveryJobAuthentication` remain. All tests compile.

---

## Phase 7: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect the OrganisationEndpoint refactor. Resolve all discrepancies flagged during planning.

- [ ] T019 Update `docs/architecture.md` — add `OrganisationEndpoint` as the canonical connection context type in the Abstractions section (see `discrepancies.md` item 1)
- [ ] T020 [P] Update `.agents/context/job-contract.md` — replace all `DiscoveryJobOrganisation` references with `ScopedOrganisationEndpoint` and `OrganisationEndpoint` (see `discrepancies.md` item 2)
- [ ] T021 [P] Update `docs/modules.md` and `docs/source-types.md` — document `OrganisationEndpoint` parameter convention for service interfaces (see `discrepancies.md` item 3)
- [ ] T022 Mark all items in `specs/016-organisation-endpoint/discrepancies.md` as `Resolved`
- [ ] T023 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] T024 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T025 Run `dotnet test` — ALL tests MUST pass
- [ ] T026 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: N/A — no setup needed
- **Foundational (Phase 2)**: No dependencies — start immediately. BLOCKS all user stories.
- **US1 (Phase 3)**: Depends on Foundational (new types must exist)
- **US3 (Phase 4)**: Depends on Foundational (needs `OrganisationEndpoint` type)
- **US2 (Phase 5)**: Depends on US1 (factory callers use updated service interfaces) AND US3 (CLI uses `ToOrganisationEndpoint()`)
- **Cleanup (Phase 6)**: Depends on US1, US2, US3 (all references to old types must be replaced first)
- **Documentation Sync (Phase 7)**: Depends on Cleanup (final state must be known)

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) — no dependencies on other stories
- **User Story 3 (P2)**: Can start after Foundational (Phase 2) — no dependencies on other stories. Placed before US2 due to technical dependency.
- **User Story 2 (P1)**: Depends on US1 (interface signatures) and US3 (conversion method) — cannot start until both complete

### Within Each User Story

- Gherkin feature file FIRST
- Interface changes before implementation changes
- Abstractions layer before Infrastructure layer
- Infrastructure layer before CLI layer

### Parallel Opportunities

- **Phase 2**: T001 → T002 → T003 are sequential (each depends on the prior type)
- **Phase 3**: T005 and T006 can run in parallel (different projects)
- **Phase 4**: T009 and T010 are sequential (Gherkin then implementation)
- **Phase 5**: T013 can run in parallel with T015/T016 (different files). T015 and T016 can run in parallel.
- **Phase 7**: T019, T020, and T021 can run in parallel (different doc files)
- **Across phases**: US1 and US3 can theoretically run in parallel (independent prereqs for US2)

---

## Parallel Example: User Story 2

```bash
# After T012 (DiscoveryJob type change) and T013 (factory interfaces):

# These three tasks can run in parallel (different files):
Task T014: "Update InventoryServiceFactory and DependencyDiscoveryServiceFactory"
Task T015: "Update InventoryCommand.cs"
Task T016: "Update DependencyCommand.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational (3 new types)
2. Complete Phase 3: User Story 1 (service interface refactor)
3. **STOP and VALIDATE**: All interfaces accept `OrganisationEndpoint`. Build may not pass until US2 callers are updated.

### Incremental Delivery

1. Foundational → 3 new types exist
2. US1 → All service interfaces and implementations updated
3. US3 → Config conversion method available
4. US2 → Job contract + CLI updated → **Full build passes**
5. Cleanup → Old types deleted → Zero legacy references
6. Documentation → Canonical docs aligned
7. Verification → Build, test, scenario run

### Key Risk: Atomic Compilation

This refactor changes type signatures across 3 layers (Abstractions → Infrastructure → CLI). The solution will not compile between Phase 3 and the end of Phase 5. This is expected for a rename refactor. The implementation agent should execute Phases 3–6 as a batch before attempting `dotnet build`.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- All new types use `sealed class` with `init`-only properties (not C# `record`) for net481 compatibility
- JSON property names are stable — no scenario file changes needed (research.md task 2)
- `AuthenticationType` is an existing enum — no new enum needed (research.md task 4)
- Commit after each phase or logical group

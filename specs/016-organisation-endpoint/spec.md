# Feature Specification: OrganisationEndpoint — Canonical Connected Endpoint Type

**Feature Branch**: `016-organisation-endpoint`  
**Created**: 2026-04-17  
**Status**: Draft  
**Prerequisite for**: `015-work-item-scoped-fetch`  
**Input**: User description: "Introduce OrganisationEndpoint as the canonical, lean type that binds an organisation/collection URL, PAT, and source type together as an inseparable unit. Rename DiscoveryJobOrganisation to OrganisationEndpoint codebase-wide. Update all service interfaces in Abstractions that currently accept (string url, string pat) as separate parameters to accept OrganisationEndpoint instead."

## Architecture References

| Document | Status |
|---|---|
| `docs/architecture.md` | Confirmed — no structural changes required |
| `.agents/guardrails/system-architecture.md` | Confirmed — new type must live in `DevOpsMigrationPlatform.Abstractions` |
| `docs/modules.md` | Confirmed — no module-layer changes required |
| `docs/source-types.md` | Confirmed — both ADO and TFS callers adopt the new type |

## Current State — What Is Wrong

The codebase has two related problems that are solved together by this feature:

1. **`DiscoveryJobOrganisation` is named for its job-contract context** (`DiscoveryJob`) but is increasingly used as a general-purpose connection context. Its name leaks the discovery job domain into service interfaces that have nothing to do with discovery jobs.

2. **Service interfaces accept `string url` and `string pat` as separate parameters**, but these two values are always co-dependent — one cannot be used without the other. Callers must pass them both everywhere and there is no enforcement that a PAT belongs to a particular org URL.

| Interface | Current signature (problem) |
|---|---|
| `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` | `(string url, string project, string pat, ...)` |
| `IWorkItemDiscoveryService.CountWorkItemsAsync` | `(string url, string project, string pat, ...)` |
| `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync` | `(string url, string project, string pat, ...)` |
| `IProjectDiscoveryService.DiscoverProjectsAsync` | `(string url, string pat, ...)` |
| `ICatalogService.GetProjectsAsync` | `(string orgUrl, string pat, ...)` |
| `ICatalogService.CountAllWorkItemsAsync` | `(string orgUrl, string project, string pat, ...)` |
| `IWorkItemLinkAnalysisService.AnalyseLinksAsync` | `(string organisationUrl, string project, string pat, ...)` |
| `IWorkItemCommentSourceFactory.Create` | `(string organisationUrl, string project, string pat)` |

## User Scenarios & Testing *(mandatory)*

### User Story 1 — All service calls use a single connection context object (Priority: P1)

As a developer working on the migration platform, I want every service interface that connects to an organisation to accept an `OrganisationEndpoint` instead of separate `string url` and `string pat` parameters, so that URL and PAT cannot be accidentally mismatched and the codebase is consistent.

**Why this priority**: This is the core value proposition — eliminating an entire class of parameter-mismatch bugs and reducing parameter noise across all service interfaces.

**Independent Test**: Can be fully tested by compiling the solution after the refactor; any caller that still passes separate url/pat parameters will fail to build. Additionally, all existing unit/integration tests must pass unchanged (behaviour is identical).

**Acceptance Scenarios**:

1. **Given** the refactored codebase, **When** a grep for `string url.*string pat` is run across all Abstractions-level interfaces, **Then** zero matches are found.
2. **Given** an `OrganisationEndpoint` constructed with a URL and PAT, **When** it is passed to any service method, **Then** the service resolves the connection using `ResolvedUrl` and `ResolvedPat` from the endpoint — never from separate parameters.
3. **Given** the full test suite, **When** all tests are run after the refactor, **Then** all tests pass with no regressions.

---

### User Story 2 — DiscoveryJob uses OrganisationEndpoint for its organisation list (Priority: P1)

As a developer, I want `DiscoveryJob.Organisations` to be typed as `List<OrganisationEndpoint>` so that the connection context type is consistent from job definition through to service invocation.

**Why this priority**: The discovery job is the entry point for inventory and dependency operations. If it still carries the old type, callers must convert at every use site.

**Independent Test**: Can be tested by deserialising an existing discovery job JSON and confirming the `Organisations` list populates correctly as `OrganisationEndpoint` instances.

**Acceptance Scenarios**:

1. **Given** a discovery job JSON with organisation entries, **When** the job is deserialised, **Then** `job.Organisations` is a `List<OrganisationEndpoint>` with all fields populated.
2. **Given** CLI commands that construct `DiscoveryJobOrganisation` today, **When** the refactor is complete, **Then** they construct `OrganisationEndpoint` instead with no change in behaviour.

---

### User Story 3 — Config-layer OrganisationEntry converts to OrganisationEndpoint (Priority: P2)

As a developer, I want `OrganisationEntry` (the mutable config-layer type) to provide a trivial conversion to `OrganisationEndpoint`, so that the transition from user configuration to runtime connection context is explicit and type-safe.

**Why this priority**: The config layer is the other source of org connection data. Without a clean mapping, callers will construct `OrganisationEndpoint` ad-hoc.

**Independent Test**: Can be tested with a unit test that creates an `OrganisationEntry`, calls the conversion, and asserts the resulting `OrganisationEndpoint` has matching values.

**Acceptance Scenarios**:

1. **Given** an `OrganisationEntry` with URL, PAT, and Type populated, **When** the conversion method is called, **Then** an `OrganisationEndpoint` is returned with `ResolvedUrl`, `ResolvedPat`, and `Type` matching the source values.

---

### Edge Cases

- What happens when `ResolvedPat` is null or empty? → `OrganisationEndpoint` is a data carrier; validation of credentials is the responsibility of the consuming service at call time, not the record constructor.
- What happens with existing serialised `DiscoveryJob` JSON that uses the old `DiscoveryJobOrganisation` shape? → JSON property names must remain stable or a deserialisation compatibility path must be provided so existing scenario files continue to work.
- What about `DiscoveryJobAuthentication`? → Its nested structure (`ResolvedAccessToken`) is flattened into `OrganisationEndpoint.ResolvedPat`. The `DiscoveryJobAuthentication` type is removed.
- What about `Projects` on `DiscoveryJobOrganisation`? → `OrganisationEndpoint` does NOT carry project lists. `DiscoveryJob` must track the org-to-projects mapping separately if needed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new `OrganisationEndpoint` immutable record MUST be introduced in `DevOpsMigrationPlatform.Abstractions`. It MUST expose `string ResolvedUrl`, `string ResolvedPat`, and `string Type`. It MUST NOT carry project lists or other inventory concerns.
- **FR-002**: `DiscoveryJobOrganisation` MUST be renamed to `OrganisationEndpoint` throughout the codebase. The `DiscoveryJobAuthentication` nested type MUST be removed; auth is accessed via `ResolvedPat` directly on the record.
- **FR-003**: `DiscoveryJob.Organisations` MUST be updated to `List<OrganisationEndpoint>`.
- **FR-004**: `IInventoryServiceFactory` and `IDependencyDiscoveryServiceFactory` MUST be updated to accept `IReadOnlyList<OrganisationEndpoint>` in place of `IReadOnlyList<DiscoveryJobOrganisation>`.
- **FR-005**: The following Abstractions-level service interfaces MUST be updated to accept `OrganisationEndpoint` in place of separate `string url`/`string pat` parameters:
  - `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` and `CountWorkItemsAsync`
  - `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync`
  - `IProjectDiscoveryService.DiscoverProjectsAsync`
  - `ICatalogService.GetProjectsAsync` and `CountAllWorkItemsAsync`
  - `IWorkItemLinkAnalysisService.AnalyseLinksAsync`
  - `IWorkItemCommentSourceFactory.Create`
- **FR-006**: All implementations of the interfaces listed in FR-005 MUST be updated to match the new signatures. No behaviour changes — this is a pure signature refactor.
- **FR-007**: `OrganisationEntry` (config layer) MUST gain a convenience method or extension to produce an `OrganisationEndpoint` trivially.
- **FR-008**: Existing serialised `DiscoveryJob` JSON (scenario files) MUST continue to deserialise correctly after the rename. JSON property compatibility MUST be maintained or scenario files updated.

### Key Entities

- **`OrganisationEndpoint`**: Renamed from `DiscoveryJobOrganisation`. Immutable record in `DevOpsMigrationPlatform.Abstractions` with `ResolvedUrl`, `ResolvedPat`, and `Type`. The canonical connection context used across all service interfaces. Does NOT carry project lists.
- **`DiscoveryJob`**: Existing job contract. Its `Organisations` property changes from `List<DiscoveryJobOrganisation>` to `List<OrganisationEndpoint>`.
- **`OrganisationEntry`**: Existing mutable config-layer type. Gains a conversion method to `OrganisationEndpoint`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No Abstractions-level service interface has `string url` and `string pat` as separate parameters after completion.
- **SC-002**: Zero references to `DiscoveryJobOrganisation` remain in the codebase after completion.
- **SC-003**: Zero references to `DiscoveryJobAuthentication` remain in the codebase after completion.
- **SC-004**: The full test suite passes after the refactor with no regressions.
- **SC-005**: All existing scenario JSON files deserialise correctly with the updated types.

## Assumptions

- This is a pure structural refactor — no behavioural changes to any service.
- `OrganisationEndpoint` does not validate credentials; it is a data carrier.
- The `Projects` list currently on `DiscoveryJobOrganisation` is either moved to `DiscoveryJob` directly or handled by callers that need org-to-project mapping. This is a design decision to be resolved in planning.
- Feature 015 (`IWorkItemFetchService`) depends on this feature and will consume `OrganisationEndpoint` in its interface signatures.

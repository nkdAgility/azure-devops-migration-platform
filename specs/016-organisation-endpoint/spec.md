# Feature Specification: OrganisationEndpoint — Canonical Connected Endpoint Type

**Feature Branch**: `016-organisation-endpoint`  
**Created**: 2026-04-17  
**Status**: Complete  
**Prerequisite for**: `015-work-item-scoped-fetch`  
**Input**: User description: "Introduce OrganisationEndpoint as the canonical, lean type that binds an organisation/collection URL, PAT, and source type together as an inseparable unit. Rename DiscoveryJobOrganisation to OrganisationEndpoint codebase-wide. Update all service interfaces in Abstractions that currently accept (string url, string pat) as separate parameters to accept OrganisationEndpoint instead."

## Architecture References

| Document | Status |
|---|---|
| `docs/architecture.md` | Confirmed — no structural changes required |
| `.agents/guardrails/architecture-boundaries.md` | Confirmed — new type must live in `DevOpsMigrationPlatform.Abstractions` |
| `docs/module-development-guide.md` | Confirmed — no module-layer changes required |
| `docs/capabilities-guide.md` | Confirmed — both ADO and TFS callers adopt the new type |

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

As a developer working on the migration platform, I want every service interface that connects to an organisation to accept a `MigrationEndpointOptions` instead of separate `string url` and `string pat` parameters, so that URL and PAT cannot be accidentally mismatched and the codebase is consistent.

**Why this priority**: This is the core value proposition — eliminating an entire class of parameter-mismatch bugs and reducing parameter noise across all service interfaces.

**Independent Test**: Can be fully tested by compiling the solution after the refactor; any caller that still passes separate url/pat parameters will fail to build. Additionally, all existing unit/integration tests must pass unchanged (behaviour is identical).

**Acceptance Scenarios**:

1. **Given** the refactored codebase, **When** a grep for `string url.*string pat` is run across all Abstractions-level interfaces, **Then** zero matches are found.
2. **Given** a `MigrationEndpointOptions` instance constructed with connector-specific fields, **When** it is passed to any service method, **Then** the service resolves the connection using the endpoint — never from separate parameters.
3. **Given** the full test suite, **When** all tests are run after the refactor, **Then** all tests pass with no regressions.

---

### User Story 2 — DiscoveryJob uses ScopedOrganisationEndpoint for its organisation list (Priority: P1)

As a developer, I want `DiscoveryJob.Organisations` to be typed as `List<ScopedOrganisationEndpoint>` so that the connection context type is consistent from job definition through to service invocation.

**Why this priority**: The discovery job is the entry point for inventory and dependency operations. If it still carries the old type, callers must convert at every use site.

**Independent Test**: Can be tested by deserialising an existing discovery job JSON and confirming the `Organisations` list populates correctly with `Endpoint` (as `MigrationEndpointOptions`) and `Projects`.

**Acceptance Scenarios**:

1. **Given** a discovery job JSON with organisation entries, **When** the job is deserialised, **Then** `job.Organisations` is a `List<ScopedOrganisationEndpoint>` with `Endpoint` and `Projects` populated.
2. **Given** CLI commands that construct `DiscoveryJobOrganisation` today, **When** the refactor is complete, **Then** they construct `ScopedOrganisationEndpoint` (wrapping a `MigrationEndpointOptions` via `entry.ToEndpointOptions()`) instead with no change in behaviour.

---

### User Story 3 — Config-layer OrganisationEntry converts to MigrationEndpointOptions (Priority: P2)

As a developer, I want `OrganisationEntry` (the mutable config-layer abstract base class) to provide a `ToEndpointOptions()` method that produces a `MigrationEndpointOptions`, so that the transition from user configuration to runtime connection context is explicit and type-safe.

**Why this priority**: The config layer is the other source of org connection data. Without a clean mapping, callers will construct endpoint options ad-hoc.

**Independent Test**: Can be tested with a unit test that creates a concrete `OrganisationEntry` subclass, calls `ToEndpointOptions()`, and asserts the resulting `MigrationEndpointOptions` has matching values.

**Acceptance Scenarios**:

1. **Given** an `OrganisationEntry` subclass with URL, authentication, and Type populated, **When** `ToEndpointOptions()` is called, **Then** a `MigrationEndpointOptions` is returned with connector-specific fields matching the resolved source values.

---

### Edge Cases

- What happens when `ResolvedAccessToken` is null? → This is valid for `Windows` auth. `OrganisationEndpoint` is a data carrier; validation of credentials is the responsibility of the consuming service at call time, not the record constructor.
- What happens with existing serialised `DiscoveryJob` JSON that uses the old `DiscoveryJobOrganisation` shape? → JSON property names must remain stable or a deserialisation compatibility path must be provided so existing scenario files continue to work.
- What about `DiscoveryJobAuthentication`? → Its nested structure is replaced by `OrganisationEndpointAuthentication`, which flattens to `AuthenticationType Type` + `string? ResolvedAccessToken`. The `DiscoveryJobAuthentication` type is removed.
- What about `Projects` on `DiscoveryJobOrganisation`? → `MigrationEndpointOptions` does NOT carry project lists. `DiscoveryJob` tracks the org-to-projects mapping via `ScopedOrganisationEndpoint`. `ApiVersion` moves into `OrganisationEndpoint` since it is a connection property.
- What about polymorphism? → During implementation, the design evolved to use `MigrationEndpointOptions` (an abstract base class) at the Abstractions-level interface boundary instead of `OrganisationEndpoint` directly. This enables polymorphic dispatch for ADO, TFS, and Simulated connectors. `OrganisationEndpoint` remains the ADO/TFS-specific resolved type used by `IAzureDevOpsClientFactory` in `Infrastructure.AzureDevOps`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new `OrganisationEndpoint` immutable sealed class MUST be introduced in `DevOpsMigrationPlatform.Abstractions`. It MUST expose `string ResolvedUrl`, `string Type`, `OrganisationEndpointAuthentication Authentication`, and `string? ApiVersion`. It MUST NOT carry project lists or other inventory concerns.
- **FR-002**: A new `OrganisationEndpointAuthentication` immutable sealed class MUST be introduced in `DevOpsMigrationPlatform.Abstractions`. It MUST expose `AuthenticationType Type` and `string? ResolvedAccessToken`. It carries only resolved values — no raw `$ENV:VARNAME` tokens. For `Windows` auth, `ResolvedAccessToken` is null.
- **FR-003**: `DiscoveryJobOrganisation` MUST be renamed to `OrganisationEndpoint` throughout the codebase. The `DiscoveryJobAuthentication` nested type MUST be removed; auth is accessed via the `OrganisationEndpointAuthentication` record on the endpoint.
- **FR-004**: `DiscoveryJob.Organisations` MUST be updated to `List<ScopedOrganisationEndpoint>`.
- **FR-005**: `IInventoryServiceFactory` and `IDependencyDiscoveryServiceFactory` MUST be updated to accept `IReadOnlyList<ScopedOrganisationEndpoint>` in place of `IReadOnlyList<DiscoveryJobOrganisation>`. The factories extract `OrganisationEndpoint` and `Projects` from each scope entry.
- **FR-006**: The following Abstractions-level service interfaces MUST be updated to accept `MigrationEndpointOptions` (the abstract polymorphic base class) in place of separate `string url`/`string pat` parameters:
  - `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` and `CountWorkItemsAsync`
  - `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync`
  - `IProjectDiscoveryService.DiscoverProjectsAsync`
  - `ICatalogService.GetProjectsAsync` and `CountAllWorkItemsAsync`
  - `IWorkItemLinkAnalysisService.AnalyseLinksAsync`
  - `IWorkItemCommentSourceFactory.Create`
- **FR-007**: All implementations of the interfaces listed in FR-006 MUST be updated to match the new `MigrationEndpointOptions` signatures. ADO/TFS implementations resolve `MigrationEndpointOptions` to `OrganisationEndpoint` internally via `IAzureDevOpsClientFactory`. No behaviour changes — this is a pure signature refactor.
- **FR-008**: `OrganisationEntry` (config layer, now an abstract base class) MUST provide an abstract `ToEndpointOptions()` method returning `MigrationEndpointOptions`. Each concrete connector subclass (e.g. `AzureDevOpsOrganisationEntry`) implements this to resolve `$ENV:VARNAME` tokens and produce a connector-specific endpoint options instance.
- **FR-009**: Existing serialised `DiscoveryJob` JSON (scenario files) MUST continue to deserialise correctly after the rename. JSON property compatibility MUST be maintained or scenario files updated.
- **FR-010**: A `ScopedOrganisationEndpoint` sealed class MUST be introduced to pair a `MigrationEndpointOptions` with `List<string> Projects` on `DiscoveryJob`. This wrapper lives on the job contract only — service interfaces accept `MigrationEndpointOptions`, not the scope wrapper. Factory implementations extract `Endpoint` for service calls and `Projects` for scope filtering.

### Key Entities

- **`MigrationEndpointOptions`**: Abstract base class in `DevOpsMigrationPlatform.Abstractions`. The polymorphic endpoint type accepted by all Abstractions-level service interfaces. Exposes `Type`, virtual methods `GetEndpointUrl()`, `GetProject()`, `GetResolvedUrl()`, and `ValidateEndpointFields()`. Concrete subclasses carry connector-specific fields.
- **`OrganisationEndpoint`**: Immutable sealed class in `DevOpsMigrationPlatform.Abstractions` with `ResolvedUrl`, `Type`, `Authentication` (`OrganisationEndpointAuthentication`), and `ApiVersion`. The ADO/TFS-specific resolved connection context used internally by `IAzureDevOpsClientFactory` in `Infrastructure.AzureDevOps`. Does NOT carry project lists.
- **`OrganisationEndpointAuthentication`**: Immutable sealed class in `DevOpsMigrationPlatform.Abstractions` with `AuthenticationType Type` and `string? ResolvedAccessToken`. The resolved runtime counterpart to `EndpointAuthenticationOptions`. For Windows auth, `ResolvedAccessToken` is null.
- **`DiscoveryJob`**: Existing job contract. Its `Organisations` property changes from `List<DiscoveryJobOrganisation>` to `List<ScopedOrganisationEndpoint>`.
- **`ScopedOrganisationEndpoint`**: Sealed class on `DiscoveryJob` that pairs `MigrationEndpointOptions Endpoint` with `List<string> Projects`. Job-contract scope only — does not appear in service interface signatures.
- **`OrganisationEntry`**: Abstract base class for config-layer organisation entries. Provides abstract `ToEndpointOptions()` returning `MigrationEndpointOptions`. Concrete subclasses (e.g. `AzureDevOpsOrganisationEntry`) carry connector-specific config fields.

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
- The `Projects` list currently on `DiscoveryJobOrganisation` moves to `ScopedOrganisationEndpoint` on `DiscoveryJob` — a wrapper that pairs `MigrationEndpointOptions` with a project filter. `ApiVersion` moves into `OrganisationEndpoint` itself since it is a connection property, not a job-scope concern.
- `OrganisationEndpoint` and `OrganisationEndpointAuthentication` are sealed classes with `init`-only properties (not C# `record` syntax) to maintain `net481` compatibility in the multi-targeted Abstractions project.
- `IAzureDevOpsClientFactory` (in `Infrastructure.AzureDevOps`) accepts `OrganisationEndpoint` (the resolved ADO/TFS type), not `MigrationEndpointOptions`. Infrastructure implementations resolve `MigrationEndpointOptions` to `OrganisationEndpoint` internally.
- Feature 015 (`IWorkItemFetchService`) depends on this feature and will consume `MigrationEndpointOptions` in its interface signatures.

## Implementation Note — Architectural Evolution

During implementation, the design evolved beyond the original spec in one key way: instead of `OrganisationEndpoint` being the universal Abstractions-level interface parameter, `MigrationEndpointOptions` (an abstract polymorphic base class) fills that role. This was driven by feature 017 (Simulated Infrastructure), which introduced the need for polymorphic endpoint dispatch across ADO, TFS, and Simulated connectors.

**Type hierarchy as implemented:**
- `MigrationEndpointOptions` (abstract) — accepted by all Abstractions-level service interfaces
  - `AzureDevOpsEndpointOptions` (concrete) — ADO-specific fields
  - `SimulatedEndpointOptions` (concrete) — simulated source fields
- `OrganisationEndpoint` (sealed) — resolved ADO/TFS connection context, used by `IAzureDevOpsClientFactory`

This is a better design (OCP-compliant, supports new connector types without modifying interfaces) and all original success criteria are still met.

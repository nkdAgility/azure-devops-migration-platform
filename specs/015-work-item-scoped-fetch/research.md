# Research: Work Item Scoped Fetch Service

**Feature**: 015-work-item-scoped-fetch  
**Date**: 2026-04-18

## R-001: Where should IWorkItemFetchService sit in the dependency chain?

**Decision**: `IWorkItemFetchService` lives in `DevOpsMigrationPlatform.Abstractions/Services/`. The ADO implementation lives in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/`. The TFS implementation lives in `DevOpsMigrationPlatform.Infrastructure.TFS/Services/` (or the multi-targeted Infrastructure project if TFS-side is needed for net481).

**Rationale**: System architecture guardrail rule 21 requires new abstractions to be defined in `DevOpsMigrationPlatform.Abstractions`. The interface is used by at least two independent callers (discovery + dependency analysis), satisfying the "at least two modules" requirement. Implementations follow the existing pattern where `Infrastructure.AzureDevOps` contains ADO-specific service classes.

**Alternatives considered**:
- Placing the interface in `Infrastructure` — rejected: violates guardrail rule 13 (module code must not reference concrete implementations) and constitution principle V.
- Placing the ADO implementation in `Abstractions` — rejected: abstractions must not contain concrete implementations.

## R-002: Should FetchAsync accept MigrationEndpointOptions or OrganisationEndpoint?

**Decision**: `FetchAsync` accepts `OrganisationEndpoint` directly, as specified in the spec (FR-002).

**Rationale**: The existing codebase pattern is that services accept `MigrationEndpointOptions` and internally call `ToOrganisationEndpoint()`. However, spec 015 explicitly requires `OrganisationEndpoint` as the connection context. This is consistent with the feature 016 intent — `OrganisationEndpoint` is the canonical connection type with resolved credentials, while `MigrationEndpointOptions` is a configuration-layer type with unresolved `$ENV:` tokens.

**Alternatives considered**:
- Using `MigrationEndpointOptions` to match existing pattern — rejected: spec FR-002 is explicit; `OrganisationEndpoint` is the resolved form and avoids leaking config-layer types into service contracts.
- Accepting both via overloads — rejected: unnecessary complexity; callers should resolve endpoints before calling.

## R-003: How does IWorkItemFetchService relate to IWorkItemQueryWindowStrategy?

**Decision**: `AzureDevOpsWorkItemFetchService` uses `IWorkItemQueryWindowStrategy` internally to obtain work item IDs, then batch-fetches fields for those IDs. The fetch service is a higher-level abstraction that composes windowing + batching + filtering into a single streaming interface.

**Rationale**: FR-005 mandates this. The window strategy handles the 20K WIQL result limit by splitting queries into date windows. The fetch service adds field projection and in-process filtering on top. This layering keeps each abstraction focused (SRP).

**Alternatives considered**:
- Having FetchAsync bypass window strategy and use direct WIQL — rejected: would duplicate the proven windowing algorithm and break the mandatory reuse principle (guardrail rule 21).
- Merging window strategy into fetch service — rejected: violates SRP; window strategy is independently useful for operations that don't need full field fetches (e.g., counting).

## R-004: What is the batch size for field fetches?

**Decision**: Use the same batch size of 200 that existing services use (`RevisionBatchSize` constant in `AzureDevOpsWorkItemDiscoveryService`).

**Rationale**: The Azure DevOps REST API `GetWorkItemsAsync` supports up to 200 IDs per call. Both `AzureDevOpsWorkItemDiscoveryService` and `AzureDevOpsDependencyAnalysisService` use this limit. Consistency avoids API throttling surprises.

**Alternatives considered**:
- Configurable batch size — rejected at this stage: the 200-limit is an API constraint, not a tuning parameter. Can be revisited if the API limit changes.

## R-005: How should WorkItemFieldFilterOptions be handled given feature 014 doesn't exist yet?

**Decision**: Create a placeholder `WorkItemFieldFilterOptions` record in `DevOpsMigrationPlatform.Abstractions/Models/` with the minimal shape needed for `WorkItemFetchScope`. It will be a simple immutable record with `FieldName`, `Operator`, and `Value` properties. When feature 014 lands, the placeholder is replaced with the full implementation.

**Rationale**: The spec explicitly states this approach in Assumptions: "if unavailable, a placeholder stub will be used and replaced when 014 lands." The placeholder must be functional enough to write filter evaluation logic in the fetch service.

**Alternatives considered**:
- Making filter options nullable and deferring all filter logic — rejected: the spec requires filter evaluation (FR-004) as a core capability.
- Waiting for feature 014 to land first — rejected: spec 015 states it can proceed with a placeholder.

## R-006: How should in-process filter evaluation work?

**Decision**: `AzureDevOpsWorkItemFetchService.FetchAsync` evaluates `WorkItemFieldFilterOptions` predicates after each batch fetch, before yielding items. Each filter option specifies a field name, comparison operator, and expected value. Items that fail any filter condition are not yielded.

**Rationale**: FR-004 requires in-process evaluation after batch fetch. This is necessary because WIQL WHERE clauses cannot express all filter combinations the callers need (e.g., type-specific filters that vary per module). The in-process approach also works uniformly for TFS sources where WIQL capabilities differ.

**Alternatives considered**:
- Server-side WIQL filtering only — rejected: WIQL cannot express all filter predicates, and some callers need post-fetch filtering for fields not available in WIQL.
- Injecting a filter strategy — rejected: over-engineering for a simple predicate evaluation that is purely sequential.

## R-007: What happens to existing callers' constructor dependencies?

**Decision**: `AzureDevOpsWorkItemDiscoveryService` will receive `IWorkItemFetchService` via constructor injection in addition to (or replacing) its current `IWorkItemQueryWindowStrategy` + `IAzureDevOpsClientFactory` dependencies. The same applies to `AzureDevOpsDependencyAnalysisService`. The DI registration extension method will wire up `IWorkItemFetchService` → `AzureDevOpsWorkItemFetchService`.

**Rationale**: Constitution principle IX requires constructor injection only. The fetch service encapsulates the windowing + batching + filtering logic, so callers no longer need direct access to `IWorkItemQueryWindowStrategy` or `IAzureDevOpsClientFactory` for field fetching. However, `AzureDevOpsDependencyAnalysisService` still needs `IAzureDevOpsClientFactory` for its Relations expansion calls (which remain outside `IWorkItemFetchService` per FR-013).

**Alternatives considered**:
- Keep all existing dependencies and add `IWorkItemFetchService` — viable for dependency analysis (needs client for Relations), but discovery should drop direct client access for field fetching.

## R-008: TFS implementation approach

**Decision**: `TfsWorkItemFetchService` will be a functional implementation in the multi-targeted `DevOpsMigrationPlatform.Infrastructure` project (net481 + net10.0) that uses the TFS Object Model's `WorkItemStore` to query and return field-projected work items. It will NOT throw `NotImplementedException` (FR-008, constitution reject condition).

**Rationale**: The spec requires a functional TFS stub. The TFS Object Model has `WorkItemStore.Query()` which returns `WorkItemCollection` that can be iterated. Field projection is achieved by specifying fields in the WIQL SELECT clause. The implementation must compile for net481 (used by the TFS subprocess) and net10.0 (for type compatibility).

**Alternatives considered**:
- Placing TFS implementation in `CLI.TfsMigration` — rejected: that project is the subprocess entry point, not a service library.
- Making it a stub that throws — explicitly rejected by FR-008 and the constitution.

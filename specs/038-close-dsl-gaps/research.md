# Research: Close DSL Migration Gaps

**Feature**: `specs/038-close-dsl-gaps/spec.md`
**Date**: 2026-06-03

---

## Decision 1: IIdentityAdapter vs IIdentitySource

**Decision**: Introduce a new `IIdentityAdapter` abstraction for target-tenant querying during PrepareAsync, completely separate from the existing `IIdentitySource`.

**Rationale**: `IIdentitySource` is an export-phase abstraction — it enumerates source identity descriptors from the source system. `IIdentityAdapter` is an import-phase abstraction — it queries the target tenant to find matching identities by UPN or display name. These are opposite-direction queries on opposite systems and must not share an abstraction.

**Alternatives considered**: Reusing `IIdentitySource` with a query variant — rejected because it would violate Principle I (Package-First) by having an export abstraction participate in import-phase logic.

---

## Decision 2: PrepareAsync as a new method on IIdentitiesOrchestrator

**Decision**: Add `PrepareAsync` to the existing `IIdentitiesOrchestrator` interface (alongside `ExportAsync`, `ImportAsync`, `ValidateAsync`). The orchestrator holds an internal resolution cache populated by `PrepareAsync` and read by `ImportAsync` and `IIdentityTranslationTool.Translate()`.

**Rationale**: `IIdentitiesOrchestrator` already owns the other phase methods. Adding `PrepareAsync` is consistent with the existing pattern and avoids a new interface proliferation. The cache is an implementation detail of the concrete `IdentitiesOrchestrator`.

**Alternatives considered**: A separate `IIdentityPrepareOrchestrator` — rejected as over-engineering; the Orchestrator already owns all identity lifecycle.

---

## Decision 3: IIdentityLookupTool parameter removal from ImportAsync

**Decision**: Remove the `IIdentityLookupTool?` method parameter from `IIdentitiesOrchestrator.ImportAsync`. The orchestrator receives `IIdentityTranslationTool` via constructor injection and uses it internally.

**Rationale**: Passing the tool as a method parameter is not SOLID — it is service-locator flavoured. Constructor injection is mandated by Principle IX. Since `IdentitiesOrchestrator` is a singleton and `IIdentityTranslationTool` is also a singleton, constructor injection is the correct lifecycle match.

**Alternatives considered**: Keeping the method parameter — rejected because it violates Principle IX and is the root cause of the current InitializeAsync-in-ImportAsync antipattern.

---

## Decision 4: Ordered strategy list over single hierarchical strategy

**Decision**: `IIdentityMatchingStrategy[]` — an ordered array of independently injectable strategies. The Orchestrator walks the list in order, stopping at the first match. Two implementations: `UpnIdentityMatchingStrategy`, `DisplayNameIdentityMatchingStrategy`.

**Rationale**: Each strategy is independently testable, independently injectable, and independently replaceable. The Orchestrator controls ordering via DI registration order. A single monolithic strategy would be harder to test and extend.

**Alternatives considered**: Single strategy with internal hierarchy — rejected; reduces testability and makes ordering implicit.

---

## Decision 5: IIdentityAdapter connector DI pattern

**Decision**: Follow the existing `AddIdentitySource<T>(string typeKey)` keyed pattern. A `CompositeIdentityAdapter` dispatches to the correct connector implementation based on `ITargetEndpointInfo.ConnectorType`. Each connector registers via `AddIdentityAdapter<T>("ConnectorTypeKey")`.

**Rationale**: Mirrors the proven `IIdentitySource` / `CompositeIdentitySource` pattern already used for export. Consistent patterns reduce cognitive overhead.

**Alternatives considered**: Keyed DI (`IKeyedServiceProvider`) — available in .NET 8+ but the existing pattern predates it and the team has not adopted it yet; stay consistent.

---

## Decision 6: TranslatePath null return — breaking change scope

**Decision**: Change `TranslatePath()` to return `null` when `result.TargetPath` is null. Audit all callers in the codebase, not just `TeamImportOrchestrator`. The audit scope covers every class that calls `TranslatePath()` directly or transitively.

**Rationale**: Silent pass-through makes the skip branch unreachable and produces incorrect imports (source paths substituted for untranslatable paths). Null is the correct sentinel for "no mapping found". The caller audit is mandatory per FR-009.

**Alternatives considered**: Using a result wrapper type — deferred; the null return is sufficient and avoids a new type.

---

## Decision 7: Constitution Principle V tension

**Finding**: Principle V states "All modules MUST use `IIdentityMappingService`." The current codebase uses `IIdentityLookupTool` for cross-module identity resolution. The spec introduces `IIdentityTranslationTool` as the new canonical seam.

**Resolution**: `IIdentityMappingService` handles override loading and step-1/step-4 resolution only — it is an internal concern of `IdentityTranslationTool`, not a module-facing seam. The constitution wording is aspirational/stale; the enforced seam is the Tool interface. No constitution violation — `IIdentityTranslationTool` delegates to `IIdentitiesOrchestrator` which internally uses `IIdentityMappingService` for override lookups.

---

## Decision 8: OTel in-memory exporter — NuGet dependency

**Finding**: `OpenTelemetry.Testing.InMemory` or equivalent in-memory exporter availability needs confirming against `Directory.Packages.props` before implementation.

**Resolution**: Use `OpenTelemetry.Sdk` with `AddInMemoryExporter` available in `OpenTelemetry` base package. If not pinned, add to `Directory.Packages.props` and pin. The `MeterProvider` must be scoped per-test via `using var provider = Sdk.CreateMeterProviderBuilder()...Build()`.

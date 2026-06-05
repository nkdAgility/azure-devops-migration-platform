# Implementation Plan: Close DSL Migration Gaps

**Branch**: `038-close-dsl-gaps` | **Date**: 2026-06-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/038-close-dsl-gaps/spec.md`

---

## Summary

Close all 9 open entries in `analysis/dsl-gaps-detected.md` (GAP-001 through GAP-009) by implementing the missing identity resolution pipeline (`IIdentityAdapter`, `IIdentityMatchingStrategy`, `IIdentityTranslationTool`, and `PrepareAsync` on `IIdentitiesOrchestrator`), fixing two `NodesModule` configuration conflicts, correcting `TeamImportOrchestrator`'s silent path pass-through and member-skip omission, deleting an architecturally impossible CLI scenario (GAP-007), and wiring OTel in-memory exporter test infrastructure for export metric assertions.

---

## Technical Context

**Language/Version**: C# 12, targeting .NET 10 for all new code. .NET 4.8 carve-out for `TfsIdentityAdapter` in the TFS agent path only.

**Primary Dependencies**:
- `Microsoft.Extensions.DependencyInjection` — constructor injection throughout
- `Microsoft.Extensions.Options` — `IOptions<IdentityTranslationOptions>`
- `OpenTelemetry` — existing `ActivitySource`, `IMigrationMetrics`; in-memory exporter added for tests
- `Moq` (MockBehavior.Strict) — test isolation

**Storage**: `IArtefactStore` — `prepare-report.json` written under `Identities/` in the package.

**Testing**: Reqnroll.MSTest + Moq in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests`. OTel in-memory exporter scoped per test via `Sdk.CreateMeterProviderBuilder()`.

**Target Platform**: .NET 10 server (agent host). `TfsIdentityAdapter` runs on .NET 4.8 by living in the TFS agent project (`DevOpsMigrationPlatform.TfsMigrationAgent`) — the project boundary is the runtime isolation seam. **No `#if NET481` guards are used** (FR-019, FR-020).

**Project Type**: Library — internal platform components (no new CLI commands, no new deployable hosts).

**Performance Goals**: `PrepareAsync` must complete within the operator's job timeout. No per-identity latency target — network-bound by adapter queries.

**Constraints**: `Translate()` stays synchronous. No live I/O at translate time. In-memory cache is the only data path from prepare to translate.

**Scale/Scope**: Handles the identity descriptors enumerated from the source package — expected hundreds to low thousands per migration job.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Package-First | ✅ Pass | `IIdentityAdapter` queries the **target** during PrepareAsync (import phase). No source API called during import. No target API called during export. |
| II. Streaming Import & Memory Safety | ✅ Pass | PrepareAsync processes source identity descriptors one at a time. Resolution cache is bounded by descriptor count. |
| III. Canonical WorkItems Layout | ✅ N/A | Identity changes do not touch WorkItems folder structure. |
| IV. Cursor-Based Checkpointing | ✅ Pass | `IdentitiesOrchestrator` already writes cursor state. PrepareAsync writes `prepare-report.json` — not a cursor replacement. |
| V. Module Isolation via Abstractions | ✅ Pass | Tension noted: constitution references `IIdentityMappingService` but modules use the Tool seam. `IIdentityTranslationTool` delegates to `IIdentitiesOrchestrator` which internally calls `IIdentityMappingService` for override lookups. No module implements its own resolution. |
| VI. Separation of Planes | ✅ Pass | No changes to CLI, TUI, or control plane. `IIdentityAdapter` lives in the agent boundary only. |
| VII. Determinism & Idempotency | ✅ Pass | PrepareAsync is idempotent — re-running overwrites `prepare-report.json` with the same result for the same inputs. |
| VIII. ATDD-First | ✅ Pass | Each user story produces one or more Gherkin scenarios before implementation. |
| IX. SOLID & DI | ✅ Pass | `IIdentityLookupTool` method-parameter antipattern removed. All new interfaces constructor-injected. |
| X. Engineering Practice | ✅ Pass | OTel spans + metrics on PrepareAsync. Structured logging throughout. `CancellationToken` forwarded. |
| XI. Full Connector Coverage | ✅ Pass | `IIdentityAdapter` has three implementations: `AzureDevOpsIdentityAdapter`, `TfsIdentityAdapter`, `SimulatedIdentityAdapter`. |

**No violations. Proceed to implementation.**

---

## Project Structure

### Documentation (this feature)

```text
specs/038-close-dsl-gaps/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
└── tasks.md             ← Phase 2 output (not yet created)
```

### Source Code

```text
src/DevOpsMigrationPlatform.Abstractions.Agent/
├── Identity/
│   ├── IIdentityAdapter.cs            NEW — connector Adapter for target-tenant UPN/display-name query
│   ├── IIdentityMatchingStrategy.cs   NEW — Strategy interface for ordered fallback matching
│   └── IIdentityMappingService.cs     UNCHANGED
├── Modules/
│   └── IIdentitiesOrchestrator.cs     MODIFIED — PrepareAsync added; ImportAsync IIdentityLookupTool? param removed
└── Tools/
    ├── IIdentityTranslationTool.cs    NEW — Tool seam replacing IIdentityLookupTool
    ├── IdentityTranslationOptions.cs  NEW — options under MigrationPlatform:Tools:IdentityTranslation
    └── IIdentityLookupTool.cs         DELETE (FR-016)

src/DevOpsMigrationPlatform.Infrastructure.Agent/
├── Identity/
│   ├── Adapters/
│   │   ├── AzureDevOpsIdentityAdapter.cs    NEW (net10, no guards)
│   │   └── CompositeIdentityAdapter.cs       NEW — connector dispatcher
│   └── Strategies/
│       ├── UpnIdentityMatchingStrategy.cs         NEW
│       └── DisplayNameIdentityMatchingStrategy.cs  NEW
├── Modules/
│   ├── IdentitiesOrchestrator.cs   MODIFIED — PrepareAsync implemented; ImportAsync param removed; cache added
│   ├── IdentitiesModule.cs         MODIFIED — PrepareAsync wired; IIdentityLookupTool refs removed
│   └── NodesModule.cs              MODIFIED — skip guard added (FR-007)
├── Teams/
│   └── TeamImportOrchestrator.cs   MODIFIED — TranslatePath null return; member skip; _nodeTranslationTool rename
├── Tools/
│   ├── IdentityLookup/
│   │   ├── IdentityLookupTool.cs                              DELETE (FR-016)
│   │   └── IdentityLookupToolServiceCollectionExtensions.cs   DELETE (FR-016)
│   └── IdentityTranslation/
│       └── IdentityTranslationTool.cs    NEW — implementation of IIdentityTranslationTool
└── Identity/
    └── IdentityServiceCollectionExtensions.cs   MODIFIED — registers new types, removes IIdentityLookupTool

src/DevOpsMigrationPlatform.Infrastructure.Simulated/
└── Identity/
    └── SimulatedIdentityAdapter.cs   NEW (net10, no guards)

src/DevOpsMigrationPlatform.TfsMigrationAgent/   (net481 — project boundary IS the isolation seam)
└── Identity/
    └── TfsIdentityAdapter.cs         NEW — no #if guards; reduced capability modeled as
                                       empty-list + structured Warning contract result (FR-019)

tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
├── Identity/
│   ├── IdentitiesOrchestratorPrepareTests.cs  NEW
│   ├── UpnIdentityMatchingStrategyTests.cs     NEW
│   ├── DisplayNameMatchingStrategyTests.cs     NEW
│   ├── AzureDevOpsIdentityAdapterTests.cs      NEW
│   ├── TfsIdentityAdapterTests.cs              NEW
│   └── SimulatedIdentityAdapterTests.cs        NEW
├── Teams/
│   └── TeamImportOrchestratorTests.cs          MODIFIED
└── Nodes/
    └── NodesModuleTests.cs                     MODIFIED

features/import/identities/
└── identity-mapping-resolution.feature        MODIFIED — unblock GAP-001 scenarios

features/import/nodes/
└── import-classification-tree.feature         MODIFIED — remove wrong AutoCreateNodes scenario

features/export/config-in-package/
└── config-applied-on-export.feature           MODIFIED — delete @us1-write-idempotency scenario

analysis/
└── dsl-gaps-detected.md                       MODIFIED — all 9 gaps marked RESOLVED
```

---

## Implementation Phases

### Phase 0 — Refactor-First: remove non-compliant guards (FR-018, FR-020)

**Goal**: Before any feature edits, remediate all non-compliant `#if`/`#if !NET481` guards in files this spec will touch, per `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md` Rule 11 (Refactor-First).

**Scope of non-compliant guards found**:

1. `IIdentitiesOrchestrator.cs` — `#if !NET481` guard around `ImportAsync` at the interface level. Non-compliant: orchestration interfaces must be runtime-agnostic. **Fix**: remove the `#if !NET481` guard; `ImportAsync` becomes unconditionally present on the interface. The net481 (TFS agent) target that previously excluded it must provide an explicit implementation that returns `Task.CompletedTask` with a structured `Warning` ProgressEvent (`"ImportAsync is not supported on the TFS agent runtime"`). This models reduced capability explicitly in the contract result.
2. `IdentitiesModule.cs` — `#if !NET481` guards around `IIdentityLookupTool` field and constructor parameter. Non-compliant: guards used for DI hiding. **Fix**: these guards are removed entirely as part of FR-016 (deletion of `IIdentityLookupTool`). No new guard replaces them — `IIdentityTranslationTool` is in Abstractions (multi-targeted) and available on both runtimes.
3. `IdentitiesOrchestrator.cs` — assess any `#if` guards present. Remediate to target-specific implementations or remove before feature edits.

Tasks:
1. Remove `#if !NET481` guard from `IIdentitiesOrchestrator.ImportAsync` — make the method unconditional on the interface
2. Provide explicit net481 implementation in the TFS agent for `ImportAsync`: returns `Task.CompletedTask` + emits `Warning` ProgressEvent
3. Assess and document all `#if` guards in `IdentitiesOrchestrator.cs` — remediate any that are non-compliant before Phase 1 edits
4. Answer all seven Required Review Questions from the guardrail and record evidence

**Gate**: No non-compliant guards remain in touched files. `dotnet build` passes on both net10 and net481 targets. Evidence recorded for all guardrail questions.

---

### Phase 1 — New abstractions (no behaviour change)

**Goal**: Lay down interfaces and options classes in `Abstractions.Agent`. No implementations, no DI wiring. Builds clean. Existing tests unchanged.

Tasks:
1. Create `IIdentityAdapter` with `FindByUpnAsync` and `FindByDisplayNameAsync`
2. Create `IdentityCandidate` record
3. Create `IIdentityMatchingStrategy` with `Match()`
4. Create `IIdentityTranslationTool` with `IsEnabled` and `Translate()`
5. Create `IdentityTranslationOptions` (sealed, init-only, `SectionName`)
6. Modify `IIdentitiesOrchestrator`: add `PrepareAsync`; remove `IIdentityLookupTool?` from `ImportAsync` (guard already removed in Phase 0)

**Gate**: `dotnet build` passes. No test failures (existing tests may need `ImportAsync` call sites updated).

---

### Phase 2 — Strategy implementations

**Goal**: `UpnIdentityMatchingStrategy` and `DisplayNameIdentityMatchingStrategy` — pure business logic, no I/O, fully unit-testable.

Tasks:
1. Implement `UpnIdentityMatchingStrategy.Match()` — exact UPN match, case-insensitive
2. Implement `DisplayNameIdentityMatchingStrategy.Match()` — Unicode NFC, case-insensitive; log warning on ambiguous match (>1 result), return null
3. Unit tests for both strategies covering all match, no-match, and ambiguous scenarios

---

### Phase 3 — IdentitiesOrchestrator.PrepareAsync

**Goal**: Real PrepareAsync implementation in `IdentitiesOrchestrator`: inject `IIdentityAdapter` + `IIdentityMatchingStrategy[]` via constructor; enumerate source identities from package; apply strategy chain; cache results; write `prepare-report.json`; emit OTel span + metrics.

Tasks:
1. Add constructor parameters: `IIdentityAdapter`, `IIdentityMatchingStrategy[]`, `IIdentityMappingService`
2. Implement `PrepareAsync`:
   - Read source identity descriptors from package
   - For each descriptor: apply `IIdentityMappingService.Resolve()` first (override check)
   - If no override: call `IIdentityAdapter.FindByUpnAsync` → apply `UpnIdentityMatchingStrategy`
   - If no UPN match: call `IIdentityAdapter.FindByDisplayNameAsync` → apply `DisplayNameIdentityMatchingStrategy`
   - If no display-name match: record as unresolved (default fallback)
   - Cache result: `ConcurrentDictionary<string, string>`
   - Write `prepare-report.json`
   - Emit `identity.prepare` span + `platform.identities.prepare.*` metrics
3. Update `ImportAsync` signature: remove `IIdentityLookupTool?` parameter; use cache
4. Integration tests for `PrepareAsync` using `SimulatedIdentityAdapter`

---

### Phase 4 — IIdentityTranslationTool implementation

**Goal**: `IdentityTranslationTool` wraps the Orchestrator cache lookup. Synchronous. No I/O.

Tasks:
1. Implement `IdentityTranslationTool`: constructor-inject `IIdentitiesOrchestrator`, `IOptions<IdentityTranslationOptions>`
2. `Translate(sourceIdentity)`: if `!IsEnabled` return sourceIdentity; else delegate to `IIdentitiesOrchestrator` cache lookup
3. Register as `IIdentityTranslationTool` singleton in `IdentityServiceCollectionExtensions`

---

### Phase 5 — Delete IIdentityLookupTool + caller update (FR-016)

**Goal**: Remove `IIdentityLookupTool` and all its infrastructure. Update every consumer to use `IIdentityTranslationTool`.

Tasks:
1. Delete `IIdentityLookupTool.cs`
2. Delete `IdentityLookupTool.cs` and `IdentityLookupToolServiceCollectionExtensions.cs`
3. Update `TeamImportOrchestrator`: `_identityLookupTool` field → `_identityTranslationTool` (type `IIdentityTranslationTool`)
4. Update `RevisionFolderProcessor`: same rename and type change
5. Update `WorkItemsModule`: same
6. Update `IdentitiesModule`: same; wire PrepareAsync call
7. Update `IdentityServiceCollectionExtensions`: remove `AddIdentityLookupToolServices()` call; add `IdentityTranslationTool` registration

**Gate**: `dotnet build` passes. `Select-String -Pattern "IIdentityLookupTool"` returns zero results.

---

### Phase 6 — IIdentityAdapter implementations (FR-005, FR-019)

**Goal**: Three connector implementations of `IIdentityAdapter`. Each lives at the correct project boundary — no `#if` guards. DI registration updated for each connector.

**Runtime isolation seams**:
- `AzureDevOpsIdentityAdapter` — lives in the AzureDevOps infrastructure project (net10). No guards needed.
- `TfsIdentityAdapter` — lives in the TFS agent project (`DevOpsMigrationPlatform.TfsMigrationAgent` or equivalent, already net481). No `#if` guards — the project boundary IS the isolation seam. Reduced capability (UPN/display-name search unavailable on older TFS) is modeled explicitly: method returns `IReadOnlyList<IdentityCandidate>.Empty` and emits a structured `Warning` log stating the TFS version limitation. This is the contract result, not a guard.
- `SimulatedIdentityAdapter` — lives in the Simulated infrastructure project (net10). No guards.

Tasks:
1. `AzureDevOpsIdentityAdapter`: query `_apis/graph/users` via existing `IAzureDevOpsClientFactory`; map response to `IdentityCandidate`
2. `TfsIdentityAdapter` (in TFS agent project): call TFS `_apis/identities`; when the endpoint returns no results or is unavailable, return `Array.Empty<IdentityCandidate>()` and log a structured warning including TFS version; no `#if` guards
3. `SimulatedIdentityAdapter`: in-memory candidates matching the existing `SimulatedIdentitySource` data set
4. Register each via `AddIdentityAdapter<T>(string typeKey)` pattern (mirrors `AddIdentitySource<T>`)
5. Wire `CompositeIdentityAdapter` dispatcher
6. Adapter unit tests for each connector; degradation test for `TfsIdentityAdapter` proving empty-list + warning is the explicit contract result

---

### Phase 7 — NodesModule fixes (GAP-002, GAP-003)

**Goal**: Skip guard, INodeEnsurer elimination, _NodeTransformTool rename.

Tasks:
1. Add skip guard to `NodesModule.ImportAsync`: return `Skipped` when `!Enabled` or `!ReplicateSourceTree`
2. Search-and-replace `INodeEnsurer` → `INodesOrchestrator` across all files; verify zero occurrences remain
3. Rename `_NodeTransformTool` → `_nodeTranslationTool` in `TeamImportOrchestrator` (FR-017)
4. Delete the `AutoCreateNodes` scenario from `features/import/nodes/import-classification-tree.feature`
5. Mark GAP-002 and GAP-003 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`
6. Unit tests: skip-guard paths

---

### Phase 8 — TeamImportOrchestrator: path translation + member skip (GAP-005, GAP-006)

**Goal**: TranslatePath returns null; callers handle null; member skip on default identity.

Tasks:
1. Change `TeamImportOrchestrator.TranslatePath()`: remove `?? sourcePath` — return `result.TargetPath` (nullable)
2. Update area-path caller loop: when `TranslatePath` returns null, log structured warning and skip; increment `platform.teams.import.areas.unresolvable` counter
3. Update iteration-path caller loop: same treatment
4. Update default area path: when null, do not call `SetAreaPathsAsync`; log structured warning
5. Full caller audit: grep all `TranslatePath` calls across the codebase; update each to handle null
6. Add `AddMemberAsync` skip: when `Translate()` returns the configured default identity, log structured warning (including `memberDescriptor`) and skip `AddMemberAsync` (FR-010)
7. Verify `IsDefault=true` warning is correctly structured: team name + `"target API does not support explicit default team assignment"` (FR-011)
8. Mark GAP-005, GAP-006, GAP-004 `Status: RESOLVED`
9. Unit tests for all null-path and skip-member paths

---

### Phase 9 — GAP-007 scenario deletion

**Goal**: Delete the architecturally impossible CLI scenario. No production code change.

Tasks:
1. Delete `@us1-write-idempotency` scenario from `features/export/config-in-package/config-applied-on-export.feature`
2. Mark GAP-007 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md` with rationale: "CLI has no access to package filesystem by architectural design. Agent resume semantics handle the existing-file case."

---

### Phase 10 — OTel in-memory exporter (GAP-008, GAP-009)

**Goal**: Wire OTel in-memory exporter for export metric assertions. Per-test-scoped `MeterProvider`.

Tasks:
1. Verify `OpenTelemetry` in-memory exporter is available in `Directory.Packages.props`; add and pin if missing
2. Create `ExportMetricsTests`: scoped `MeterProvider` using `Sdk.CreateMeterProviderBuilder().AddMeter(...).AddInMemoryExporter(exportedItems).Build()`
3. Assert `migration.workitems.attempted` counter after export run
4. Assert `migration.workitems.retried` counter after simulated transient failure + retry
5. Assert `migration.workitem.duration.ms` histogram record exists
6. Assert `RevisionCountMean`, `FieldCountMean`, `PayloadBytesMean` on `MetricSnapshot`
7. Verify counter isolation: each test gets fresh `MeterProvider`, zero bleed-through
8. Mark GAP-008 and GAP-009 `Status: RESOLVED`

---

### Phase 11 — Documentation Sync

**Goal**: Update canonical docs to reflect the new identity architecture. Mark all gaps resolved.

Tasks:
1. Update `.agents/30-context/domains/identity-and-mapping.md`: document `IIdentityAdapter`, `IIdentityMatchingStrategy`, `IIdentityTranslationTool`, `PrepareAsync` role
2. Update `.agents/30-context/domains/connector-model.md`: add `IIdentityAdapter` to connector abstraction list
3. Update `docs/operator-guide.md`: state that default-team assignment is not performed automatically (US5 S2) — operator must set the default team via Project Settings → Teams in the target project
4. Update `docs/configuration-reference.md`: document config-applied-on-export resume semantics (US6 S2) — agent overwrites `migration-config.json` if endpoints unchanged, rejects with `InvalidOperationException` if endpoints changed
5. Mark GAP-001 `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`
6. Verify all 9 gaps are `Status: RESOLVED` — no open gaps remain
7. Create/verify `specs/038-close-dsl-gaps/discrepancies.md` — every entry `Resolved` or `N/A` (Spec-Completion Gate)
8. Remove resolved items from `analysis/pending-actions.md` if referenced there
9. Update `specs/038-close-dsl-gaps/checklists/requirements.md` final pass

> **Plan ↔ tasks phase mapping**: plan Phase 0 (Refactor-First guards) = tasks Phase 2 (Foundational);
> plan Phases 1–6 (identity abstractions → strategies → PrepareAsync → Tool → delete lookup → adapters)
> = tasks Phase 3 (US1); plan Phase 7 = tasks Phase 4 (US2); plan Phase 8 = tasks Phases 5–6 (US3–US5);
> plan Phase 9 = tasks Phase 7 (US6); plan Phase 10 = tasks Phase 8 (US7); plan Phase 11 = tasks Phase 9 (Docs).

---

## Complexity Tracking

| Item | Why Non-Trivial | Mitigation |
|---|---|---|
| `IIdentitiesOrchestrator.ImportAsync` signature change | Breaking change on existing interface; all callers must be updated | Phase 1 does this first so build failures surface immediately |
| `TranslatePath()` null return | Breaking API change; unknown number of callers | Mandatory full-codebase grep audit in Phase 8 before any call site is left untouched |
| `IIdentityLookupTool` deletion | Removes an existing interface used in multiple places | Phase 5 is a single atomic step with build gate; zero occurrences verified before declaring done |
| Non-compliant `#if !NET481` guard on `IIdentitiesOrchestrator.ImportAsync` | Interface-level guard violates guardrail Rule 1 and Rule 3; removing it requires a net481 explicit implementation | Phase 0 removes the guard first; TFS agent project gets explicit `ImportAsync` returning `Task.CompletedTask` + Warning |
| `TfsIdentityAdapter` project placement | Must live in TFS agent project (net481 boundary), not a shared project with guards | Phase 6 places it in the correct project; no `#if` guards used anywhere |

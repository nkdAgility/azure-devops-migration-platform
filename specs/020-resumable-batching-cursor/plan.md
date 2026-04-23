# Implementation Plan: Resumable Work Item Batching

**Branch**: `020-resumable-batching-cursor` | **Date**: 2026-04-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/020-resumable-batching-cursor/spec.md`

## Summary

Make the work-item batching strategy resumable so that interrupted export, dependency, and discovery operations can continue from a saved continuation token rather than reprocessing the full project. Resume safety is enforced via query fingerprint comparison (SHA-256 of WIQL text + sorted parameters). Callers own duplicate handling and checkpoint persistence cadence. Ordering is ChangedDate ASC, WorkItemId ASC (oldest-first) for drift-tolerant continuation.

The implementation extends three existing abstractions (`ICheckpointingService`, `IWorkItemQueryWindowStrategy`, `IWorkItemFetchService`) and introduces three new model types (`BatchContinuationToken`, `ResumeDecision`, `IQueryFingerprintService`) — all in `DevOpsMigrationPlatform.Abstractions`. No new projects are created.

## Technical Context

**Language/Version**: C# 10+, targeting net481;net10.0 (multi-target for Abstractions/Infrastructure)  
**Primary Dependencies**: System.Diagnostics.DiagnosticSource (telemetry), System.Security.Cryptography (SHA-256 fingerprinting), System.Text.Json (token serialization)  
**Storage**: IStateStore (via ICheckpointingService) for continuation token persistence under `.migration/Checkpoints/`  
**Testing**: MSTest + Reqnroll.MSTest + Moq (MockBehavior.Strict)  
**Target Platform**: Windows/Linux (net10.0 for hosts, net481 for TFS subprocess)  
**Project Type**: Library (abstractions + infrastructure extensions)  
**Performance Goals**: O(1) resume decision; no regression in existing enumeration throughput  
**Constraints**: Memory-safe — no full result set loading for resume; continuation token < 1 KB serialized  
**Scale/Scope**: Proven at 20,000+ work items; resume reduces re-work to at most one batch window on interruption

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All 9 guardrail files, all 9 context files, and relevant docs files
> have been read in this session. Confirmed.

- [x] **Package-First (I):** This feature operates within the export/fetch path. All continuation tokens are persisted via `IStateStore` through `ICheckpointingService`. No direct source-to-target migration.
- [x] **Streaming (II):** The window strategy yields `IAsyncEnumerable<WorkItemQueryWindow>` — no change. Resume skips already-processed windows without loading them. No in-memory sorting.
- [x] **WorkItems Layout (III):** This feature does not touch the WorkItems folder structure. It operates at the query-window/fetch level, upstream of folder writes.
- [x] **Checkpointing (IV):** Continuation tokens are stored under `.migration/Checkpoints/` via `ICheckpointingService` new methods (`ReadContinuationTokenAsync`, `WriteContinuationTokenAsync`). No watermark tables.
- [x] **Module Isolation (V):** All persistence through `IStateStore` (via `ICheckpointingService`). New interfaces defined in `DevOpsMigrationPlatform.Abstractions`. No concrete store references.
- [x] **Separation of Planes (VI):** Changes are in Infrastructure and Abstractions layers only. No control plane, TUI, or CLI logic affected.
- [x] **Determinism (VII):** Same inputs + same resume token = same query windows. Query fingerprint ensures semantic consistency. No breaking schema change (additive only).
- [x] **ATDD-First (VIII):** Spec has 3 user stories with 6 acceptance scenarios. Feature file will be created under `features/platform/checkpointing/`. One scenario per ATDD session.
- [x] **SOLID & DI (IX):** `IQueryFingerprintService` defined in Abstractions; `QueryFingerprintService` in Infrastructure registered via existing `AddMigrationPlatformOptions` extension. Constructor injection throughout. `BatchContinuationToken` and `ResumeDecision` are sealed records with init-only properties.

## Project Structure

### Documentation (this feature)

```text
specs/020-resumable-batching-cursor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── discrepancies.md     # Architecture discrepancies (3 items, resolved)
├── checklists/          # Requirement checklists
└── tasks.md             # Phase 2 output (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   ├── BatchContinuationToken.cs       # NEW — sealed record (v1: no fallback fields)
│   │   ├── ResumeDecision.cs               # NEW — sealed record
│   │   ├── ResumeDecisionStatus.cs         # NEW — enum
│   │   ├── ResumeRejectedException.cs      # NEW — extends InvalidOperationException
│   │   └── WorkItemFetchScope.cs           # MODIFIED — add resume parameters
│   ├── Services/
│   │   ├── ICheckpointingService.cs        # MODIFIED — add continuation token methods
│   │   ├── IQueryFingerprintService.cs     # NEW — interface
│   │   ├── IWorkItemFetchService.cs        # MODIFIED — add EvaluateResumeDecisionAsync (FR-014)
│   │   └── WorkItemQueryWindow.cs          # MODIFIED — add resume options
│   └── PackagePaths.cs                     # MODIFIED — add ContinuationFile(moduleName) path
├── DevOpsMigrationPlatform.Infrastructure/
│   ├── Checkpointing/
│   │   └── CheckpointingService.cs         # MODIFIED — implement continuation token methods
│   ├── Services/
│   │   └── QueryFingerprintService.cs      # NEW — SHA-256 implementation
│   └── Config/
│       └── MigrationPlatformServiceExtensions.cs  # MODIFIED — register new service
└── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
    └── Services/
        ├── WorkItemQueryWindowStrategy.cs   # MODIFIED — resume-aware windowing
        └── AzureDevOpsWorkItemFetchService.cs  # MODIFIED — emit checkpoints

features/
└── platform/
    └── checkpointing/
        └── resumable-batching-cursor.feature  # NEW — Gherkin acceptance scenarios

tests/
└── DevOpsMigrationPlatform.Infrastructure.Tests/
    ├── Checkpointing/
    │   ├── ResumableBatchingCursorSteps.cs    # NEW — Reqnroll bindings
    │   ├── ResumableBatchingCursorContext.cs  # NEW — shared test context
    │   └── ResumableBatchingContractTests.cs  # NEW — unit tests for token/decision
    ├── Inventory/
    │   └── WorkItemQueryWindowStrategyTests.cs  # MODIFIED — resume window tests
    └── Services/
        └── AzureDevOpsWorkItemFetchServiceTests.cs  # MODIFIED — checkpoint emission tests
```

**Structure Decision**: No new projects. All changes extend existing projects in the established three-layer split (Abstractions → Infrastructure → Infrastructure.AzureDevOps). Tests co-locate with existing test project.

## Complexity Tracking

No constitution violations. All changes are additive extensions of existing abstractions.

## Architecture Review Findings (Post-Plan)

**Date**: 2026-04-22 | **Scope**: Planned changes only | **Result**: 0 Critical, 3 Medium, 1 Low

### Finding 1 (Medium): ResumeDecision API delivery mechanism

**Issue**: The contract specifies a `ResumeDecision` but no existing method signature returns it.

**Resolution**: `AzureDevOpsWorkItemFetchService.FetchAsync()` will evaluate the resume decision internally at the start of enumeration. The decision is:
- **Accepted**: Skip windows up to the saved token position; emit structured log + OTel metric.
- **RejectedQueryMismatch**: Throw `ResumeRejectedException` (a new checked exception extending `InvalidOperationException`) containing the `ResumeDecision` object. Caller catches and decides recovery.
- **Unavailable**: Log info; proceed from beginning (no error).

The window strategy itself stays streaming (`IAsyncEnumerable`) — the decision is evaluated once before enumeration starts. This avoids changing the async-enumerable signature while giving callers explicit mismatch feedback.

### Finding 2 (Medium): QueryFingerprintService injection point

**Resolution**: `IQueryFingerprintService` is injected into `AzureDevOpsWorkItemFetchService` via constructor. The fetch service computes the fingerprint from `WorkItemFetchScope.BaseQuery` + `WorkItemQueryWindowOptions.QueryParameters` at the start of `FetchAsync()`, before passing options to the window strategy. The fingerprint is embedded in each emitted `BatchContinuationToken`. The window strategy does not need the fingerprint service — it remains query-only.

### Finding 3 (Medium): ContinuationCheckpointWriter nullability

**Resolution**: When `ResumeEnabled = true` but `ContinuationCheckpointWriter` is null, the fetch service emits a warning-level structured log: `"Resume enabled but no checkpoint writer provided; continuation state will not be persisted."` Checkpoints are silently skipped (no exception). This matches the caller-owned persistence contract while making the omission observable.

### Finding 4 (Low): Token versioning strategy

**Resolution**: `StrategyVersion` starts at `"1.0"`. Tokens with unknown versions are treated as `RejectedQueryMismatch` with reason `"incompatible_strategy_version"`. New fields will always have safe defaults for forward compatibility. This is already specified in the error model table of the contract.

---

## Codebase Architecture Review (Pre-Implementation Baseline)

**Date**: 2026-04-22 | **Scope**: Entire solution | **Perspectives**: Modular Monolith · Clean Architecture · Hexagonal · Vertical Slice · Screaming Architecture

### Summary

| Perspective | Critical | High | Medium | Low | Info |
|---|---|---|---|---|---|
| Modular Monolith [MM] | 2 | 0 | 2 | 0 | — |
| Clean Architecture [CA] | 0 | 0 | 0 | 1 | — |
| Hexagonal [HX] | 0 | 0 | 0 | 1 | — |
| Vertical Slice [VS] | 0 | 0 | 1 | 0 | — |
| Screaming Architecture [SA] | 0 | 2 | 2 | 3 | 2 |
| **Total** | **2** | **2** | **5** | **5** | **2** |

### Critical

- **[MM-C1]** `QueueCommand.cs:347` — casts to concrete `AzureDevOpsEndpointOptions` to extract auth token. Fix: add auth accessor to an abstraction interface.
- **[MM-C2]** `QueueCommand.cs:353` — calls `WiqlValidator.Validate()` directly (concrete AzureDevOps type). Fix: define `IWorkItemQueryValidator` in Abstractions; implement in Infrastructure.AzureDevOps; inject via DI.

### High

- **[SA-H1]** `Abstractions/Utilities/PathUtilities.cs` — generic `Utilities` namespace. Rename to domain-specific (e.g. `Configuration.EndpointPath`).
- **[SA-H2]** `CLI.Migration/Utilities/PathUtilities.cs` — same generic `Utilities` namespace in CLI project.

### Medium

- **[MM-M1]** `LocalStackHost.cs:65-66` — registers concrete endpoint option types directly instead of delegating to `AddMigrationCliEndpointTypes()`.
- **[MM-M2]** `InventoryCommand.cs:54`, `DependencyCommand.cs:54` — discovery commands call infrastructure extensions directly.
- **[VS-M1]** `WorkItemsModule.cs:114,179` — directly instantiates Export/Import orchestrators instead of injecting via factory/DI.
- **[SA-M1]** `RevisionFolderProcessor.cs` — generic "Processor" class name; rename to `RevisionFolderImporter`.
- **[SA-M2]** `RevisionFolderProcessorFactory.cs` — rename to `RevisionFolderImporterFactory`.

### Low

- **[CA-L1]** `AzureBlobArtefactStore.cs` — stub throws `NotImplementedException`; remove or document future plan.
- **[HX-L1]** `WorkItemsModule.cs:209,212` — `File.Exists()` called directly; extract to `IIdMapPathResolver` abstraction.
- **[SA-L1]** `RevisionFolderProcessor.ProcessAsync()` — rename to `ImportAsync()`.
- **[SA-L2]** `TfsExportAgent.RunAsync()` — rename to `ExportAsync()`.
- **[SA-L3]** `TelemetryPoller.RunAsync()` — rename to `PollTelemetryAsync()`.

### Informational

- **[SA-I1]** `polymorphic-endpoint-config.feature:7` — uses "deserializes" (technical language).
- **[SA-I2]** Multiple `.feature` files use "JSON", "HTTP", "API" in scenario names.

### Cross-Cutting Patterns

1. **CLI → Infrastructure leakage** (MM-C1 + MM-C2): QueueCommand.cs casts to AzureDevOps types and calls concrete validators. Root cause: missing abstraction for query validation and endpoint auth. One refactoring session resolves both.
2. **Processor naming chain** (SA-M1 + SA-M2 + SA-L1): `Processor` → `Importer` rename resolves 3 findings at once.

### Relevance to Feature 020

The 2 Critical findings (MM-C1, MM-C2) are in `QueueCommand.cs` which is the CLI entry point for submitting export/import jobs. Feature 020 does not modify `QueueCommand` — no blocking dependency. However, if future work wires resumable batching options through the CLI, the abstraction gap should be resolved first.

**Recommendation**: Fix MM-C1 and MM-C2 before or alongside feature 020 implementation to avoid accumulating more CLI coupling.

# Architecture Discrepancies

**Feature**: Module IModule Phase Consolidation and IAnalyser Introduction
**Flagged by**: speckit.specify
**Status**: Resolved

## Discrepancies

### IModule contract missing InventoryAsync and PrepareAsync

- **Source doc**: `docs/module-development-guide.md`
- **Section**: "IModule Contract" (lines 9–19)
- **Issue**: The documented `IModule` interface shows only `ExportAsync`, `PrepareAsync`, `ImportAsync`, and `ValidateAsync`. It does not include `InventoryAsync`, and `PrepareAsync` while shown in the doc, is absent from the actual interface implementation. The four `Supports*` boolean properties are also undocumented.
- **Suggested update**: Update the `IModule` contract code block to include `InventoryAsync(InventoryContext context, CancellationToken ct)`, `PrepareAsync(PrepareContext context, CancellationToken ct)`, and the four capability flags (`SupportsInventory`, `SupportsExport`, `SupportsPrepare`, `SupportsImport`).
- **Status**: ✓ Resolved in speckit.implement

### Discovery modules section describes classes to be eliminated

- **Source doc**: `docs/module-development-guide.md`
- **Section**: "Discovery Modules" (lines 234–243)
- **Issue**: `InventoryModule`, `InventoryDiscoveryModule`, and `DependencyDiscoveryModule` are described as active implementations. All three are eliminated by this spec: the first two fold into `WorkItemsModule.InventoryAsync` and the orchestrator multi-org loop; the third becomes `DependencyAnalyser : IAnalyser`.
- **Suggested update**: Replace the "Discovery Modules" section with a description of the `IAnalyser` interface and the `DependencyAnalyser` implementation. Remove `InventoryModule`, `InventoryDiscoveryModule`, and `DependencyDiscoveryModule` from the module table. Add note that inventory is now an intrinsic phase on each domain module.
- **Status**: ✓ Resolved in speckit.implement

### Module → Orchestrator mapping table references eliminated classes

- **Source doc**: `docs/module-development-guide.md`
- **Section**: "Module ↔ Orchestrator Mapping" (lines 72–82)
- **Issue**: `DependencyDiscoveryModule → IDependencyOrchestrator`, `InventoryModule → IInventoryOrchestrator`, and `InventoryDiscoveryModule → IInventoryOrchestrator` all reference classes being removed.
- **Suggested update**: Remove the three eliminated rows. Add a row for `DependencyAnalyser → IDependencyOrchestrator` (if orchestrator is retained) or document that `DependencyAnalyser` directly uses `IArtefactStore` without a separate orchestrator.
- **Status**: ✓ Resolved in speckit.implement

### IInventoryOrchestrator interface retention unclear after elimination

- **Source doc**: `docs/module-development-guide.md`
- **Section**: "Interface Contracts" (lines 55–63)
- **Issue**: `IInventoryOrchestrator` is listed as a shared orchestrator for `InventoryModule` and `InventoryDiscoveryModule`. After both are eliminated, whether `IInventoryOrchestrator` is retained (reused inside `WorkItemsModule.InventoryAsync`) or also removed is not yet decided in the docs.
- **Suggested update**: Clarify in docs whether `IInventoryOrchestrator` is retained as a service injected into `WorkItemsModule`, or replaced by inline orchestration within `WorkItemsModule.InventoryAsync`. Update the Interface Contracts table accordingly.
- **Status**: ✓ Resolved in speckit.implement

### Phase dispatch table in docs/architecture.md incomplete

- **Source doc**: `docs/architecture.md`
- **Section**: Phase gate rules / JobKind dispatch
- **Issue**: `JobKind.Prepare` exists in the `JobKind` enum and is referenced in guardrail 10, but there is no documented phase dispatch table showing that `Prepare` calls `PrepareAsync` on modules. The `Migrate` pipeline sequence (`inventory → export → prepare → import → validate`) is not explicitly documented in the architecture doc.
- **Suggested update**: Add a `JobKind` → Phase dispatch table to `docs/architecture.md` (or `docs/module-development-guide.md`) matching the table in `analysis/draftspec-Module-refactor-consolidation.md` section "JobKind Dispatch — Updated".
- **Status**: ✓ Resolved in speckit.implement

### module-rules.md does not cover InventoryAsync or PrepareAsync

- **Source doc**: `.agents/guardrails/module-rules.md`
- **Section**: Full checklist for new modules
- **Issue**: The module template only describes `ExportAsync` and `ImportAsync` implementation steps. After this spec, new modules must also implement `InventoryAsync` (if `SupportsInventory`) and `PrepareAsync` (if `SupportsPrepare`).
- **Suggested update**: Add checklist items for `InventoryAsync` and `PrepareAsync` implementations, including the required `prepare-report.json` output contract and the `SupportsInventory`/`SupportsPrepare` property declarations.
- **Status**: ✓ Resolved in speckit.implement

### IAnalyser interface not documented anywhere

- **Source doc**: `docs/module-development-guide.md` (and `docs/architecture.md`)
- **Section**: N/A — `IAnalyser` does not yet exist in any doc
- **Issue**: This spec introduces `IAnalyser` as a first-class platform interface alongside `IModule`. Neither `docs/module-development-guide.md` nor `docs/architecture.md` describes it.
- **Suggested update**: Add an "Analysers" section to `docs/module-development-guide.md` documenting the `IAnalyser` interface contract, `AnalyseContext`, invariants (no target writes, must produce at least one artefact, declares `DependsOn`), and the `DependencyAnalyser` as the initial implementation. Add `IAnalyser` to the extension-point list in `docs/architecture.md`.
- **Status**: ✓ Resolved in speckit.implement

### DependencyPhase enum extension not documented

- **Source doc**: `docs/module-development-guide.md` (or architecture doc)
- **Section**: N/A
- **Issue**: `DependencyPhase` currently has values `Export`, `Import`, and `Both`. This spec adds `Inventory = 0`, `Prepare = 4`, and `Analyse = 5`. No doc describes the current or extended enum values.
- **Suggested update**: Document `DependencyPhase` values and their usage in a new "Module Dependencies and DependsOn" subsection in `docs/module-development-guide.md`.
- **Status**: ✓ Resolved in speckit.implement

### architecture-boundaries.md Rule 24 does not define the {Stem}Analyser naming convention

- **Source doc**: `.agents/guardrails/architecture-boundaries.md`
- **Section**: Rule 24 — Module/Tool identifier naming convention
- **Issue**: Rule 24 defines conventions for `{Stem}Module` (config path, Name property, cursor key, file name) and `{Stem}Tool` (folder, file, DI extension, interface, options, config). It does not define the equivalent convention for `{Stem}Analyser`, which this spec introduces. Without a documented convention, subsequent analysers could be named or configured inconsistently.
- **Suggested update**: Extend Rule 24 to include: `{Stem}Analyser: Name = "{Stem}", config = "MigrationPlatform:Analysers:{Stem}", DI extension = Add{Stem}AnalyserServices (or grouped in AddAnalyserServices), interface = IAnalyser, file = {Stem}Analyser.cs`. This must be updated in `architecture-boundaries.md` as a doc-task in `tasks.md`.
- **Status**: ✓ Resolved in speckit.implement

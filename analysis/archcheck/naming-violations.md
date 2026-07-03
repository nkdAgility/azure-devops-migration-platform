# Taxonomy and Phase-Naming Violation Sweep

> **Update 2026-07-03:** 10 renames executed as pure renames under ADR-0029 — `IClassificationTreeReader` family → `*Source`, revision `*Mapper` → `*Processor`, `TfsAttachmentRegistry` → `TfsAttachmentIdStore`, `ReferencedPathTracker` → `ReferencedPathLifecycle`, `IWorkerEventWriter` → `IWorkerEventSink`, `IJobPlanExecutor` → `IJobPlanOrchestrator`. Remaining rows below are unactioned.

Read-only audit of public type names in `src/` against:

1. `.agents/20-guardrails/core/taxonomy-naming.md` (glossary role suffixes)
2. `.agents/20-guardrails/core/coding-standards.md` (domain language, no `ADO` shorthand)
3. Operator rule: type names must not embed phase words **Import/Export** (phase is dispatch context, not identity)

Scope: 787 public type declarations (760 unique names) across all `src/` projects. Branch `update-for-comms`, 2026-07-03.
Reference counts (`refs`) are rough whole-word occurrence counts across `src/` + `tests/`.

Severity: **High** = public `Abstractions*` surface · **Medium** = internal cross-project · **Low** = local.

---

## Category A — Suffix not in glossary taxonomy

### A.1 Suffixes sanctioned elsewhere (glossary gap, NOT flagged as violations)

The contract specs in `.agents/10-contracts/specs/*` and `surface-catalog.yaml` themselves use these suffixes by name (e.g. `IArtefactStore`, `IStateStore`, `IProgressSink`, `IPackageStoreFactory`, `ICurrentAgentJobContextAccessor`, `IJobExecutionPlanBuilder`, `IConnectorCapabilityProvider`, `IControlPlaneTelemetryClient`, `IFieldTransformValidator`). Since contracts define canonical surfaces, these suffix families are treated as **sanctioned but missing from the glossary** — the fix is a glossary amendment, not renames:

| Suffix | Count in src/ | Sanctioning evidence |
|---|---|---|
| `*Factory` | 45 | `IPackageStoreFactory`, `IWorkItemExportOrchestratorFactory` etc. in contract specs |
| `*Store` | 17 | `IArtefactStore`, `IStateStore` in `surface-catalog.yaml` |
| `*Provider` | 16 | `IConnectorCapabilityProvider`, `PackageLoggerProvider` in specs (also .NET `ILoggerProvider` convention) |
| `*Client` | 6 | `IControlPlaneTelemetryClient` in observability-transport-contract |
| `*Sink` | 4 | `IProgressSink` in specs |
| `*Builder` | 4 | `IJobExecutionPlanBuilder` in task-plan-contract |
| `*Accessor` | 6 | `ICurrentAgentJobContextAccessor` in runtime-context-contract |
| `*Validator` | 11 | `IFieldTransformValidator`, `PackageValidator` in specs |

**Recommendation:** add these eight roles to the taxonomy glossary (or explicitly deprecate them with a migration plan). Until then they are ambiguous, which is itself a guardrail defect.

### A.2 Truly unsanctioned suffixes (violations — 26 types)

No glossary role and no contract-spec sanction found.

| File:Line | Current name | Suggested compliant name | Refs | Severity |
|---|---|---|---|---|
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IClassificationTreeReader.cs:14` | `IClassificationTreeReader` | `IClassificationTreeSource` or `IClassificationTreeAdapter` | 42 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/CompositeClassificationTreeReader.cs:18` | `CompositeClassificationTreeReader` | follows interface rename | 5 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/CompositeClassificationTreeReader.cs:71` | `KeyedClassificationTreeReader` | follows interface rename | 4 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedClassificationTreeReader.cs:21` | `SimulatedClassificationTreeReader` | follows interface rename | 5 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Nodes/TfsClassificationTreeReader.cs:25` | `TfsClassificationTreeReader` | follows interface rename | 8 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/ProjectInventory.cs:38` | `IProjectInventoryReader` | `IProjectInventorySource` / merge into a `ProjectInventory` service contract | 14 | High |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/ProjectInventory.cs:56` | `IProjectInventoryWriter` | `IProjectInventoryService` (impure single-concern I/O = Service) | 22 | High |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/IWorkItemRevisionReader.cs:15` | `IWorkItemRevisionReader` | `IWorkItemRevisionSource` (sibling `*RevisionSourceFactory` family already exists) | 26 | High |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Telemetry/IWorkerEventWriter.cs:14` | `IWorkerEventWriter` | `IWorkerEventService` or `IWorkerEventSink` (Sink pending A.1 sanction) | 25 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/UnifiedWorkerEventWriter.cs:34` | `UnifiedWorkerEventWriter` | follows interface rename | 57 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure/Serialization/EndpointOptionsTypeRegistry.cs:14` | `EndpointOptionsTypeRegistry` | `EndpointOptionsTypeResolver` (it owns a decision outcome) | 32 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Attachments/TfsAttachmentRegistry.cs:13` | `TfsAttachmentRegistry` | `TfsAttachmentAdapter` or `TfsAttachmentService` | 14 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IReferencedPathTracker.cs:14` | `IReferencedPathTracker` | `IReferencedPathLifecycle` or `IReferencedPathTool` (if pure) | 27 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/ReferencedPathTracker.cs:23` | `ReferencedPathTracker` | follows interface rename | 13 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WorkItems/Revisions/AzureDevOpsWorkItemRevisionMapper.cs:19` | `IAzureDevOpsWorkItemRevisionMapper` (+impl `AzureDevOpsWorkItemRevisionMapper`) | `I...RevisionTranslationTool` (pure mapping = Tool) | 10 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItems/Revisions/TfsWorkItemRevisionMapper.cs:20` | `IWorkItemRevisionMapper` | `IWorkItemRevisionTranslationTool` | 8 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItems/Revisions/TfsWorkItemRevisionMapper.cs:25` | `TfsWorkItemRevisionMapper` | follows interface rename | 7 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Storage/IPackageMigrationConfigLoader.cs:24` | `IPackageMigrationConfigLoader` | `IPackageMigrationConfigService` | 19 | High |
| `src/DevOpsMigrationPlatform.CLI.Migration/Views/IUiDispatcher.cs:12` | `IUiDispatcher` | borderline — UI-thread dispatcher is a Terminal.Gui idiom; suggest glossary exemption for UI layer | 7 | Low |
| `src/DevOpsMigrationPlatform.CLI.Migration/Views/TerminalGuiDispatcher.cs:13` | `TerminalGuiDispatcher` | as above | 2 | Low |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs:27` | `IJobPlanExecutor` | `IJobPlanWorker` or `IJobPlanOrchestrator` (it dispatches ordered plan work) | 13 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs:37` | `JobPlanExecutor` | follows interface rename | 30 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/DiagnosticsFileMetricExporter.cs:13` | `DiagnosticsFileMetricExporter` | borderline — implements OpenTelemetry `BaseExporter`; framework convention, suggest exemption | 3 | Low |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/DiagnosticsFileTraceExporter.cs:13` | `DiagnosticsFileTraceExporter` | as above | 3 | Low |
| `src/DevOpsMigrationPlatform.Abstractions.Storage/IPackagePreparer.cs:20` | `IPackagePreparer` | `IPackageLifecycle` (owns initialization/seed state transitions) | 11 | High |

No `*Manager`, `*Helper`, `*Handler`, or `*Util` types exist in `src/`.

---

## Category B — Import/Export embedded in type identity (operator rule)

Phase-symmetric components carrying a phase word as identity. **Hard violations (23 types):**

| File:Line | Current name | Suggested compliant name | Refs | Severity |
|---|---|---|---|---|
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Export/IWorkItemExportOrchestrator.cs:9` | `IWorkItemExportOrchestrator` | `IWorkItemsOrchestrator` (glossary's own example) | 6 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs:47` | `WorkItemExportOrchestrator` | `WorkItemsOrchestrator` | 27 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Export/IWorkItemExportOrchestratorFactory.cs:19` | `IWorkItemExportOrchestratorFactory` | `IWorkItemsOrchestratorFactory` | 11 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestratorFactory.cs:16` | `WorkItemExportOrchestratorFactory` | `WorkItemsOrchestratorFactory` | 3 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamExportOrchestrator.cs:34` | `TeamExportOrchestrator` | merge/rename to phase-symmetric `TeamsOrchestrator` | 38 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:24` | `TeamImportOrchestrator` | as above | 56 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Attachments/IEmbeddedImageExportService.cs:13` | `IEmbeddedImageExportService` | `IEmbeddedImageService` or `IEmbeddedImageCaptureService` | 4 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/EmbeddedImageExportService.cs:28` | `EmbeddedImageExportService` | `EmbeddedImageService` | 13 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/IWorkItemsImportCapabilityValidator.cs:9` | `IWorkItemsImportCapabilityValidator` | `IWorkItemsCapabilityValidator` | 8 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemType/WorkItemsImportCapabilityValidator.cs:13` | `WorkItemsImportCapabilityValidator` | `WorkItemsCapabilityValidator` | 5 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Telemetry/IWorkItemExportMetrics.cs:13` | `IWorkItemExportMetrics` | `IWorkItemMetrics` (phase becomes a tag/dimension) | 9 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItems/Telemetry/WorkItemExportMetrics.cs:15` | `WorkItemExportMetrics` | `WorkItemMetrics` | 3 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/IExportProgressStore.cs:14` | `IExportProgressStore` | `IWorkItemProgressStore` (phase-scoped instance, not identity) | 17 | High |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/IExportProgressStoreFactory.cs:9` | `IExportProgressStoreFactory` | `IWorkItemProgressStoreFactory` | 22 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/SqliteExportProgressStore.cs:29` | `SqliteExportProgressStore` | `SqliteWorkItemProgressStore` | 7 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/ExportProgressStoreFactory.cs:12` | `ExportProgressStoreFactory` | `WorkItemProgressStoreFactory` | 6 | Medium |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemResolution/ImportWorkItemStateStore.cs:20` | `ImportWorkItemStateStore` | `WorkItemReplayStateStore` | 26 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/IImportCreatedNodeStateStore.cs:11` | `IImportCreatedNodeStateStore` | `ICreatedNodeStateStore` | 6 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemResolution/ImportPreparer.cs:23` | `ImportPreparer` | `WorkItemReadinessLifecycle` (double violation: phase word + unsanctioned `Preparer`) | 16 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/WorkItemExportProgress.cs:14` | `WorkItemExportProgress` | `WorkItemProgress` | 9 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Extensions/WorkItemRevisionExportContext.cs:15` | `WorkItemRevisionExportContext` | `WorkItemRevisionCaptureContext` or plain `WorkItemRevisionContext` | 4 | Medium |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/ImportedWorkItemResult.cs:9` | `ImportedWorkItemResult` | `MigratedWorkItemResult` / `WorkItemReplayResult` | 23 | High |
| `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItems/ImportContext.cs` region — see also namespace folders `Export/`, `Import/` which encode phase into structure | — | fold into phase-symmetric capability folders | — | Medium |

**Borderline (flagged, judgment call — 11 types):**

| Name | Location | Verdict |
|---|---|---|
| `ExportContext` (refs=75) | `Abstractions.Agent/Export/ExportContext.cs:17` | Phase dispatch-context object — phase IS its identity, arguably legitimate; but symmetric merge with `ImportContext` into `PhaseContext`/`MigrationContext` would be cleaner. High blast radius. |
| `ImportContext` (refs=77) | `Abstractions.Agent/WorkItems/ImportContext.cs:13` | Same as above. |
| `ImportReadinessReport` (refs=46), `IImportFailurePattern` (35), `ImportFailureFinding` (52), `ImportFailureSeverity` (30), `ImportFailurePatternContext` (30) | `Abstractions.Agent/WorkItems/` | Prepare-gate domain vocabulary ("readiness for import") — plausibly genuine domain language, but the whole family could rename to `Readiness*` without loss. Flag for operator decision. |
| `BoardConfigImportMode` (refs=18) | `Abstractions.Agent/Teams/BoardConfigImportMode.cs:7` | Options enum describing a mode — legitimate per operator carve-out. |
| `ExportServiceCollectionExtensions` / `ImportServiceCollectionExtensions` | `Infrastructure.AzureDevOps` | DI wiring classes named after phase folders; low value, rename with folder restructure. |
| `SimulatedExportDataOptions`-style option types (various) | — | Mode-describing options; legitimate. |

---

## Category C — `ADO` shorthand in type names

**None found.** No type name contains `ADO`, `Ado`, or `AzDo`. Compliant.

---

## Category D — Role-suffix misuse (named X, behaves like Y)

All five `*Tool` implementations (`BoardConfigMergeTool`, `EmbeddedImageReferenceTool`, `FieldTransformTool`, `IdentityTranslationTool`, `NodeTranslationTool`) were inspected for I/O (`IPackageAccess`, `File.*`, `HttpClient`, streams, artefact stores): **all clean/pure** — no ADR-0026 violations.

One lower-confidence observation (not counted as a violation): `ImportPreparer` performs state seeding/rebuild, which is Lifecycle behavior wearing an ad-hoc suffix — covered in Category B/A.2.

---

## Prioritised Remediation Table

| # | Rename | Class | Effort | Priority |
|---|---|---|---|---|
| 1 | Amend taxonomy glossary to sanction (or deprecate) Factory/Store/Provider/Client/Sink/Builder/Accessor/Validator | Docs-only (guardrail edit, operator consent) | S | P1 — unblocks all other classification |
| 2 | `IWorkItemExportOrchestrator*` family → `IWorkItemsOrchestrator*` | **Class C** (Abstractions.Agent contract; surface-catalog names orchestration surfaces) | M | P1 |
| 3 | `TeamExportOrchestrator`/`TeamImportOrchestrator` → phase-symmetric `TeamsOrchestrator` | Mechanical→B (internal, but touches module dispatch) | M | P1 |
| 4 | `IExportProgressStore(+Factory)`, `WorkItemExportProgress` → `WorkItemProgress*` | **Class C** (Abstractions.Agent) | M | P2 |
| 5 | `IEmbeddedImageExportService` → `IEmbeddedImageService` | **Class C** (Abstractions.Agent) | S | P2 |
| 6 | `IWorkItemsImportCapabilityValidator`, `IImportCreatedNodeStateStore`, `ImportedWorkItemResult`, `IWorkItemExportMetrics` | **Class C** (Abstractions.Agent) | M | P2 |
| 7 | `ImportPreparer`, `ImportWorkItemStateStore`, `WorkItemRevisionExportContext`, `Sqlite/ExportProgressStoreFactory` impls | Mechanical (internal) | S | P3 |
| 8 | `IClassificationTreeReader` family, `IWorkItemRevisionReader`, `IProjectInventoryReader/Writer`, `IWorkerEventWriter`, `IPackagePreparer`, `IPackageMigrationConfigLoader`, `IJobPlanExecutor` | **Class C** (Abstractions contracts) | L | P3 |
| 9 | `EndpointOptionsTypeRegistry`, `TfsAttachmentRegistry`, `ReferencedPathTracker`, revision `Mapper`s, `UnifiedWorkerEventWriter` | Mechanical (internal) | M | P4 |
| 10 | Decide `ExportContext`/`ImportContext` and `ImportReadiness*`/`ImportFailure*` family (borderline, refs 30–77 each) | **Class C** if renamed — operator decision required first | L | P4 |

**Class C note:** every rename of a type in `DevOpsMigrationPlatform.Abstractions*` (items 2, 4, 5, 6, 8, 10) alters a public contract surface and per `change-classes.yaml` requires explicit operator consent, an ADR add/update in the same change, contract compatibility tests, and a test-first trace. Renames of `Infrastructure*`-internal implementations (items 3, 7, 9) are mechanical Class A/B refactors executable via IDE rename with behavioral tests.

## Totals

- **A.1** glossary-gap suffixes (sanctioned by contracts, not violations): 8 suffix families, ~109 types
- **A.2** truly unsanctioned suffix violations: **26** (21 firm + 5 borderline/framework-convention)
- **B** Import/Export phase-word violations: **23 firm + 11 borderline**
- **C** ADO shorthand: **0**
- **D** high-confidence role misuse: **0**

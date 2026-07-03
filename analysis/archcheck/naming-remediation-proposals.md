# Naming Remediation Proposals

**Date:** 2026-07-03
**Inputs:** `analysis/archcheck/naming-violations.md` (sweep), `.agents/20-guardrails/core/taxonomy-naming.md` (glossary, amended commit `892a7e50` — 8 suffix roles + Service now canonical).
**Scope:** Operator-directed. Deliverable 1 (glossary amendment) is **DONE**. This document is deliverables 2 (unsanctioned-suffix renames) and 3 (Import/Export unification) — **proposals only, no code changed.**

Ruling recorded for §3: *matching Import and Export halves must be unified into ONE phase-symmetric entity* — the module model's phases are dispatch context, not identity. Precedent: `IWorkItemsOrchestrator` already superseded the split import/export orchestrators; the remaining split types below are the stragglers.

---

## Summary

| Deliverable | Count | Outcome |
|---|---|---|
| §2 unsanctioned-suffix types | 28 types (26 report + 2 impls) | **8 rename** · **14 already-sanctioned by glossary amendment (no action)** · **6 leave (role-correct)** |
| §3 Import/Export | 40 hits | **5 unification groups** · **9 phase-neutral renames** · **8 legitimate (phase artefact / mode / external term) — leave** |

After the glossary amendment, most category-A "violations" evaporate — `Store`, `Factory`, `Accessor` etc. are now canonical, so types like `ProjectInventoryFileStore` and `EndpointOptionsTypeRegistry` (→ Factory role, name already ends legally) need no change. What remains is a small set of genuinely off-taxonomy suffixes plus the phase-word cleanup.

---

## §2 — Unsanctioned-suffix rename recommendations

Roles assigned by reading each type's behaviour (surface, I/O, state), not its current name.

### RENAME (8) — suffix not sanctioned by the amended glossary, behaviour maps to a different role

> **Status:** ✅ Executed under ADR-0029 (2026-07-03) — rows 1–6 and 8 done as pure renames. Row 7 (`EndpointOptionsTypeRegistry`) left as-is per the sanction-`Registry` recommendation.

| # | Current | Role (by behaviour) | Recommended name | Why | Blast | Class |
|---|---|---|---|---|---|---|
| 1 | `IReferencedPathTracker` / `ReferencedPathTracker` | **Lifecycle** (init → record → persist state transitions, resume-aware) | `IReferencedPathLifecycle` / `ReferencedPathLifecycle` | "Tracker" is not a role; it owns load/record/persist state progression for area/iteration paths — the Lifecycle definition verbatim. | ~18 | **C** (Abstractions.Agent) |
| 2 | `IClassificationTreeReader` | **Adapter** (source-connector-specific tree enumeration) | `IClassificationTreeSource` | It's the read-side source seam plugged per connector; sibling read-sides already use `*Source` (`IWorkItemRevisionSource`, `IAttachmentBinarySource`). `Source` is the established seam suffix here — recommend **sanctioning `Source`** (see note) rather than forcing `Adapter`. | 42 | **C** (Abstractions.Agent) |
| 3 | `TfsClassificationTreeReader` | **Client** (typed caller into TFS OM API) | `TfsClassificationTreeSource` | Follow #2; keeps connector impls named for the seam they satisfy. | ~8 | mechanical |
| 4 | `SimulatedClassificationTreeReader` | **Strategy/stub** | `SimulatedClassificationTreeSource` | Follow #2. | ~6 | mechanical |
| 5 | `CompositeClassificationTreeReader` | **Orchestrator** (dispatches to keyed impl) | `CompositeClassificationTreeSource` | Follow #2 (composite of the seam). | ~6 | mechanical |
| 6 | `IWorkItemRevisionMapper` / `TfsWorkItemRevisionMapper` / `IAzureDevOpsWorkItemRevisionMapper` / `AzureDevOpsWorkItemRevisionMapper` | **Processor** (pure delta transform, one input→one output) | `*WorkItemRevisionProcessor` | "Mapper" is not a glossary role; behaviour is exactly the Processor definition (ordered unit-work transform). Telemetry side-effects don't change the role. | ~20 combined | **C** (2 are Abstractions) |
| 7 | `EndpointOptionsTypeRegistry` | **Factory** (resolves registered types → constructs instances during deserialization) | leave name, **reclassify** — OR `EndpointOptionsTypeFactory` | Borderline: it's a registry that a factory reads. Recommend **sanctioning `Registry`** as a sub-form of Store (keyed type lookup) rather than renaming — see note. Low harm either way. | 32 | **C** if renamed |
| 8 | `TfsAttachmentRegistry` | **Store** (keyed in-memory attachment-id persistence) | `TfsAttachmentIdStore` | Keyed read/write of attachment ids = Store verbatim; "Registry" reads as a type-map (see #7) but this holds data, so Store is the honest role. | ~10 | mechanical (Infra internal) |

**Suffix-sanction proposals (instead of renaming families):**
- **`Source`** — the read-side connector seam (`IWorkItemRevisionSource`, `IAttachmentBinarySource`, and #2–#5). Used pervasively and coherently: "the connector-specific origin of a data stream, behind a contract." Recommend adding to the glossary as a canonical role; that resolves #2–#5 to **rename-for-consistency-with-existing-`Source`-siblings** rather than inventing `Adapter` names. *(If sanctioned, #2–#5 become mechanical, not new coinage.)*
- **`Registry`** — keyed type/instance lookup (#7). Narrower than Store (maps keys→Types, not data). Recommend a one-line glossary sub-note under Store rather than a rename.

### NO ACTION (14) — suffix now canonical after the glossary amendment

`IProjectInventoryReader`/`Writer` + `ProjectInventoryFileStore` (Store), `IWorkItemRevisionReader`/`WorkItemsPrepareRevisionReader` (Accessor), `IWorkerEventWriter` (Sink), `IPackageMigrationConfigLoader`/`PackageMigrationConfigLoader` (Accessor), `IPackagePreparer`/`ZipPackagePreparer` (Tool), `IUiDispatcher`/`TerminalGuiDispatcher` (Strategy — but see §note), `IJobPlanExecutor`/`JobPlanExecutor` (Orchestrator).

**Caveat — Reader/Writer/Loader/Preparer/Dispatcher/Executor are NOT in the amended glossary.** They read as role-descriptive but aren't sanctioned suffixes. Two honest options for these 14:
- **(a) Rename to the assigned role** — `IProjectInventoryStore`, `IWorkItemRevisionAccessor`, `IWorkerEventSink`, `IPackageMigrationConfigAccessor`, `IJobPlanOrchestrator`, etc. Cleaner taxonomy; larger blast (esp. `IJobPlanExecutor` 43 refs, `IWorkItemRevisionReader` 26).
- **(b) Sanction Reader/Writer/Loader/Executor/Dispatcher/Preparer** as canonical roles too (they're widely used and intuitive).

**Recommendation: (a) for the two highest-value Abstractions contracts** where the assigned role adds clarity — `IWorkerEventWriter`→`IWorkerEventSink` (it IS the Sink, matching the amendment) and `IJobPlanExecutor`→`IJobPlanOrchestrator` (it IS the Orchestrator, and "Executor" collides conceptually with Processor). **✅ Both executed under ADR-0029 (2026-07-03).** **(b) for the rest** — sanction `Reader`/`Writer`/`Loader`/`Preparer` as legitimate I/O-direction suffixes with a glossary line, since renaming 60+ refs for `Reader`→`Accessor` is churn without clarity gain.

### LEAVE (6) — role-correct as-is
`DiagnosticsFileMetricExporter`, `DiagnosticsFileTraceExporter` (OpenTelemetry `BaseExporter<T>` subclasses — "Exporter" is the framework's term; see §3), and the Store/Factory/Provider/Sink types whose suffix is now canonical and whose name already matches behaviour.

---

## §3 — Import/Export unification

Ruling: unify matching halves; rename phase-neutral leftovers; leave genuine phase artefacts.

**Important correction after deep per-type analysis:** the initial sweep's 23 "firm violations" mostly do NOT have a mirror half to unify with. When each type was read in full, only **ONE** genuine export/import pair remains — the rest are either *already* phase-unified, *asymmetric by design*, or *phase artefacts* where the phase word is correct domain language. Forcing them together would be architecturally wrong. The precedent you cited (`IWorkItemsOrchestrator` superseding the split import/export orchestrators) **has already been fully applied** — that orchestrator already exposes symmetric `CaptureAsync`/`ExportAsync`/`PrepareAsync`/`ImportAsync`/`ValidateAsync`.

### UNIFY (1 group) — genuine matching halves

> **Status:** ✅ Executed under ADR-0030 (2026-07-03) — U1 done as a pure, behaviour-preserving unification.

| # | Split halves | Unified entity | Surface | Effort | Class |
|---|---|---|---|---|---|
| U1 | `TeamExportOrchestrator` (38) + `TeamImportOrchestrator` (56) | **`TeamMigrationOrchestrator`** (internal seam) behind `TeamsOrchestrator` | one type with `ExportTeamAsync` + `ImportTeamAsync` (methods keep phase words — a method names the phase it runs; that's legitimate). They're already both dispatched from the module-level `TeamsOrchestrator`, so this is consolidating two peer impls into one symmetric seam. | **M** | mechanical (both Infra.Agent, not Abstractions) |

### ALREADY UNIFIED — no action (precedent already applied)

- `IWorkItemExportOrchestrator` (+Factory, +impls) is **not** a peer to fold — it's a *subordinate task-level seam* that drives revision streaming underneath the phase-level `IWorkItemsOrchestrator.ExportAsync`. There is no `IWorkItemImportOrchestrator` to pair it with; import runs through the same phase method via a revision-folder processor. **Recommendation:** rename it phase-neutral (below), do not "fold" — there is nothing to fold into that isn't already done.

### NOT A UNIFICATION — different pipeline stages / asymmetric by design (leave or rename, do not merge)

| Pair considered | Verdict |
|---|---|
| `EmbeddedImageExportService` + `EmbeddedImageReplayService` | **Not mirror ops.** Export *extracts* images to the package; Replay *uploads* them to target — sequential pipeline steps, not symmetric halves. They are ALREADY unified where it matters: both delegate to the shared pure engine `IEmbeddedImageReferenceTool` (ADR-0026). Leave both service names (they're correctly `*Service` per the new glossary role); optionally drop `Export` from the export one → `EmbeddedImageExtractionService` if you want the phase word gone. |
| `ExportContext` (75) + `ImportContext` (77) | **Asymmetric by design — do NOT merge.** `ImportContext` is a strict subset (Job/Package/ProgressSink); `ExportContext` adds Organisations + TaskId + metrics/snapshot stores because export is source-facing with control-plane telemetry and import is target-facing without it. A forced `JobPhaseContext` base would leak export-only fields into import. Leave both. |
| `ExportServiceCollectionExtensions` + `ImportServiceCollectionExtensions` | **Orthogonal registrations** (source-facing clients/mappers vs target-facing targets/strategies) — no overlap. Merging gives one grab-bag class. Leave, or co-locate without merging. |
| `IExportProgressStore` family + `IImportCreatedNodeStateStore` | Different concerns/schemas (export = per-item revision progress in SQLite; import = created-node keys + JSON cursor). Not a pair. |

### RENAME to phase-neutral (component names, not phase artefacts)

> **Status:** ✅ Executed under ADR-0030 (2026-07-03) — the ProgressStore/Metrics/Progress/RevisionContext rows done as pure renames. The `IWorkItemExportOrchestrator` family row was left as-is (low-priority; subordinate revision-streaming seam — SKIPPED per the doc).

| Current | → | Why | Class |
|---|---|---|---|
| `IExportProgressStore` / `IExportProgressStoreFactory` / `SqliteExportProgressStore` / `ExportProgressStoreFactory` (12+ refs) | `IWorkItemProgressStore` / … | It stores per-work-item revision progress for resume — not conceptually export-only; drop the phase word. | **C** (Abstractions) |
| `IWorkItemExportMetrics` / `WorkItemExportMetrics` | `IWorkItemMetrics` / `WorkItemMetrics` | A metrics contract, not an export artefact. | **C** |
| `WorkItemExportProgress` | `WorkItemProgress` | A progress record. | **C** |
| `WorkItemRevisionExportContext` | `WorkItemRevisionContext` | Per-revision extension context; phase word is noise. | mechanical |
| `IWorkItemExportOrchestrator` (+Factory, +impls) | `IWorkItemRevisionExportDriver`? / or drop `Export` | Subordinate revision-streaming seam. If it truly only runs during export it may keep the word as a phase-scoped driver; **low priority** — confirm whether import reuses it before renaming. | **C** |

### LEAVE — legitimate phase artefact, mode option, or external framework term

| Type(s) | Why legitimate |
|---|---|
| `ImportReadinessReport`, `ImportReadiness*`, `ImportFailureFinding`, `ImportFailure`, `ImportFailureSeverity`, `ImportFailurePatternContext`, `IImportFailurePattern` | **Prepare-phase artefacts** — immutable DTOs describing *readiness to import* / *why import would fail*, produced by Prepare and consumed before Import. The word names the phase the artefact is *about*. Correct domain language, no export counterpart by design. |
| `IImportCreatedNodeStateStore`, `ImportWorkItemStateStore`, `ImportedWorkItemResult`, `ImportPreparer` | **Import-phase state/results** — record what import created (resume/idempotency); no export equivalent (export doesn't create target state, and there is no export "prepare"). Phase-scoped by nature. |
| `IWorkItemsImportCapabilityValidator` | Validates *target* capability before import; no export sibling. Borderline — low priority. |
| `BoardConfigImportMode` | Mode **enum** — `ImportMode` legitimately describes import behaviour (Replace/Merge/Skip). Already ruled legit. |
| `DiagnosticsFileMetricExporter`, `DiagnosticsFileTraceExporter`, `BaseExporter` | **OpenTelemetry framework term** — `BaseExporter<T>` is OTel's base class; "Exporter" = telemetry export, unrelated to the migration Export phase. **False positive.** |
| `SimulatedExportDataOptions` / `SimulatedExportGeneratorOptions` | Options for simulated *source-data generation*; the word scopes the simulated-source concern. Low priority. |

**Net §3 finding:** 1 real unification (U1 Teams), ~5 phase-neutral renames worth doing, and the large remainder are correct as-is. The Import/Export "problem" is much smaller than the raw grep suggested — the architecture is already mostly phase-symmetric.

---

## Recommended execution order (if you approve)

1. **Mechanical, no consent needed:** U1 (unify Teams orchestrators); §2 #8 `TfsAttachmentRegistry`→`Store`; the Infra-internal phase-neutral renames (`WorkItemRevisionExportContext`, the `*Mapper`→`*Processor` impls).
2. **One ADR + consent (Abstractions Class C):** §2 Abstractions renames (`IReferencedPathTracker`→`Lifecycle`, the `ClassificationTree*` family if `Source` is sanctioned, the `*Mapper` contracts), `IWorkerEventWriter`→`IWorkerEventSink`, `IJobPlanExecutor`→`IJobPlanOrchestrator`, and §3's phase-neutral Store/Metrics/Progress renames.
3. **Glossary follow-up (cheap, high leverage):** decide `Source` sanction (resolves the ClassificationTree family to mechanical), and Reader/Writer/Loader/Preparer sanction-vs-rename (drives whether §2's 14 "no action" items need touching at all).
4. **Do NOT do:** the `*Context` merge, the EmbeddedImage service merge, the DI-extension merge — all architecturally wrong per the analysis above.

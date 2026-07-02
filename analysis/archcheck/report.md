# Architecture Review — Combined Report

**Date:** 2026-07-02
**Scope:** Entire solution
**Perspectives checked:** Modular Monolith · Clean Architecture · Hexagonal · Vertical Slice · Screaming Architecture · Architecture Deepening · Module Compliance · Orchestrator Compliance · Extension/Connector Compliance · Tool Compliance

Status badges: **✅ [FIXED]** — auto-fix applied and verified (build + tests green). **🔄 [ESCALATED]** — auto-fix skipped/escalated during execution. **⏳ [PENDING OPERATOR]** — Class C change blocked pending operator consent. **📋 [BACKLOG]** — queued but not yet applied. **🔬 [DEEPENING ONLY]** — advisory / no change queued.

---

## ⚠️ OPERATOR ACTION REQUIRED

The following **29 findings** are **blocked** pending explicit operator consent (change-governance.md rule 3: "Class C: blocked unless operator consent + ADR + contract tests are all present"). No auto-fix was applied to any item below. This list is final as of the auto-fix + verify run on 2026-07-02; it now includes MC-M2, which was escalated during auto-fix execution.

### MM-C1 — Remove Infrastructure.TfsObjectModel → Infrastructure.Storage.FileSystem ProjectReference
- **File:** src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.csproj:46
- **Blocker:** Class C per change-classes.yaml: "Change adds, replaces, widens, narrows, or bypasses a canonical surface or blackbox contract." Removing the module-to-module reference and relocating storage-implementation selection narrows the module's dependency surface and changes which composition root owns the package-boundary contract wiring — a contract-level boundary change (also intertwined with MM-H1). change-governance.md rule 3: "Class C: blocked unless operator consent + ADR + contract tests are all present."
- **Required evidence:**
  - [ ] Explicit operator consent (consent-policy.yaml: block_if_missing: true)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Delete the ProjectReference; move the AddPackageBoundaryServices() call to the TfsMigrationAgent host composition root; depend only on Abstractions.Storage contracts.

### MM-H1 — Extract subprocess host composition root out of Infrastructure.TfsObjectModel
- **File:** src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/MigrationPlatformHost.cs:48
- **Blocker:** Relocating the DI host builder replaces the canonical composition entrypoint for the TFS subprocess — change-classes.yaml Class C: "adds, replaces ... or bypasses a canonical surface". A new/moved host entrypoint is a surface/contract change, and change-governance.md rejects any change that "labels a change as A/B while introducing contract-level surface change".
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Move the subprocess host builder (Serilog, OTel, storage selection) to the TfsMigrationAgent host or a dedicated host project; module exposes only AddTfsObjectModelModule.

### CA-C1 — Introduce IWorkerEventWriter port for UnifiedWorkerEventWriter
- **File:** src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:54
- **Blocker:** Adding a new port interface in Abstractions is a Class C change: change-classes.yaml C definition — "Change adds ... a canonical surface or blackbox contract." A new Abstractions interface is a new canonical surface.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Define IWorkerEventWriter in Abstractions(.Agent), implement in Infrastructure.Agent.Telemetry, inject the interface into both workers, register in composition roots.

### CA-C2 — Remove Infrastructure.Storage.FileSystem dependency from job workers
- **File:** src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:28
- **Blocker:** Eliminating the concrete-storage binding requires the workers to consume storage exclusively through the package-boundary contract, changing which surfaces the use-case ring is bound to — change-classes.yaml Class C: change that "narrows, or bypasses a canonical surface"; the current code bypasses Abstractions.Storage and the fix re-scopes the contract boundary. Coupled with MM-C1.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Depend only on Abstractions.Storage ports (IArtefactStore, IPackageAccess, factories); resolve FileSystem implementations only in the composition root.

### CA-H1 — Move ITfsJobServiceFactory into Abstractions.Agent
- **File:** src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/ITfsJobServiceFactory.cs:13
- **Blocker:** Moving an interface into the Abstractions layer adds/replaces a canonical surface — change-classes.yaml Class C: "adds, replaces ... a canonical surface or blackbox contract." (Same finding as HX-M1.)
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Relocate ITfsJobServiceFactory (and exposed domain types) to Abstractions.Agent; keep TfsJobServiceFactory as the infrastructure implementation; introduce boundary DTOs if TFS SDK types leak.

### HX-M1 — Move ITfsJobServiceFactory to Abstractions (duplicate of CA-H1)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/ITfsJobServiceFactory.cs:13
- **Blocker:** Duplicate of CA-H1 — same Class C rule applies: relocation into Abstractions adds a canonical surface.
- **Required evidence:**
  - [ ] Explicit operator consent (shared with CA-H1)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Same fix as CA-H1; execute once under a single consent grant.

### HX-H1 — Replace FileNotFoundException coupling in IPackageAccess.ResetMetaAsync with a storage-neutral contract
- **File:** src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:550, 561, 889, 893
- **Blocker:** Changing IPackageAccess's error contract (return type or exception type) widens/replaces the blackbox contract of the package-boundary canonical surface — change-classes.yaml Class C: "adds, replaces, widens, narrows ... a canonical surface or blackbox contract."
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (all IPackageAccess consumers and both storage adapters)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Change IPackageAccess.ResetMetaAsync to return a result or throw an abstraction-owned ArtefactNotFoundException; map in the FileSystem adapter; catch the abstraction exception in the worker.

### VS-H1 — Elevate WorkItemsPrepareRevisionReader static helper to an injected IWorkItemRevisionReader in Abstractions.Agent
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Revisions/WorkItemsPrepareRevisionReader.cs:16-19
- **Blocker:** Adds a new canonical interface in Abstractions.Agent — change-classes.yaml Class C: "Change adds ... a canonical surface or blackbox contract."
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Define IWorkItemRevisionReader in Abstractions.Agent and inject into the seven consumers.

### VS-H2 — Define IProjectInventoryReader/Writer contract replacing static ProjectInventoryFile
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/ProjectInventoryFile.cs:52, 95
- **Blocker:** New Abstractions contract governing the inventory file format — Class C per change-classes.yaml: adds a canonical surface/blackbox contract (also touches the package file format contract).
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (inventory file round-trip)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Define IProjectInventoryReader/Writer in Abstractions.Agent, single DI implementation, inject into the seven consuming slices.

### VS-H3 — Move KnownProcessIds (or IProcessIdResolver) into Abstractions
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/KnownProcessIds.cs:9, 25
- **Blocker:** Relocating a cross-connector shared type into Abstractions adds/replaces a canonical surface consumed by three connector projects — change-classes.yaml Class C definition applies.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests across all three connectors
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Relocate KnownProcessIds or introduce an IProcessIdResolver contract in Abstractions so connectors depend only on Abstractions.

### VS-M3 — Resolve WorkItemRevisionFolderParser cross-slice coupling (move to Abstractions or consume VS-H1 reader)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Revisions/WorkItemRevisionFolderParser.cs:19
- **Blocker:** Both fix options are contract-level: moving the package-format naming contract into Abstractions or depending on the new VS-H1 surface — change-classes.yaml Class C: "adds, replaces ... a canonical surface or blackbox contract." Also dependent on VS-H1's consent outcome.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change (package folder-naming contract)
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Either move the revision-folder naming contract into Abstractions.Agent or have ReferencedPathsFromWorkItemsStrategy consume the injected revision reader from VS-H1.

### MC-H1 — Implement the mandated module anatomy contract surface (IModuleContract, Selection/Data/Processing)
- **File:** src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs:23
- **Blocker:** Widens IModule and adds multiple new canonical contract interfaces plus a config-shape contract change — change-classes.yaml Class C: "adds, replaces, widens ... a canonical surface or blackbox contract." Affects all four IModule implementations and migration.schema.json.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (IModule implementations, config schema)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Define IModuleContract with ISelectionDefinition/IDataDefinition/IProcessingDefinition, expose IModule.Contract, restructure module options into the three aspects.

### MC-H2 — Migrate WorkItemsModuleOptions from legacy Scope/Extensions to Selection/Data/Processing
- **File:** src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsModuleOptions.cs:23-26
- **Blocker:** Changes the public configuration contract (migration.schema.json shape) — a blackbox contract replacement per change-classes.yaml Class C. Breaking for every existing user config file.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (schema round-trip, legacy-config migration/shim)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Restructure options to the three mandated aspects, regenerate migration.schema.json, optionally keep legacy keys behind a deprecation shim.

### MC-L1 — Implement real Teams/Nodes Prepare validation or set SupportsPrepare=false
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs:188-220 (also NodesModule.cs:177-211)
- **Blocker:** Either option changes contract-visible phase behavior: SupportsPrepare=false narrows the module's declared phase surface (task plans change), and adding real validation changes PrepareReport semantics consumers rely on — change-classes.yaml Class C: "narrows ... a canonical surface or blackbox contract." Operator must choose which path.
- **Required evidence:**
  - [ ] Explicit operator consent (choice of implement vs disable)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests for phase-support semantics
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Implement target validation in the prepare path via the orchestrator, or set SupportsPrepare = false until it exists.

### MC-L2 — Constrain ModuleDependency targets to IModule; re-express InventoryAnalyser ordering
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs:35
- **Blocker:** Constraining ModuleDependency's accepted targets narrows the IModule.DependsOn contract — change-classes.yaml Class C: "narrows ... a canonical surface."
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests for dependency ordering
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Express analyser ordering through the phase pipeline or a dedicated analyser-dependency mechanism; constrain ModuleDependency to IModule types.

### MC-I1 — Restore module-model.md to .agents/30-context/domains/ on main
- **File:** .agents/30-context/domains/module-model.md
- **Blocker:** Adding/restoring an authoritative contract-adjacent governance document changes the routing/authority surface agents operate under; change-governance.md rejects changes that update contracts "without synchronized ADR and tests", and which document version is authoritative is an operator decision (fail-closed).
- **Required evidence:**
  - [ ] Explicit operator consent on which document version is authoritative
  - [ ] ADR or linked record of the restoration
  - [ ] Verification that routing-catalog/reading-order references resolve
- **Fix pending consent:** Merge or restore module-model.md so the documented routing/reading order resolves.

### OC-I1 — Author or restore the Orchestrator Model authority documents (audit blocked, fail-closed)
- **File:** .agents/10-contracts/specs/orchestrator-contract.md (and .agents/30-context/domains/orchestrator-model.md)
- **Blocker:** Creating a new authoritative contract document adds a canonical contract (change-classes.yaml Class C: "adds ... a blackbox contract"), and the audit itself is fail-closed per the routing contract: "If no activity matches, stop and ask the operator." The referenced documents do not exist; their absence blocks any auto-fix and blocks the entire OC perspective audit.
- **Required evidence:**
  - [ ] Explicit operator consent and authorship of the contract content
  - [ ] ADR recording the new/restored contract
  - [ ] Corrected workflow references
  - [ ] Re-run of the OC audit against the restored documents
- **Fix pending consent:** Author/restore orchestrator-model.md and orchestrator-contract.md, or correct the paths in .agents/workflows/nkda-archcheck-workflow.js:189, then re-run the OC audit.

### EC-H1 — Introduce ConnectorCapability flags for team/comment extensions and remove nullable gating
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs:39-58
- **Blocker:** Adding new capability flags widens the IConnectorCapabilityProvider capability contract that every connector must explicitly register against — change-classes.yaml Class C: "widens ... a canonical surface or blackbox contract."
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update defining the new capability flags
  - [ ] Contract compatibility tests across Simulated/AzureDevOps/TFS connectors
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Add capability flags (TeamSettings, TeamMembers, Comments, etc.) registered per connector via IConnectorCapabilityProvider; gate with _capProvider.Has(Cap.X); make dependencies non-nullable.

### EC-M2 — Implement mandatory pagination for ADO board/backlog/identity list operations
- **File:** src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs:48-93, 131-162
- **Blocker:** Where the endpoint is genuinely unpaged, the fix requires documenting an exemption to the connector-model.md mandatory-pagination rule — a guardrail-challenge outcome. consent-policy.yaml requires operator consent for "Any explicit guardrail challenge outcome". The paging-implementation portion alone would be Class B, but the exemption decision blocks it.
- **Required evidence:**
  - [ ] Explicit operator consent for any pagination-rule exemption
  - [ ] Documented exemption at each unpaged call site
  - [ ] Behavioral tests for paged enumeration
- **Fix pending consent:** Implement continuation-token paging where the REST API supports it; document exemptions at call sites where it does not.

### EC-M3 — Fold TeamSettingsTeamExtension into the core Teams pipeline (invalid extension seam)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs:24
- **Blocker:** Removing an IModuleExtension seam (or recording a Guardrail Challenge exception) replaces/removes a canonical extension surface and package-content shape; consent-policy.yaml requires operator consent for "Any explicit guardrail challenge outcome" and change-classes.yaml Class C covers replacing a canonical surface.
- **Required evidence:**
  - [ ] Explicit operator consent (fold vs Guardrail Challenge exception)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (team package content before/after)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Fold team-settings export/import into the core Teams pipeline governed by module options, or record an explicit Guardrail Challenge Protocol exception.

### EC-M4 — Extract board-config merge/validation engine into a canonical seam
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/BoardConfigTeamExtension.cs:389-470
- **Blocker:** Creating a new canonical seam (a Tool contract in the abstraction layer) adds a canonical surface — change-classes.yaml Class C: "Change adds ... a canonical surface or blackbox contract."
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Extract BuildValidStatesMap/FilterInvalidStateMappings/MergeByName into a canonical board-config merge/validation Tool with its own contract; extension keeps policy only.

### EC-L1 — Fix ITeamTarget endpoint parameter forged with null! by extensions
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs (SetTeamSettingsAsync/SetAreaPathsAsync/AddMemberAsync call sites)
- **Blocker:** The 'remove parameter' option narrows the ITeamTarget seam contract — change-classes.yaml Class C: "narrows ... a canonical surface." The alternative (resolve the real endpoint) may be Class B, but choosing between them changes the seam contract decision and needs the operator.
- **Required evidence:**
  - [ ] Explicit operator consent (contract change vs endpoint resolution)
  - [ ] ADR add/update if the ITeamTarget signature changes
  - [ ] Contract compatibility tests across connectors
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Either remove the endpoint parameter from the ITeamTarget contract or resolve and pass the real target endpoint.

### TC-H1 — Refactor or rename AttachmentReplayTool (Tool contract violation)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Attachments/AttachmentReplayTool.cs:20, 83-88
- **Blocker:** Both remediation paths are contract-level: promoting it creates a new Abstractions.Agent interface (Class C "adds a canonical surface"); the rename path re-scopes what counts as a Tool under execution-model.md — a contract taxonomy decision. Operator must choose the direction.
- **Required evidence:**
  - [ ] Explicit operator consent (rename vs promote)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests if a seam is added
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Either rename to AttachmentReplayService/Handler, or promote to a genuine canonical seam with an Abstractions.Agent interface and DI singleton registration.

### TC-H2 — Split or rename EmbeddedImageRewriteTool (Tool contract violation)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Attachments/EmbeddedImageRewriteTool.cs:20, 57-64
- **Blocker:** Same as TC-H1: the split option adds a new canonical Tool seam and moves upload orchestration across the Extension/Tool contract boundary — change-classes.yaml Class C. Direction choice needs the operator.
- **Required evidence:**
  - [ ] Explicit operator consent (rename vs split)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Rename to Service/Handler, or split: pure URL-rewrite stays a Tool, upload orchestration moves to the extension layer.

### TC-M1 — Purify IdentityTranslationTool (state, package I/O, orchestrator inversion)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityTranslation/IdentityTranslationTool.cs:43, 47-49, 73-156, 175, 190-210
- **Blocker:** Changing the tool's InitializeAsync/Translate surface semantics (what it accepts, what it persists) alters the canonical Tool seam contract — change-classes.yaml Class C: "replaces, widens, narrows ... a canonical surface or blackbox contract" — and removes the WriteUnresolvedAsync unresolved.json package output, a package-content contract change.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (unresolved.json production path preserved)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Move package read/write and the orchestrator fallback into the Identities orchestrator/extension; pass resolved maps into the tool as data.

### TC-M2 — Resolve IFieldTransformTool AddScoped vs singleton Tool contract conflict
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformToolServiceCollectionExtensions.cs:52-67
- **Blocker:** field-transform-contract.md Governance says such surface-semantics changes are "at least Class B", but resolving the conflict requires either changing lifetime semantics of a canonical Tool surface or amending an authoritative contract document — the latter is unambiguously Class C ("updates contracts without synchronized ADR and tests" is a reject condition), and the operator must pick which document/behavior wins.
- **Required evidence:**
  - [ ] Explicit operator consent (registration change vs contract amendment)
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (per-job options behavior)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Either change registration to singleton with config-accessor indirection, or amend the contract/doc via change governance.

### TC-L3 — Extract single IEmbeddedImageReferenceTool for export+import image-reference parsing
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/EmbeddedImageExportService.cs:60-90
- **Blocker:** Adds a new canonical Tool interface in Abstractions.Agent — change-classes.yaml Class C: "Change adds ... a canonical surface." Also changes parser behavior shared across export/import regimes, affecting package/rewrite output.
- **Required evidence:**
  - [ ] Explicit operator consent
  - [ ] ADR add/update in same change
  - [ ] Contract compatibility tests (export/import parity on reference detection)
  - [ ] Test-first trace showing RED -> GREEN -> REFACTOR
- **Fix pending consent:** Extract a pure IEmbeddedImageReferenceTool (parse + rewrite) in Abstractions.Agent/Tools consumed by both phases.

### MM-I1 — Watch-item: single-module contracts in modules (no action now; any move to Abstractions is Class C)
- **File:** src/DevOpsMigrationPlatform.ControlPlane/Jobs/IJobStore.cs
- **Blocker:** Currently compliant — no change is being made. Listed here only because the sole possible action (relocating interfaces to Abstractions) would add canonical surfaces per change-classes.yaml Class C; it must not be auto-applied.
- **Required evidence:**
  - [ ] Explicit operator consent at the time any relocation is proposed
  - [ ] ADR add/update
  - [ ] Contract compatibility tests
- **Fix pending consent:** No action required now. If cross-module adoption occurs, moving IJobStore/ILeaseJobResolver/ITfsJobServiceFactory/IAzureDevOpsClientFactory to Abstractions is a Class C change requiring the full consent gate.

### MC-M2 — Delegate TeamsModule Capture/Prepare orchestration to ITeamsOrchestrator (escalated during auto-fix)
- **File:** src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs:116-224
- **Blocker:** Escalated during auto-fix execution: the fix is already applied in the working tree. TeamsModule.CaptureAsync (line 116-117) is a one-line delegation to ITeamsOrchestrator.CaptureAsync, and PrepareAsync (lines 119-136) delegates report generation/persistence to ITeamsOrchestrator.PrepareAsync. ITeamsOrchestrator (src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/ITeamsOrchestrator.cs) already declares both methods with doc comments stating the orchestrator owns enumeration/report persistence and the module is a thin facade, and TeamsOrchestrator.cs implements both (lines 112, 184). No code change needed — operator confirmation requested to close the finding.
- **Required evidence:**
  - [ ] Explicit operator consent (confirm the finding is satisfied by the existing code and close it)
  - [ ] ADR add/update (only if the operator disputes the current shape)
  - [ ] Contract compatibility tests
- **Fix pending consent:** None — verify the existing delegation matches module-model.md and mark the finding closed.

---

## Summary Table

| Perspective | Critical | High | Medium | Low | Informational | Auto-fix Applied | Needs Operator | Status |
|---|---|---|---|---|---|---|---|---|
| Modular Monolith [MM] | 1 | 1 | 2 | 1 | 1 | 3 | 3 | ✅ 3 fixed · ⏳ 3 pending operator |
| Clean Architecture [CA] | 2 | 1 | 3 | 1 | 1 | 4 | 3 | ✅ 4 fixed · ⏳ 3 pending operator · 🔬 1 deepening |
| Hexagonal [HX] | 0 | 1 | 1 | 3 | 2 | 3 | 2 | ✅ 3 fixed · ⏳ 2 pending operator · 🔬 2 deepening |
| Vertical Slice [VS] | 0 | 3 | 3 | 1 | 1 | 4 | 4 | ✅ 4 fixed · ⏳ 4 pending operator |
| Screaming Architecture [SA] | 0 | 0 | 1 | 2 | 2 | 4 | 0 | ✅ 4 fixed |
| Architecture Deepening [DC] | 0 | 4 | 5 | 3 | 0 | 0 | 0 | 🔬 12 deepening (advisory backlog) |
| Module Compliance [MC] | 0 | 2 | 3 | 3 | 1 | 3 | 6 | ✅ 3 fixed · 🔄 1 escalated · ⏳ 5 pending operator |
| Orchestrator Compliance [OC] | 0 | 0 | 0 | 0 | 1 | 0 | 1 | ⏳ 1 pending operator (audit fail-closed) |
| Extension/Connector Compliance [EC] | 0 | 2 | 5 | 1 | 2 | 3 | 5 | ✅ 3 fixed · ⏳ 5 pending operator · 🔬 2 deepening |
| Tool Compliance [TC] | 0 | 2 | 2 | 3 | 2 | 2 | 5 | ✅ 2 fixed · ⏳ 5 pending operator · 🔬 2 deepening |
| **Total** | **3** | **16** | **25** | **18** | **13** | **26** | **29** | **✅ 26 fixed · 🔄 1 escalated · ⏳ 29 pending · 🔬 19 deepening** |

Status legend: ✅ Fixed | 🔄 Escalated | ⏳ Pending operator | 📋 Backlog | 🔬 Deepening

Note: findings triaged as deepening-only (all DC findings plus CA-I1, HX-L4, HX-L5, EC-I1, EC-I2, TC-I1, TC-I2) carry no queued fix and are counted in neither triage column. SA-I2 is an audit-scope note with no queued action. MC-M2 was originally auto-fix queued and was escalated to the operator during execution (fix already present in the working tree), which moves one item from the Auto-fix column to Needs Operator relative to the original triage.

---

## Build and Test Results (auto-fix verify phase, 2026-07-02)

| Gate | Result |
|---|---|
| Build | ✅ Passed |
| Tests | ✅ Passed |
| Build errors | None |
| Test failures | None |
| Fixes reverted | None |

All 26 applied auto-fixes were verified together with a full solution build and test run. One queued fix (MC-M2) was skipped/escalated because the change was already present in the working tree. No fixes failed and none were reverted. All applied changes are uncommitted on branch `update-for-comms`.

---

## Critical Violations (must fix before merge)

```
[MM-C1] ⏳ [PENDING OPERATOR] Module-to-module ProjectReference: Infrastructure.TfsObjectModel references
        Infrastructure.Storage.FileSystem, bypassing Abstractions.Storage
  File: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.csproj:46
  Fix:  Remove the ProjectReference to DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem. TfsObjectModel should
        depend only on the IArtefactStore/IStateStore/package contracts in DevOpsMigrationPlatform.Abstractions.Storage;
        the host (TfsMigrationAgent) should wire the FileSystem implementation by calling AddPackageBoundaryServices()
        itself. The only usage is MigrationPlatformHost.cs line 145 calling services.AddPackageBoundaryServices() —
        move that call to the host composition root.

[CA-C1] ⏳ [PENDING OPERATOR] Use-case job handlers depend on concrete UnifiedWorkerEventWriter instead of an abstraction
  File: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:27,54,75;
        src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs:31,56,81
  Fix:  Define a port (e.g. IWorkerEventWriter) in DevOpsMigrationPlatform.Abstractions (or Abstractions.Agent), have
        UnifiedWorkerEventWriter in Infrastructure.Agent.Telemetry implement it, and inject the interface into
        JobAgentWorker/TfsJobAgentWorker. Register the mapping in the composition roots.

[CA-C2] ⏳ [PENDING OPERATOR] Job handlers import DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem, binding the
        use-case ring to a specific storage technology
  File: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:28;
        src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs:32
  Fix:  Remove the Infrastructure.Storage.FileSystem using from both workers and depend only on the storage ports
        already defined in DevOpsMigrationPlatform.Abstractions.Storage (IArtefactStore, IPackageAccess, package store
        factory abstractions). Resolve the FileSystem implementations exclusively in the DI composition root; this also
        keeps the future Azure Blob store (noted at JobAgentWorker.cs:641) swappable.
```

## High Violations (fix in current sprint)

```
[MM-H1] ⏳ [PENDING OPERATOR] Host composition root embedded inside a module: MigrationPlatformHost builds the full DI host
        (Serilog, OpenTelemetry, Azure Monitor exporters, concrete storage) inside Infrastructure.TfsObjectModel
  File: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/MigrationPlatformHost.cs:48
  Fix:  Extract the subprocess host builder into the TfsMigrationAgent host project (or a dedicated host project).
        Leave the module exposing only an AddTfsObjectModelModule(IServiceCollection) extension for its own services;
        telemetry, logging sinks, and storage implementation selection belong to the composition root. This also
        removes the root cause of MM-C1.

[CA-H1] ⏳ [PENDING OPERATOR] Port interface ITfsJobServiceFactory declared in infrastructure project but consumed
        cross-project by TfsMigrationAgent
  File: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/ITfsJobServiceFactory.cs:13
  Fix:  Move ITfsJobServiceFactory (and any domain types its signature exposes) to DevOpsMigrationPlatform.Abstractions.Agent
        so TfsJobAgentWorker and TfsMigrationAgentServiceExtensions depend only on the inner ring; keep TfsJobServiceFactory
        as the Infrastructure.TfsObjectModel implementation. If its signature currently leaks TFS SDK types, introduce
        domain DTOs at the boundary first (Check 4/5).

[HX-H1] ⏳ [PENDING OPERATOR] JobAgentWorker catches System.IO.FileNotFoundException from IPackageAccess.ResetMetaAsync,
        coupling module code to filesystem store semantics (breaks blob-store substitutability)
  File: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:550, 561, 889, 893
  Fix:  Define a storage-neutral contract: have IPackageAccess.ResetMetaAsync return a result (e.g. bool/ResetOutcome)
        or throw an abstraction-owned ArtefactNotFoundException; map FileNotFoundException to it inside the FileSystem
        infrastructure adapter and catch the abstraction exception in the worker.

[VS-H1] ⏳ [PENDING OPERATOR] WorkItemsPrepareRevisionReader static helper shares revision-enumeration business logic across
        Prepare, Import-failure, Node-validation, and WorkItemType-validation slices
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Revisions/WorkItemsPrepareRevisionReader.cs:16-19
  Fix:  Elevate revision enumeration to an injected service (e.g. IWorkItemRevisionReader in Abstractions.Agent) and
        inject it into each consumer (MissingAttachmentBinaryImportFailurePattern, MissingEmbeddedImageBinaryImportFailurePattern,
        NodePathValidator, FieldTransformCompatibilityImportFailurePattern, InvalidRevisionPayloadImportFailurePattern,
        MissingRevisionArtefactImportFailurePattern, WorkItemTypeValidator), removing the static coupling between slices.

[VS-H2] ⏳ [PENDING OPERATOR] ProjectInventoryFile static MergeAsync/ReadAsync is shared business logic across
        Inventory/Discovery, Analysis, Identities, Nodes, Teams, WorkItems, and JobPlanExecutor slices
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/ProjectInventoryFile.cs:52, 95
  Fix:  Define IProjectInventoryReader/Writer in Abstractions.Agent and register a single implementation in DI; inject
        into InventoryOrchestrator, InventoryAnalyser, JobPlanExecutor, IdentitiesModule, NodesModule, TeamsModule, and
        WorkItemsOrchestrator so inventory file format changes are made behind a contract instead of a static shared by
        seven slices.

[VS-H3] ⏳ [PENDING OPERATOR] KnownProcessIds static class in Infrastructure.Agent is referenced by three connector projects
        (AzureDevOps, Simulated, TfsObjectModel), coupling connectors to another module's internals
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/KnownProcessIds.cs:9, 25
  Fix:  Move KnownProcessIds (or an IProcessIdResolver contract) into DevOpsMigrationPlatform.Abstractions so connector
        projects depend only on Abstractions, restoring the rule that cross-slice sharing flows through Abstractions.

[DC-H1] 🔬 [DEEPENING ONLY] QueueCommand is a 2,891-line shallow god-module mixing queueing, SSE streaming, TUI progress
        state machines, and inventory rendering
  File: src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs:1-2891
  Fix:  Deepen by extraction: pull the JobProgressState/JobProgressUpdate reducer (lines ~1383-1435) and
        DiscoveryProgressState into a deep 'job progress projection' module with a small interface (Apply(event) -> state),
        and move stream-consumption into a reusable module shared with LogsCommand/TuiCommand. The reducer becomes
        directly testable through its interface; the command shrinks to wiring.

[DC-H2] 🔬 [DEEPENING ONLY] Each module orchestrator (Nodes, Teams, Identities, Dependency, WorkItems, Inventory — ~5,900
        lines combined) re-implements the same run harness: checkpointing loop, progress events, metrics, ActivitySource,
        JsonSerializerOptions, NET481 forks
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ (NodesOrchestrator.cs, TeamsOrchestrator.cs,
        IdentitiesOrchestrator.cs, DependencyOrchestrator.cs et al.)
  Fix:  ADR-0012 defines the IModule five-phase contract but no deep shared harness exists behind it. Create one
        'module run harness' module that owns checkpoint/resume, progress emission, metrics, and tracing; orchestrators
        supply only per-module capture/replicate logic.

[DC-H3] 🔬 [DEEPENING ONLY] #if !NET481 / #if NET481 preprocessor forks in 28 files (including constructor signatures)
        act as compile-time seams instead of adapters
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs (constructor; 63+ occurrences
        solution-wide)
  Fix:  Replace intra-file forks with adapters at real seams: net481-only behaviour lives in the
        TfsObjectModel/TfsMigrationAgent projects satisfying the same interface (per ADR-0007 compiler-enforced
        project boundaries).

[DC-H4] 🔬 [DEEPENING ONLY] Nullable optional dependencies (IPackageAccess?, IPlatformMetrics?, IProgressSink?,
        ICurrentJobEndpointAccessor?) injected across 32 files hide invariants from the interface
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs (constructor ~line 52) and 31 other files
  Fix:  Inject non-null no-op adapters (NullProgressSink, NullPlatformMetrics) registered in DI instead. Interfaces
        shrink to their type signatures, null-branch bugs gain locality in one adapter, and tests stop needing
        null-permutation cases.

[MC-H1] ⏳ [PENDING OPERATOR] Module anatomy contract surface is entirely unimplemented — no IModule.Contract, IModuleContract,
        ISelectionDefinition, IDataDefinition, or IProcessingDefinition exists anywhere in src/, and no module config
        exposes the mandated aspects. Affects all four IModule implementations (WorkItemsModule, TeamsModule,
        NodesModule, IdentitiesModule) and the IModule interface itself.
  File: src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs:23
  Rule: module-anatomy-contract.md Contract Surface list and Required Semantics rule 1: "Module config uses exactly
        three top-level aspects: `Selection`, `Data`, `Processing`"
  Fix:  Add the contract surface: define IModuleContract with ISelectionDefinition/IDataDefinition/IProcessingDefinition,
        expose it as IModule.Contract, and restructure module options around Selection/Data/Processing aspects.

[MC-H2] ⏳ [PENDING OPERATOR] WorkItemsModuleOptions uses `Scope` and `Extensions` as top-level config aspects; the legacy
        shape is also baked into migration.schema.json (WorkItemsScopeOptions, WorkItemsExtensionsOptions)
  File: src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsModuleOptions.cs:23-26
  Rule: module-anatomy-contract.md rule 2: "`Scope` and `Extensions` are legacy and must not be used for new module
        designs" and rule 1: "Module config uses exactly three top-level aspects: `Selection`, `Data`, `Processing`"
  Fix:  Migrate WorkItemsModuleOptions to Selection/Data/Processing aspects (Scope → Selection; Extensions split into
        Data and Processing entries) and regenerate migration.schema.json; keep legacy keys only behind an explicit
        deprecation shim if backwards compatibility is required.

[EC-H1] ⏳ [PENDING OPERATOR] Extensions gate on nullable optional dependencies instead of the ConnectorCapability mechanism
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs:39-58 (also
        TeamMembersTeamExtension.cs, TeamAreaPathsTeamExtension.cs, TeamIterationsTeamExtension.cs,
        TeamCapacityTeamExtension.cs, WorkItems/Extensions/CommentsWorkItemExtension.cs)
  Rule: connector-model.md: "Each connector registers its supported capabilities via IConnectorCapabilityProvider.
        Extensions gate their work behind _capProvider.Has(Cap.X) — no null guards, no try/catch for capability detection."
  Fix:  Six of the seven IModuleExtension implementations inject nullable ITeamSource?/ITeamTarget?/
        IWorkItemCommentSourceFactory? and gate SupportsExport/SupportsImport on null checks (e.g. `if (_teamSource is
        null) ... return;`). Only BoardConfigTeamExtension uses IConnectorCapabilityProvider. Introduce capability
        flags (TeamSettings, TeamMembers, Comments) registered explicitly per connector and gate with
        _capProvider.Has(Cap.X); make dependencies non-nullable.

[EC-H2] ✅ [FIXED] TfsNullBoardAdapter models reduced TFS capability as a throwing stub, not in the contract result
  File: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Teams/TfsNullBoardAdapter.cs:19-75
  Rule: connector-model.md models the TFS reduced-capability pattern as: "reduced capability: returns empty + structured
        Warning ... Modeled explicitly in the contract result, not a stub." (cf. TfsIdentityAdapter: "It MUST NOT throw")
  Fix:  TfsNullBoardAdapter throws NotSupportedException on every member, relying on a distant capability check to
        never call it — the opposite of the TfsIdentityAdapter pattern. Rework to return empty sequences/no-op results
        with a structured Warning log, mirroring TfsIdentityAdapter, so an ordering bug between capability gate and
        adapter call cannot crash a migration.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Teams/TfsNullBoardAdapter.cs

[TC-H1] ✅ [FIXED] AttachmentReplayTool is a Tool in name only: performs network I/O, persists state, and is
        constructed ad-hoc per consumer
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Attachments/AttachmentReplayTool.cs:20, 83-88
        (consumer: WorkItems/Revisions/WorkItemResolutionProcessor.cs:287)
  Rule: execution-model.md 'Layer: Tool': "Pure stateless transformation / lookup logic — No I/O, no network calls,
        no filesystem access" and "A Tool is a singleton with one central config for the entire run"; capability-ethos
        rule 7: "Contracts belong in abstraction layers"; ethos rule 6: "Boundary naming must scream intent"
  Fix:  It uploads via IWorkItemTarget and writes mappings via IIdMapStore, has no I*Tool interface in
        Abstractions.Agent/Tools/, and is new'd inside WorkItemResolutionProcessor rather than DI-registered. Either
        rename it (e.g. AttachmentReplayService/Handler), or refactor it into a genuine canonical seam with an
        Abstractions.Agent interface and DI registration.
  Status: ✅ Fixed — operator ruled RENAME (Tool taxonomy settled: Tools are pure engines; replay/I-O units are services).
        Renamed AttachmentReplayTool -> AttachmentReplayService (same location, mirroring EmbeddedImageReplayService);
        ADR-0026 amended; convention pinned by Architecture/ToolTaxonomyArchitectureTests.
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-03)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Attachments/AttachmentReplayService.cs (renamed),
        src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Revisions/WorkItemResolutionProcessor.cs,
        tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/AttachmentReplayServiceTests.cs (renamed),
        tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Architecture/ToolTaxonomyArchitectureTests.cs (new),
        docs/adr/0026-tool-contract-purification.md

[TC-H2] ⏳ [PENDING OPERATOR] EmbeddedImageRewriteTool performs network uploads and package reads while carrying the
        canonical Tool name
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Attachments/EmbeddedImageRewriteTool.cs:20, 57-64
        (consumer: WorkItems/Revisions/WorkItemResolutionProcessor.cs:102)
  Rule: execution-model.md Layer: Tool — "Owns: Pure stateless transformation / lookup logic — No I/O, no network calls,
        no filesystem access"; Logic seam: "Extension owns: orchestrating the transformation; Tool owns: the pure
        transformation itself"
  Fix:  It reads binaries via a delegate and uploads via IWorkItemTarget.UploadEmbeddedImageAsync. No interface in
        Abstractions.Agent/Tools/, lives in WorkItems/Attachments/ not Tools/, and is instantiated inline with a
        NullLogger fallback. Rename to a Service/Handler, or split: keep the pure URL-rewrite string transformation as
        a real Tool and move the upload orchestration into the extension layer.
```

## Medium Violations (fix in next sprint)

```
[MM-M1] ✅ [FIXED] Infrastructure.TfsObjectModel exposes no self-contained Add<Module> registration entry point;
        ~30 registrations of its internal types (TfsClassificationTreeCapture, TfsActiveJobIdentitySource,
        TfsActiveJob* factories, etc.) live in the TfsMigrationAgent host
  File: src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs:70-160
  Fix:  Create a ServiceCollectionExtensions class in DevOpsMigrationPlatform.Infrastructure.TfsObjectModel exposing a
        single AddTfsObjectModelModule(this IServiceCollection, IConfiguration) method that encapsulates all
        module-internal registrations; have TfsMigrationAgentServiceExtensions call it.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/TfsObjectModelModuleServiceExtensions.cs; src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs

[MM-M2] ✅ [FIXED] CLI.Migration host scatters module-internal DI registrations across individual command classes
        (ConfigurationService, InteractiveConfigurationBuilder, JsonSchemaConfigValidator, ControlPlaneClient adapters)
  File: src/DevOpsMigrationPlatform.CLI.Migration/Commands/ConfigNewCommand.cs:28-30 (also ConfigureCommand.cs,
        PrepareCommand.cs, QueueCommand.cs:86-95, LogsCommand.cs:49-50, Discovery/DependencyCommand.cs:47,137,151,
        TuiCommand.cs:45)
  Fix:  Move these registrations into the owning modules' ServiceCollectionExtensions (e.g. AddConfigurationServices,
        AddControlPlaneClient, AddConfigSchemaValidation) and have each command call the single module entry point.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.CLI.Migration/Configuration/ConfigurationWizardServiceCollectionExtensions.cs; src/DevOpsMigrationPlatform.CLI.Migration/Commands/ConfigNewCommand.cs; src/DevOpsMigrationPlatform.CLI.Migration/Commands/ConfigureCommand.cs

[CA-M1] ✅ [FIXED] Business configuration logic embedded in JobAgentWorker (Simulated generator propagation,
        DiscoveryConfigWrapper deserialization/mutation)
  File: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs:641-915
  Fix:  Extract the config-normalisation rules (propagating Source.Generator into SimulatedOrganisationEntry, wrapper
        deserialization, probe payload construction) into a dedicated use-case service (e.g. a JobConfigResolver behind
        an abstraction) so the worker only orchestrates. Add unit tests for the extracted rules.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs; src/DevOpsMigrationPlatform.MigrationAgent/JobConfigResolver.cs; tests/DevOpsMigrationPlatform.MigrationAgent.Tests/JobConfigResolverTests.cs

[CA-M2] ✅ [FIXED] Business logic in CLI QueueCommand: phase resolution, task aggregation, blocking-findings
        evaluation, duration statistics inside a 2,891-line command
  File: src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs:384, 1644-1948, 2269-2820
  Fix:  Move phase/task derivation (ResolveTaskPhase, ShouldRenderTaskInPhase, terminal/pending phase computation,
        elapsed-time statistics) and blocking-findings evaluation into a use-case-layer read model or task-summary
        service in Abstractions.Agent/Infrastructure.Agent; the CLI command should only render the returned view model.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.CLI.Migration/Services/JobTaskSummaryService.cs; src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs; tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs

[CA-M3] ✅ [FIXED] Organisation filtering rule (Enabled) applied in CLI DependencyCommand instead of the
        use-case layer
  File: src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs:165
  Fix:  Push the enabled-organisation filter into the dependency-discovery orchestrator (or a config projection method
        on the domain config model) so all entry points apply the same rule; the command iterates an already-filtered
        collection.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions/Options/MigrationPlatformOptions.cs; src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs

[HX-M1] ⏳ [PENDING OPERATOR] ITfsJobServiceFactory is defined in Infrastructure.TfsObjectModel but consumed by the
        TfsMigrationAgent worker project — shared interface outside Abstractions (duplicate of CA-H1)
  File: src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/ITfsJobServiceFactory.cs:13
  Fix:  Move ITfsJobServiceFactory (and any DTOs it exposes) into DevOpsMigrationPlatform.Abstractions.Agent (or a
        TFS-facing Abstractions namespace); keep TfsJobServiceFactory as the Infrastructure implementation registered in DI.

[VS-M1] ✅ [FIXED] Work-items import slice has no CLI-level end-to-end SystemTest — only adapter-level import
        tests (SimulatedBoardAdapterImportTests) exist
  File: tests/DevOpsMigrationPlatform.CLI.Migration.Tests
  Fix:  Add a [TestCategory("SystemTest")] scenario in DevOpsMigrationPlatform.CLI.Migration.Tests that queues an
        import job from a prepared package and asserts on target-side observable output (resolution state, imported
        revisions), mirroring the existing TfsExport system tests.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SystemTests/WorkItemsImportSliceSystemTests.cs

[VS-M2] ✅ [FIXED] config (new/set/get), controlplane start, and agent start command slices have no SystemTest
        asserting observable end-to-end output
  File: src/DevOpsMigrationPlatform.CLI.Migration/Program.cs:134-174
  Fix:  Add [TestCategory("SystemTest")] tests invoking each command family via the CLI entry point and asserting on
        observable output (written config file, started host health endpoint), so each slice's full path is validated.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SystemTests/CommandSliceStartupSystemTests.cs

[VS-M3] ⏳ [PENDING OPERATOR] WorkItemRevisionFolderParser static is consumed by both the Revisions pipeline and the Nodes
        ReferencedPathsFromWorkItemsStrategy, creating hidden coupling between the WorkItems and Nodes slices
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Revisions/WorkItemRevisionFolderParser.cs:19
  Fix:  Either move the revision-folder naming contract into Abstractions.Agent (it is a package-format concern shared
        by design) or have ReferencedPathsFromWorkItemsStrategy consume the injected revision reader from VS-H1 instead
        of parsing folder names directly.

[SA-M1] ✅ [FIXED] Generic 'Helper' class name: EndpointSlugHelper (Check 1)
  File: src/DevOpsMigrationPlatform.Abstractions.Agent/Context/ISourceEndpointInfo.cs:48
  Fix:  Rename to a domain-revealing name such as EndpointSlug (with static Extract/FromUrl) or
        OrganisationEndpointSlug; also move it out of the ISourceEndpointInfo.cs file into its own file so the type is
        discoverable by name.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions.Agent/Context/OrganisationEndpointSlug.cs; src/DevOpsMigrationPlatform.Abstractions.Agent/Context/ISourceEndpointInfo.cs; src/DevOpsMigrationPlatform.Abstractions.Agent/Context/ITargetEndpointInfo.cs; src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs; src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/TfsObjectModelModuleServiceExtensions.cs; src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/TfsJobServiceFactory.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/ActiveJobEndpointInfo.cs

[DC-M1] 🔬 [DEEPENING ONLY] JobAgentWorker (1,127 lines) and TfsJobAgentWorker (533 lines) duplicate job-kind dispatch,
        discovery-job handling, and endpoint-context setup across two host projects
  File: src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs (OnJobAsync/OnDiscoveryJobAsync/
        SetCurrentEndpointContext vs TfsJobAgentWorker.cs equivalents)
  Fix:  Both derive from ModulePipelineWorkerBase yet each re-implements OnDiscoveryJobAsync and endpoint-context
        plumbing. Deepen ModulePipelineWorkerBase to own job-kind dispatch (ADR-0009 single job kind discriminator) and
        endpoint-context establishment, leaving the workers as thin connector-specific adapters.

[DC-M2] 🔬 [DEEPENING ONLY] Abstractions.Agent: 94 interfaces across 213 files averaging ~30 lines each — many are
        single-adapter hypothetical seams (INodesOrchestrator, ITeamsOrchestrator, IWorkItemExportOrchestratorFactory,
        etc. each with exactly one implementation)
  File: src/DevOpsMigrationPlatform.Abstractions.Agent/ (project-wide)
  Fix:  Audit each interface against 'one adapter = hypothetical seam, two adapters = real seam'; collapse category-1
        in-process hypothetical seams; keep only seams where Simulated/AzureDevOps/TFS adapters genuinely vary.

[DC-M3] 🔬 [DEEPENING ONLY] Infrastructure.Storage.AzureBlob project contains zero implementation code — an empty adapter
        project holding a seam open with nothing behind it
  File: src/DevOpsMigrationPlatform.Infrastructure.Storage.AzureBlob/ (whole project)
  Fix:  Either implement the blob adapter against IPackageAccess/Abstractions.Storage or delete the project until
        needed; if deliberately deferred, record it in an ADR.

[DC-M4] 🔬 [DEEPENING ONLY] Job plan module split leaks: JobExecutionPlanBuilder (1,432) and JobPlanExecutor (1,482) plus
        JobAgentWorker each define private MigrationEndpointOptions/EndpointInfo wrapper classes for the same concept
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs:1229-1272
        (ConfigOrganisationEndpointOptions, ConfigSource/TargetEndpointInfo) vs JobAgentWorker.cs:1122
        (InlineMigrationEndpointOptions)
  Fix:  Extract one deep 'endpoint options from package config' module with a single interface (ADR-0008: configuration
        travels in the package); builder, executor, and workers become callers. Concentrate .migration/plan.json
        persistence in one place.

[DC-M5] 🔬 [DEEPENING ONLY] Two overlapping node-readiness orchestrators (NodeReadinessOrchestrator and
        WorkItemsNodeReadinessOrchestrator) in WorkItems/Nodes plus a separate NodesOrchestrator in Modules
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Nodes/
  Fix:  Apply the deletion test to the smaller readiness orchestrator; merge node-readiness behind a single interface
        owned by the Nodes module (per ADR-0019 workitems extension seam), so work-item import consumes node readiness
        through one seam.

[MC-M1] ✅ [FIXED] IdentitiesModule.CaptureAsync inlines the enumeration loop, inventory-file merge, and metric
        emission instead of delegating to IIdentitiesOrchestrator (every other phase delegates; Capture does not)
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs:81-155
  Rule: module-model.md Boundaries: "Modules are phase boundaries and dispatch points; orchestration sequencing belongs
        in orchestrators" and Execution Shape: "Module methods delegate workflow sequencing to orchestrator abstractions
        for the same concern."
  Fix:  Move the identity enumeration/count loop and ProjectInventoryFile.MergeAsync call into
        IIdentitiesOrchestrator.CaptureAsync and have the module delegate, mirroring the WorkItemsModule pattern.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IIdentitiesOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/IdentitiesModuleInventoryTests.cs; .agents/30-context/domains/identity-and-mapping.md; docs/module-development-guide.md

[MC-M2] 🔄 [ESCALATED — NO CHANGE NEEDED] TeamsModule inlines Capture orchestration (enumeration loop, inventory merge) and Prepare
        orchestration (PrepareReport construction and package persistence via PersistContentAsync) in the module wrapper
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs:116-224
  Rule: module-model.md: "orchestration sequencing belongs in orchestrators" and "Module methods delegate workflow
        sequencing to orchestrator abstractions for the same concern"; the Module vs Orchestrator Split assigns
        "progress/metrics emission" to the orchestrator.
  Fix:  Move CaptureAsync enumeration and PrepareAsync report generation/persistence into ITeamsOrchestrator methods
        and delegate from the module.
  Status: 🔄 Escalated — fix already present in the working tree; no code change needed.
  Escalation detail: TeamsModule.CaptureAsync (line 116-117) already delegates to ITeamsOrchestrator.CaptureAsync and
        PrepareAsync (lines 119-136) delegates report generation/persistence to ITeamsOrchestrator.PrepareAsync;
        ITeamsOrchestrator declares both methods and TeamsOrchestrator implements them (lines 112, 184).

[MC-M3] ✅ [FIXED] NodesModule inlines Capture orchestration (node counting, inventory merge) and Prepare
        orchestration (PrepareReport serialization and package write via PersistPackageTextAsync) in the module wrapper
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs:91-215
  Rule: module-model.md: "Modules must not own checkpoint loops, stage sequencing, or replay ordering logic" /
        "Module methods delegate workflow sequencing to orchestrator abstractions."
  Fix:  Move CaptureAsync counting and PrepareAsync report persistence into INodesOrchestrator methods and delegate
        from the module.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/INodesOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs

[EC-M1] ✅ [FIXED] ADO adapters have no resilience pipeline; transient failures are swallowed inline instead of
        retried via IResiliencePipelineProvider
  File: src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs:57-70, 100-128 (also
        Identity/AzureDevOpsIdentityAdapter.cs)
  Rule: connector-model.md key rule: "Retry is via IResiliencePipelineProvider, not inline."
  Fix:  AzureDevOpsBoardAdapter and AzureDevOpsIdentityAdapter wrap every SDK call in broad `catch (Exception)` blocks
        that log a warning and return empty/null/yield break — an inline failure policy with zero retry (a transient
        429/timeout produces an empty board-config artefact that looks like success). Route SDK calls through
        IResiliencePipelineProvider and let non-transient failures surface as contract results, not swallowed catches.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs

[EC-M2] ⏳ [PENDING OPERATOR] List operations perform no pagination
  File: src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs:48-93, 131-162
        (GetBoardsAsync, GetBacklogsAsync); Identity/AzureDevOpsIdentityAdapter.cs:51-92 (SearchAsync/ReadIdentitiesAsync)
  Rule: connector-model.md key rule: "Pagination is mandatory for all list operations."
  Fix:  Board, backlog, and identity enumerations issue single unpaged SDK calls and materialise full lists. Where the
        underlying REST endpoint supports continuation (identity search does via paged reads), implement
        continuation-token paging; where the API is genuinely unpaged, document the exemption at the call site so the
        rule is visibly satisfied rather than silently ignored.

[EC-M3] ⏳ [PENDING OPERATOR] TeamSettingsTeamExtension fails the extension-seam test: team settings are a sub-object of the
        team, not a distinct domain object
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs:24
  Rule: capability-ethos-rules.md (Extension Seam Ethos): "A valid extension seam requires all of the following ...
        The concern operates on a distinct domain object with its own identity and lifecycle — not a property or
        sub-object of the core entity" and "Reject any IModuleExtension whose data is a property of the core entity..."
  Fix:  Backlog levels, bug behaviour and working days are properties of the team entity with no independent identity
        or lifecycle. Fold team-settings export/import into the core Teams pipeline governed by module options, or
        record an explicit Guardrail Challenge Protocol exception.

[EC-M4] ⏳ [PENDING OPERATOR] BoardConfigTeamExtension embeds core merge/validation engine logic instead of being a thin
        policy facade
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/BoardConfigTeamExtension.cs:389-470
        (BuildValidStatesMap, FilterInvalidStateMappings, MergeByName)
  Rule: capability-ethos-rules.md rule 3: "Core capability logic is centralized. Translation, mapping, and validation
        engines for a concern must live once behind the canonical seam. Duplicating that logic in modules, orchestrators,
        or extensions is forbidden"; rule 4: "Adapters are thin policy facades."
  Fix:  State-mapping validation and name-keyed merge semantics are transform/validation engine logic living inside the
        extension. Extract into a canonical board-config merge/validation seam (e.g. an XxxTool) so future connectors
        and orchestrators cannot re-implement it, leaving the extension with skip/fail/checkpoint policy only.

[EC-M5] ✅ [FIXED] AgentControlPlaneClientAdapter bare catch swallows cancellation and all failures as
        'agent inactive'
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentControlPlaneClientAdapter.cs:31-53
  Rule: connector-model.md: "Retry is via IResiliencePipelineProvider, not inline."
  Fix:  The adapter uses a bare `catch { return false; }` — no resilience pipeline, and OperationCanceledException is
        converted into `false` (a live agent reported stale on shutdown, risking lock steal from an active agent).
        Rethrow OperationCanceledException, use the resilience pipeline for transient HTTP failures, and log a
        structured warning before returning false.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentControlPlaneClientAdapter.cs

[TC-M1] ⏳ [PENDING OPERATOR] IdentityTranslationTool owns state between invocations, does package I/O writes, and depends
        on an orchestrator (seam inversion)
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityTranslation/IdentityTranslationTool.cs:43, 47-49,
        73-156, 175, 190-210
  Rule: execution-model.md Layer: Tool — "Must not own: State between invocations"; "No I/O, no network calls, no
        filesystem access"; Logic seam: "Extension → Tool ... Tool owns: the pure transformation itself"
  Fix:  Three rule breaches: (1) InitializeAsync mutates _overrides/_prepared/_allUniqueNames fields consumed by later
        Translate calls; (2) WriteUnresolvedAsync persists unresolved.json via IPackageAccess; (3) the tool
        constructor-injects IIdentitiesOrchestrator and calls _orchestrator.ResolvePrepared — a lower layer calling up
        into an orchestrator. Move package read/write and the orchestrator fallback into the Identities
        orchestrator/extension, and pass the resolved maps into a pure tool.

[TC-M2] ⏳ [PENDING OPERATOR] IFieldTransformTool is registered AddScoped, contradicting the singleton Tool contract
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformToolServiceCollectionExtensions.cs:52-67
  Rule: execution-model.md: "A Tool is a singleton with one central config for the entire run, declared once at
        MigrationPlatform.Tools.*. One instance, one config, shared by every consumer."
  Fix:  FieldTransformTool, FieldTransformFactory, and FieldTransformValidator are all AddScoped. The per-job
        IOptionsSnapshot rationale in the XML doc is a real constraint, but it conflicts with the written contract —
        either change the registration to singleton with a config-accessor indirection, or amend the contract/doc via
        change governance (field-transform-contract.md Governance: such surface-semantics changes are "at least Class B").
```

## Low Violations (fix when convenient)

```
[MM-L1] ✅ [FIXED] Layer-first 'Models' folders inside modules: ControlPlane/Models and
        Infrastructure.TfsObjectModel/WorkItems/Revisions/Models organise by technical layer rather than concern
  File: src/DevOpsMigrationPlatform.ControlPlane/Models;
        src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItems/Revisions/Models
  Fix:  Relocate the types in each Models/ folder next to the concern that owns them (e.g. Jobs/, Revisions/ root) so
        collaborating classes are co-located; delete the generic Models folders.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobRecord.cs; src/DevOpsMigrationPlatform.ControlPlane/Controllers/AgentLeaseResponse.cs; src/DevOpsMigrationPlatform.ControlPlane/Controllers/AgentStatusResponse.cs; src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobStore.cs; src/DevOpsMigrationPlatform.ControlPlane/Jobs/IJobStore.cs; src/DevOpsMigrationPlatform.ControlPlane/Controllers/AgentLeaseController.cs

[CA-L1] ✅ [FIXED] Task-list presentation logic in TuiTaskProgressView encodes domain grouping rules (TaskKind,
        discovery-module sort keys, phase membership)
  File: src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiTaskProgressView.cs:110-468
  Fix:  Mostly presentation shaping, but the discovery-module derivation (GetDiscoveryModule/GetDiscoveryModuleSortKey)
        and phase-membership rules duplicate logic also present in QueueCommand; consolidate them into a shared
        task-summary/read-model service so TUI and CLI render the same computed model.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.CLI.Migration/Services/JobTaskSummaryService.cs; src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiTaskProgressView.cs; src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs

[HX-L1] ✅ [FIXED] ConfigTokenResolver in the Abstractions project reads Environment.GetEnvironmentVariable
        directly (environment access in domain/abstraction layer)
  File: src/DevOpsMigrationPlatform.Abstractions/Options/ConfigTokenResolver.cs:33
  Fix:  Inject a Func<string,string?> / IEnvironmentReader seam (defaulting to Environment.GetEnvironmentVariable at
        the composition root) so the resolver in Abstractions has no direct environment dependency and is testable
        without process env mutation.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions/Options/ConfigTokenResolver.cs

[HX-L2] ✅ [FIXED] AgentOtelExtensions performs direct Directory.CreateDirectory/file-path I/O and reads
        Telemetry__DiagnosticsSessionId via Environment.GetEnvironmentVariable in agent code
  File: src/DevOpsMigrationPlatform.TfsMigrationAgent/AgentOtelExtensions.cs:57, 94, 122
  Fix:  Read the session id via IConfiguration (already available in the builder context); keep directory creation
        inside the DiagnosticsFileMetricExporter/infrastructure exporter rather than the agent's OTel wiring extension.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.TfsMigrationAgent/AgentOtelExtensions.cs

[HX-L3] ✅ [FIXED] TfsMigrationAgent Program.cs creates log directories with Directory.CreateDirectory during
        Serilog bootstrap
  File: src/DevOpsMigrationPlatform.TfsMigrationAgent/Program.cs:72
  Fix:  Acceptable at a composition root, but prefer letting Serilog's File sink create the directory (shared:true
        handles this) or moving path preparation into a small infrastructure helper so the host entry point stays free
        of raw System.IO calls.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.TfsMigrationAgent/Program.cs

[VS-L1] ✅ [FIXED] NodeReplicationProgress.StateKey is an unscoped constant ("nodestructure-nodes-confirmed")
        with stale XML docs claiming IStateStore persistence; the constant is dead
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/NodeReplicationProgress.cs:10-19
  Fix:  Delete the unused StateKey constant and correct the XML doc to describe package-content persistence via
        NodesOrchestrator.SaveProgressAsync; if a state-store key is ever reintroduced, scope it as
        <operation>/<jobId>/nodestructure-nodes-confirmed.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/NodeReplicationProgress.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeTranslation/NodeEnsurerTests.cs

[SA-L1] ✅ [FIXED] Technical verb ExecuteAsync on freely-named orchestrator NodeReadinessOrchestrator (Check 5)
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Nodes/NodeReadinessOrchestrator.cs:61
  Fix:  Rename ExecuteAsync(ProjectMapping, bool, CancellationToken) to a business verb describing the outcome, e.g.
        PrepareRequiredNodePathsAsync or EnsureNodeReadinessAsync (not framework-mandated, so freely renameable).
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Nodes/NodeReadinessOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/Nodes/WorkItemsNodeReadinessOrchestrator.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/NodeReadinessOrchestratorTests.cs

[SA-L2] ✅ [FIXED] Technical verb RunAsync on InventoryOrchestrator (Check 5)
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs:72
  Fix:  Rename RunAsync to a business verb such as RunInventoryAsync or RecordInventoryAsync so the call site reads as
        the business operation it performs.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs; src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IInventoryOrchestrator.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemResolution/WorkItemsOrchestrator.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsOrchestratorInventoryTests.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModules/InventoryModuleFactory.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/InventoryOrchestratorTests.cs

[DC-L1] 🔬 [DEEPENING ONLY] Abstractions.ControlPlane project exists to hold a single 31-line interface (IJobLifecycleMetrics)
  File: src/DevOpsMigrationPlatform.Abstractions.ControlPlane/Metrics/IJobLifecycleMetrics.cs:1-31
  Fix:  Fold IJobLifecycleMetrics into DevOpsMigrationPlatform.Abstractions (which already hosts telemetry contracts)
        unless a compiler-enforced boundary (ADR-0007) genuinely requires isolation.

[DC-L2] 🔬 [DEEPENING ONLY] Static JsonSerializerOptions and ActivitySource declarations copy-pasted into nearly every
        orchestrator and executor
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/ (NodesOrchestrator.cs, TeamsOrchestrator.cs,
        JobPlanExecutor.cs, and siblings)
  Fix:  Package-serialisation options (camelCase, ignore-null, case-insensitive) are an invariant of the filesystem
        package format (ADR-0002), not of each orchestrator. Centralise as PackageJson.Options next to IPackageAccess,
        and one shared ActivitySource holder for WellKnownActivitySourceNames.Migration. Subsumed by DC-H2.

[DC-L3] 🔬 [DEEPENING ONLY] Naming drift undermines the ubiquitous language: '_PlatformMetrics' PascalCase field, and
        'Orchestrator' used for at least four different roles (module runner, per-entity exporter, plan executor
        helper, readiness checker)
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs (field declarations;
        solution-wide suffix usage)
  Fix:  Fix the casing, reserve 'Orchestrator' for the IModule five-phase runner, rename per-entity workers
        (TeamExportOrchestrator) and readiness checkers; no CONTEXT.md exists — creating one and recording these terms
        would give future deepening work its domain glossary.

[MC-L1] ⏳ [PENDING OPERATOR] TeamsModule and NodesModule PrepareAsync write a hardcoded empty PrepareReport
        (ResolvedCount = 0) with no target validation performed
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs:188-220 (also NodesModule.cs:177-211)
  Rule: module-model.md Execution Shape defines PrepareAsync as "validates target"; emitting a fixed zero-count report
        and metrics satisfies the phase in name only.
  Fix:  Implement real target validation in the Teams/Nodes prepare path (via the orchestrator per MC-M2/M3) or set
        SupportsPrepare = false until it exists.

[MC-L2] ⏳ [PENDING OPERATOR] WorkItemsModule.DependsOn declares a ModuleDependency on InventoryAnalyser, which is an
        IAnalyser, not an IModule
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs:35
  Rule: module-model.md Related Extension Points table defines Analyser as a separate extension point that runs "After
        all inventory modules complete"; IModule.DependsOn is documented as "Modules this one depends on". Encoding an
        analyser as a module dependency blurs the extension-point taxonomy.
  Fix:  Express the analyser ordering guarantee through the phase pipeline (analysers already run after inventory) or a
        dedicated analyser-dependency mechanism, and constrain ModuleDependency targets to IModule types.

[MC-L3] ✅ [FIXED] ModuleBase has zero subclasses in src/ (dead abstraction) and its unsupported-phase defaults
        are inconsistent: CaptureAsync/PrepareAsync return Skipped with a warning, while
        ExportAsync/ImportAsync/ValidateAsync silently return Completed
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs:37-50
  Rule: module-model.md phase-support semantics: "Modules that return false are excluded from ... task plans" —
        silently returning Completed falsely reports success for phases a module does not support.
  Fix:  Either delete ModuleBase or make all unsupported-phase defaults return TaskExecutionResult.Skipped with a
        warning, consistent with CaptureAsync/PrepareAsync.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs

[EC-L1] ⏳ [PENDING OPERATOR] Extensions pass null! for the endpoint parameter of ITeamTarget methods
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/TeamSettingsTeamExtension.cs (also
        TeamAreaPathsTeamExtension.cs, TeamMembersTeamExtension.cs) — SetTeamSettingsAsync(null!, ...),
        SetAreaPathsAsync(null!, ...), AddMemberAsync(null!, ...)
  Rule: capability-ethos-rules.md rule 8 (spirit): "Evolution goes behind the seam" — the seam contract declares an
        endpoint parameter the policy layer cannot supply, so callers forge it with null!.
  Fix:  Either remove the parameter from the ITeamTarget contract or resolve the real target endpoint; any connector
        implementation that reads it will NRE at runtime.

[TC-L1] ✅ [FIXED] TfsMigrationAgent registers concrete NodeTranslationTool as a parallel DI entry point beside
        the canonical seam
  File: src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs:109-117
  Rule: capability-ethos-rules.md rule 2: "One public reusable surface per seam ... Parallel runtime entry points for
        the same concern are forbidden" and rule 8: "must not add alternate runtime entry points beside the seam".
  Fix:  After calling AddNodeTranslationToolServices(), the host re-registers the concrete class NodeTranslationTool
        and re-maps INodeTranslationTool from it. Fix the net481 path inside AddNodeTranslationToolServices (single
        registration site) instead of duplicating construction wiring in the host; consumers depend only on
        INodeTranslationTool, never the concrete type.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/NodeTranslationToolServiceCollectionExtensions.cs; src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs

[TC-L2] ✅ [FIXED] NodesOrchestrator binds the Tool's central config (IOptionsMonitor<NodeTranslationOptions>)
        directly, bypassing the seam surface
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs:65, 242
  Rule: execution-model.md: Tool config is "run-wide and central" and consumers use the tool via its canonical surface;
        capability-ethos-rules.md rule 2 forbids parallel entry points for the same concern (rule 5: policy duplication risk).
  Fix:  The orchestrator injecting the tool's own options section (MigrationPlatform:Tools:NodeTranslation) creates a
        second consumer of the tool's private configuration. Expose whatever gating/enablement info the orchestrator
        needs (e.g. IsEnabled, HasMappings) on INodeTranslationTool and remove the direct options dependency.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeTranslationTool.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeTranslation/NodeTranslationTool.cs; src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/NodesModuleTests.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/NodesModuleInventoryTests.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModules/InventoryModuleFactory.cs; tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeTranslation/NodeEnsurerTests.cs

[TC-L3] ⏳ [PENDING OPERATOR] Duplicated embedded-image reference parsing across export and import — deepening gap for a
        canonical Tool
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/EmbeddedImageExportService.cs:60-90 vs
        WorkItems/Attachments/EmbeddedImageRewriteTool.cs:22-23, 153-174
  Rule: capability-ethos-rules.md rule 3: "Core capability logic is centralized. Translation, mapping, and validation
        engines for a concern must live once behind the canonical seam. Duplicating that logic in modules,
        orchestrators, or extensions is forbidden."
  Fix:  Export parses <img src> via HtmlAgilityPack while import re-implements image-reference detection with
        hand-rolled Markdown/HTML regexes. Extract a single pure IEmbeddedImageReferenceTool (parse + rewrite) in
        Abstractions.Agent/Tools/ consumed by both phases — this also removes the parser-behavior divergence between
        the two regimes.
```

## Informational

```
[MM-I1] ⏳ [PENDING OPERATOR] Public contracts declared inside modules (IJobStore/ILeaseJobResolver in ControlPlane,
        ITfsJobServiceFactory in TfsObjectModel, IAzureDevOpsClientFactory in AzureDevOps) — currently single-module
        consumers, so compliant, but watch for cross-module adoption
  File: src/DevOpsMigrationPlatform.ControlPlane/Jobs/IJobStore.cs
  Fix:  No action required now. If any second module or the infrastructure layer starts consuming these interfaces,
        move them to the appropriate Abstractions project (Abstractions.ControlPlane / Abstractions.Agent) at that
        point (a Class C change requiring the consent gate).

[CA-I1] 🔬 [DEEPENING ONLY] Use-case orchestrators (WorkItemExportOrchestrator and factories) live in a project named
        Infrastructure.Agent, obscuring the ring boundary
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs:1
  Fix:  No dependency-rule breach found (the orchestrators import no SDK namespaces), but the application ring shares a
        project and namespace with adapters (Telemetry, Connectors). Consider splitting an Application.Agent project
        (or at minimum namespace segregation with an architecture test) so Check 2 violations become compile-time
        impossible.

[HX-L4] 🔬 [DEEPENING ONLY] AgentLifecycleService (ControlPlaneHost) uses File.Exists to probe agent executable paths —
        host-level process management, boundary-adjacent but acceptable
  File: src/DevOpsMigrationPlatform.ControlPlaneHost/AgentLifecycle/AgentLifecycleService.cs:83, 118
  Fix:  No change required (host composition concern; the file already routes environment reads through IConfiguration
        per Hexagonal rule 6). If testability becomes a need, wrap the executable probe behind an IAgentExecutableLocator.

[HX-L5] 🔬 [DEEPENING ONLY] SchemaGeneratorHost writes schema output with File.WriteAllTextAsync/Directory.CreateDirectory —
        dev tooling, outside the migration runtime boundary
  File: src/DevOpsMigrationPlatform.SchemaGenerator/SchemaGeneratorHost.cs:86-91
  Fix:  No change required; SchemaGenerator is a build-time tool, not module/domain code. Document the exemption if the
        archcheck is automated.

[VS-I1] ✅ [FIXED] Check 5 (features/ directory) is not applicable: .feature files were deliberately retired to
        the MSTest internal DSL (commits #126/#127); the skill document is stale relative to the codebase
  File: .agents/skills/nkda-archcheck-vertical-slice/SKILL.md:150-171
  Fix:  Update SKILL.md Check 5 to validate slice acceptance coverage against the internal MSTest DSL scenario tests
        (Testing.Dsl) instead of Gherkin .feature files, so the check reflects the post-Reqnroll architecture.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: .agents/skills/nkda-archcheck-vertical-slice/SKILL.md

[SA-I1] ✅ [FIXED] Technical verb RunAsync on TelemetryPoller (Check 5, UI-infrastructure type)
  File: src/DevOpsMigrationPlatform.CLI.Migration/Views/TelemetryPoller.cs:38
  Fix:  Optional: rename RunAsync to PollTelemetryAsync. Low impact since TelemetryPoller is CLI view infrastructure,
        not a domain/use-case class.
  Status: ✅ Fixed — applied and verified (build passed, tests passed)
  Fixed in: uncommitted working tree, branch `update-for-comms` (verified by full build + test run, 2026-07-02)
  Files modified: src/DevOpsMigrationPlatform.CLI.Migration/Views/TelemetryPoller.cs; tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsPanelDslTests.cs; tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/JobDetail/TuiJobDetail_LiveDataStreaming_DslTests.cs

[SA-I2] 🔬 [DEEPENING ONLY] Check 4 not evaluable: no features/ directory exists on this branch
  File: features/
  Fix:  No .feature files found under the repository root or src/tests (only inside an unrelated .claude/worktrees
        checkout). If Gherkin acceptance criteria are expected per the skill, restore or relocate the features/ tree;
        otherwise update the skill/scope to reflect scenario definitions living in scenarios/*.json.

[MC-I1] ⏳ [PENDING OPERATOR] The authoritative doc .agents/30-context/domains/module-model.md does not exist at its
        documented path in the main repo checkout — only found under .claude/worktrees/crazy-goldberg-c58e96/
  File: .agents/30-context/domains/module-model.md
  Rule: Routing contract (fail-closed): agents following the reading order from the main branch cannot load the Module
        Model rules this audit was run against.
  Fix:  Merge or restore module-model.md into the main branch .agents/30-context/domains/ directory so the documented
        reading order resolves.

[OC-I1] ⏳ [PENDING OPERATOR] Audit blocked: authoritative Orchestrator Model documents do not exist
  File: .agents/30-context/domains/orchestrator-model.md; .agents/10-contracts/specs/orchestrator-contract.md
  Rule: The audit instructions require applying rules only from these documents and forbid prior knowledge, so no
        compliance verdicts can be issued (fail-closed). Routing contract: "If no activity matches, stop and ask the
        operator."
  Fix:  Nine I*Orchestrator implementations were located and are ready to audit once the documents exist
        (WorkItemsOrchestrator, TeamsOrchestrator, NodesOrchestrator, IdentitiesOrchestrator, DependencyOrchestrator,
        WorkItemsNodeReadinessOrchestrator, WorkItemExportOrchestrator, WorkItemExportOrchestratorFactory,
        InventoryOrchestrator). Author or restore the two documents (or correct the paths in
        .agents/workflows/nkda-archcheck-workflow.js:189), then re-run this audit.

[EC-I1] 🔬 [DEEPENING ONLY] Referenced authority document .agents/30-context/domains/capability-seam-contract.md does not exist
  File: .agents/30-context/domains/capability-seam-contract.md
  Rule: The audit brief names this file as authoritative but it is absent. Seam rules were sourced from
        .agents/20-guardrails/core/capability-ethos-rules.md and .agents/10-contracts/seam-catalog.yaml instead.
  Fix:  Either create the contract doc or fix references (e.g. in workflow scripts) that point at the missing path.

[EC-I2] 🔬 [DEEPENING ONLY] SimulatedBoardAdapter mixes test-assertion capture surface into a production-registered connector
  File: src/DevOpsMigrationPlatform.Infrastructure.Simulated/Teams/SimulatedBoardAdapter.cs:19-33
  Rule: Not a quoted-rule violation (the Simulated >=2-item rule is satisfied: 2 boards, 2 backlogs, 2 swimlanes), but
        the public Update*Calls capture lists exist purely for test assertion — an extra public API beside the seam
        contract.
  Fix:  Consider exposing the capture lists via a test-only interface.

[TC-I1] 🔬 [DEEPENING ONLY] FieldTransformTool carries phase knowledge — sanctioned by field-transform-contract.md but
        contradicting the generic Tool rule
  File: src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformTool.cs:135-149, 156-162
  Rule: execution-model.md says a Tool "Must not own: Phase knowledge", yet field-transform-contract.md mandates
        "IsEnabledForPhase(FieldTransformPhase phase)" on the canonical surface — the two authoritative documents
        contradict each other.
  Fix:  Reconcile the docs: either carve out an explicit exception in the Tool layer description or move phase gating
        to the WorkItems extension adapter (which already binds Phase via FieldTransformExtensionOptions).

[TC-I2] 🔬 [DEEPENING ONLY] Two of the three cited authoritative documents do not exist in the repository
  File: .agents/30-context/domains/module-model.md; .agents/30-context/domains/capability-seam-contract.md
  Rule: Referenced by the archcheck workflow (.agents/workflows/nkda-archcheck-workflow.js:231-232) but absent. The
        audit used the actual equivalents: .agents/30-context/architecture/execution-model.md (Layer: Tool),
        .agents/20-guardrails/core/capability-ethos-rules.md, and .agents/10-contracts/specs/field-transform-contract.md.
  Fix:  Either create the two named docs or update the workflow prompt to point at the real files so the fail-closed
        routing model stays truthful.
```

## Deepening Opportunities

Advisory items from the Architecture Deepening pass and perspective-level notes. No fixes are queued; each is a candidate for a future deepening initiative.

- **CA-I1** — Consider splitting an Application.Agent project to separate the use-case ring from adapters; optionally add namespace segregation plus an architecture test.
- **HX-L4** — AgentLifecycleService File.Exists probe: acceptable host concern; optionally wrap behind an IAgentExecutableLocator.
- **HX-L5** — SchemaGeneratorHost file writes: dev-tooling exemption; document if archcheck is automated.
- **EC-I1** — Missing capability-seam-contract.md authority document: create it or fix workflow references (document-authority decision).
- **EC-I2** — SimulatedBoardAdapter test-assertion capture surface: consider a test-only interface.
- **DC-H1** — Deepen QueueCommand via job-progress projection extraction; command shrinks to wiring.
- **DC-H2** — Build a shared module run harness behind the IModule five-phase contract (checkpoint/resume, progress, metrics, tracing).
- **DC-H3** — Replace #if NET481 preprocessor forks with project-boundary adapters (two real adapters instead of compile-time forks).
- **DC-H4** — Replace nullable optional dependencies with non-null no-op adapters (NullProgressSink/NullPlatformMetrics) in DI.
- **DC-M1** — Deepen ModulePipelineWorkerBase to own job-kind dispatch and endpoint-context setup; workers become thin connector adapters.
- **DC-M2** — Audit Abstractions.Agent single-implementation interfaces against real vs hypothetical seams; collapse category-1 in-process hypothetical seams.
- **DC-M3** — Implement or delete the empty Infrastructure.Storage.AzureBlob project; record deferral in an ADR.
- **DC-M4** — Extract one 'endpoint options from package config' module shared by builder, executor, workers; concentrate plan.json persistence in one place.
- **DC-M5** — Merge overlapping node-readiness orchestrators behind one Nodes-owned seam per ADR-0019.
- **DC-L1** — Fold single-interface Abstractions.ControlPlane project into Abstractions unless a compiler-enforced boundary genuinely requires isolation.
- **DC-L2** — Centralise package JsonSerializerOptions (PackageJson.Options next to IPackageAccess) and one shared ActivitySource holder; subsumed by DC-H2.
- **DC-L3** — Fix naming drift (_PlatformMetrics casing; overloaded 'Orchestrator' suffix); create CONTEXT.md glossary.
- **TC-I1** — Reconcile the Tool 'no phase knowledge' rule with field-transform-contract.md IsEnabledForPhase.
- **TC-I2** — Fix archcheck workflow references to two nonexistent authority documents (point at execution-model.md, capability-ethos-rules.md, field-transform-contract.md).

---

## Recommended Next Steps

One action per operator-blocked item. Every item requires **explicit operator consent** before any change is made; the additional evidence listed must accompany the change.

1. **MM-C1** — Decide whether to remove the Infrastructure.TfsObjectModel → Infrastructure.Storage.FileSystem ProjectReference and relocate AddPackageBoundaryServices() to the TfsMigrationAgent host. Evidence: operator consent, ADR, contract compatibility tests, RED→GREEN→REFACTOR trace. (Decide together with MM-H1 and CA-C2.)
2. **MM-H1** — Approve extracting the subprocess host composition root (Serilog/OTel/storage selection) out of MigrationPlatformHost.cs into the TfsMigrationAgent host. Evidence: consent, ADR, contract tests, test-first trace.
3. **CA-C1** — Approve defining an IWorkerEventWriter port in Abstractions(.Agent) for UnifiedWorkerEventWriter and injecting it into both workers. Evidence: consent, ADR, contract tests, test-first trace.
4. **CA-C2** — Approve removing the Infrastructure.Storage.FileSystem dependency from JobAgentWorker/TfsJobAgentWorker in favour of Abstractions.Storage ports. Evidence: consent, ADR, contract tests, test-first trace. (Coupled with MM-C1.)
5. **CA-H1** — Approve moving ITfsJobServiceFactory (plus boundary DTOs) into Abstractions.Agent. Evidence: consent, ADR, contract tests, test-first trace.
6. **HX-M1** — Duplicate of CA-H1; execute once under a single consent grant. Evidence: shared consent, ADR, contract tests, test-first trace.
7. **HX-H1** — Approve replacing the FileNotFoundException coupling in IPackageAccess.ResetMetaAsync with a storage-neutral result/abstraction exception. Evidence: consent, ADR, contract tests covering all IPackageAccess consumers and both storage adapters, test-first trace.
8. **VS-H1** — Approve elevating WorkItemsPrepareRevisionReader to an injected IWorkItemRevisionReader in Abstractions.Agent (seven consumers). Evidence: consent, ADR, contract tests, test-first trace.
9. **VS-H2** — Approve defining IProjectInventoryReader/Writer in Abstractions.Agent to replace the static ProjectInventoryFile (seven consuming slices). Evidence: consent, ADR, inventory file round-trip contract tests, test-first trace.
10. **VS-H3** — Approve moving KnownProcessIds (or an IProcessIdResolver contract) into Abstractions. Evidence: consent, ADR, contract tests across all three connectors, test-first trace.
11. **VS-M3** — Choose a resolution for WorkItemRevisionFolderParser cross-slice coupling: move the naming contract to Abstractions.Agent or consume the VS-H1 reader (dependent on VS-H1's outcome). Evidence: consent, ADR (package folder-naming contract), contract tests, test-first trace.
12. **MC-H1** — Approve implementing the mandated module anatomy contract surface (IModuleContract, Selection/Data/Processing) across all four IModule implementations. Evidence: consent, ADR, contract tests (IModule implementations + config schema), test-first trace.
13. **MC-H2** — Approve migrating WorkItemsModuleOptions from legacy Scope/Extensions to Selection/Data/Processing and regenerating migration.schema.json (breaking for user configs). Evidence: consent, ADR, schema round-trip and legacy-config shim tests, test-first trace.
14. **MC-L1** — Choose: implement real Teams/Nodes Prepare validation, or set SupportsPrepare = false. Evidence: consent recording the choice, ADR, phase-support semantics contract tests, test-first trace.
15. **MC-L2** — Approve constraining ModuleDependency targets to IModule and re-expressing InventoryAnalyser ordering. Evidence: consent, ADR, dependency-ordering contract tests, test-first trace.
16. **MC-I1** — Decide which version of module-model.md is authoritative and restore it to .agents/30-context/domains/ on main. Evidence: consent on the authoritative version, ADR/linked record, verification that routing-catalog/reading-order references resolve.
17. **OC-I1** — Author or restore the Orchestrator Model authority documents (orchestrator-model.md, orchestrator-contract.md) or correct the workflow paths, then re-run the OC audit. Evidence: consent + authorship, ADR, corrected workflow references, OC audit re-run.
18. **EC-H1** — Approve introducing ConnectorCapability flags (TeamSettings, TeamMembers, Comments, …) and removing nullable gating in the six extensions. Evidence: consent, ADR defining the flags, contract tests across Simulated/AzureDevOps/TFS, test-first trace.
19. **EC-M2** — Approve pagination implementation for ADO board/backlog/identity list operations and rule exemptions where endpoints are genuinely unpaged. Evidence: consent for each exemption, documented exemption at each unpaged call site, paged-enumeration behavioral tests.
20. **EC-M3** — Choose: fold TeamSettingsTeamExtension into the core Teams pipeline, or record a Guardrail Challenge Protocol exception. Evidence: consent recording the choice, ADR, team package content before/after contract tests, test-first trace.
21. **EC-M4** — Approve extracting the board-config merge/validation engine into a canonical Tool seam. Evidence: consent, ADR, contract tests, test-first trace.
22. **EC-L1** — Choose: remove the endpoint parameter from ITeamTarget, or resolve and pass the real target endpoint. Evidence: consent recording the choice, ADR if the signature changes, cross-connector contract tests, test-first trace.
23. **TC-H1** — Choose: rename AttachmentReplayTool to Service/Handler, or promote it to a canonical seam with an Abstractions.Agent interface. Evidence: consent recording the direction, ADR, contract tests if a seam is added, test-first trace.
24. **TC-H2** — Choose: rename EmbeddedImageRewriteTool, or split it (pure rewrite stays a Tool; upload orchestration moves to the extension). Evidence: consent recording the direction, ADR, contract tests, test-first trace.
25. **TC-M1** — Approve purifying IdentityTranslationTool (move package I/O and the orchestrator fallback into the Identities orchestrator/extension). Evidence: consent, ADR, contract tests preserving the unresolved.json production path, test-first trace.
26. **TC-M2** — Choose: change IFieldTransformTool registration to singleton with config-accessor indirection, or amend field-transform-contract.md via change governance. Evidence: consent recording the choice, ADR, per-job options behavior contract tests, test-first trace.
27. **TC-L3** — Approve extracting a single pure IEmbeddedImageReferenceTool shared by export and import. Evidence: consent, ADR, export/import parity contract tests, test-first trace.
28. **MM-I1** — Watch item only: no action now. If cross-module adoption of IJobStore/ILeaseJobResolver/ITfsJobServiceFactory/IAzureDevOpsClientFactory occurs, obtain consent + ADR + contract tests before any relocation to Abstractions.
29. **MC-M2** — Confirm the escalation: the delegation to ITeamsOrchestrator is already in place in the working tree; review and close the finding (no code change). Evidence: operator confirmation; ADR only if the current shape is disputed.

After the operator decisions, also consider scheduling the 19 deepening-only items (DC-H1..DC-L3 and perspective advisories) as a separate architecture-deepening initiative.

---

The review, triage, auto-fix, and verify phases are complete: 26 auto-fixes applied and verified (build + tests green), 1 item escalated as already-fixed (MC-M2), 0 failed, 0 reverted, and 29 items remain blocked on operator consent. Machine-readable triage is written to `analysis/archcheck/triage.json`.

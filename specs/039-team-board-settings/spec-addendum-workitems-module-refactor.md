# Spec Addendum — WorkItems Module Refactor (extension-model alignment)

**Status**: **In progress — Stage 1 COMPLETE (test-first, verified green). Stages 2–4 pending.** Design
accepted in [ADR 0019](../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md).

Stage 1 delivered:
- **Increment 1 — inventory move (RED→GREEN→REFACTOR).** `WorkItemsOrchestratorInventoryTests` written
  and run **RED** (NotImplementedException ×4) → orchestrator `CaptureAsync` implemented **GREEN** (4/4)
  → module delegates; dead `ApplyImportReplayLevers` + unused activity sources removed. Also closed a
  DI-vs-factory duplicate-construction regression (DI now passes the inventory deps).
- **Increment 2 — factory removed, module thinned.** `IWorkItemsOrchestratorFactory` /
  `WorkItemsOrchestratorFactory` **deleted**; DI composes the orchestrator graph directly; `WorkItemsModule`
  is now a thin façade taking only `IWorkItemsOrchestrator` (ctor collapsed from ~30 params to 1, all
  phases delegate). Obsolete fat-ctor guard-clause tests and the redundant module-inventory tests removed;
  the module-isolation test reframed to the thin module + orchestrator. Behavioural construction relocated
  to a `WorkItemsModuleTestFactory` test helper.

**Gate: full suite green (1064/1064), both net481 + net10 build.** Remaining: Stages 2–4 (staged cursor
pipeline + Links → Attachments → Comments), each failing-test-first per §6/§10.
**Parent feature**: [039-team-board-settings](spec.md) (extension architecture established here).
**Scope**: `WorkItemsModule`, `WorkItemsOrchestrator`, and the `WorkItems/**` per-revision runtime.
**Why it lives here**: 039 established the canonical extension model
([execution-contract](../../.agents/10-contracts/specs/execution-contract.md),
[execution-model](../../.agents/30-context/architecture/execution-model.md)). This addendum is the
approved plan for bringing WorkItems into line with it. It is **not** an approval to begin editing
code; implementation must follow the test-first session model in §10.

---

## 1. Problem statement

`WorkItemsModule` is **not** a thin module today. `ExportAsync` / `ImportAsync` / `PrepareAsync` /
`ValidateAsync` are thin one-line delegations to `_workItemsOrchestrator`, but three things break the
thin-module rule:

1. **`CaptureAsync` (~177 lines inline)** — the inventory flow lives in the module: the
   `BuildEventStream` closure, repo counting, discovery streaming, the no-orchestrator fallback path,
   and `ProjectInventoryFile.MergeAsync`. This is entity-loop + capability logic a module must not own.
2. **Constructor is a composition root** — it news up the orchestrator and, inside it,
   `WorkItemsImportCapabilityValidator`, `WorkItemsNodeReadinessOrchestrator`, `ImportPreparer`, and
   `CreateDefaultImportFailurePatterns`. The ctor takes **~30 dependencies**, most existing only to
   build the orchestrator.
3. **Holds tools** — injects `IIdentityTranslationTool`, `INodeTranslationTool`, `IFieldTransformTool`
   purely to forward them to the orchestrator factory. A thin module must not hold tools (seam break).

Also: **`ApplyImportReplayLevers` in the module is dead code** — the live copy is in
`WorkItemsImportRuntime` (the orchestrator runtime).

---

## 2. Decided model (this session)

### Layer split
- **Core — owned by the orchestrator**: the **revision stream** (the per-entity loop spine),
  resolution strategy, id-map, node-readiness sequencing, checkpointing, metrics, progress.
- **Extensions (`IModuleExtension`, each with its own `IOptions<T>`)**: **Links, Attachments,
  Comments, EmbeddedImages** — facets attached to a work item.
- **Tools (singleton, one central config, consumed by orchestrator/extensions — never the module)**:
  Identity translation, Node translation, FieldTransform. (Plus optionally a small image
  ref-rewrite/download tool used by the EmbeddedImages extension.)

### Revisions are CORE, not an extension
A work item *is* its revision history; the orchestrator's per-entity loop **is** the revision stream
(`IWorkItemRevisionSourceFactory`). Disabling revisions means nothing is exported — that is not an
optional capability. The `RevisionsEnabled` flag in `WorkItemsModuleExtensions` is mislabelled; it is
effectively always-on core, not an extension toggle.

### EmbeddedImages is an EXTENSION, not a tool
`IEmbeddedImageDownloader.TryDownloadAsync` does network download; `IEmbeddedImageExportService`
downloads images and **stores them locally** under a per-work-item `folderPath`, then rewrites refs.
That is per-entity behaviour with its own artifacts and its own `Enabled`/`DownloadTimeoutSeconds`
config — structurally identical to Attachments. The discriminator is **config cardinality** (shared
singleton service = tool; per-entity behaviour with own config = extension), *not* whether it does
I/O. The only tool-shaped sliver is the pure parse/rewrite of `<img>` / `![]()` refs, which the
extension *may* call as a small tool.

> **Open operator decision (does not change the classifications above):** the current
> `execution-contract.md` (rules 25/28) and `execution-model.md` state "Tools are stateless and pure
> — no I/O, no network, no filesystem." This is **wrong** (e.g. `IIdentityTranslationTool` reads a
> cache; a downloader is still a tool). The tool definition should be corrected to
> *"singleton + one central config + provides a service to many consumers"* and drop the no-I/O
> clause. Flagged for a separate contract change; not done here.

### Extension count
**Four** extensions: Links, Attachments, Comments, EmbeddedImages. (Not five — Revisions is core.)
`Query`, include/exclude `Filters`, and `WorkItemResolutionStrategy` are **config/policy**, not
extensions.

---

## 3. Tools and extensions reference

| Item | Backing source/service | Classification |
|---|---|---|
| Revisions | `IWorkItemRevisionSourceFactory` | **Core** (orchestrator owns the loop) |
| Links | revision relations | **Extension** (optional) |
| Attachments | `IAttachmentBinarySource` | **Extension** (optional) |
| Comments | `IWorkItemCommentSourceFactory` | **Extension** (optional) |
| EmbeddedImages | `IEmbeddedImageExportService` / `IEmbeddedImageDownloader` | **Extension** (optional); may call a ref-rewrite tool |
| Identity translation | `IIdentityTranslationTool` | **Tool** |
| Node translation | `INodeTranslationTool` | **Tool** |
| Field transform | `IFieldTransformTool` | **Tool** |

---

## 4. Implementation plan

### Phase 0 — Regression net (no changes)
Confirm the WorkItems suite is green; pin the parity baseline (inventory counts; exported
revision/attachment/comment/embedded-image artifacts; import results). This is the gate every later
phase must keep green.

### Phase 1 — Thin the module (pure refactor, zero behaviour change)
1. Move `CaptureAsync` inventory logic into a WorkItems inventory orchestrator (extend the existing
   `IInventoryOrchestrator` / `IWorkItemDiscoveryService` path). Module `CaptureAsync` becomes a
   one-line delegate.
2. Move orchestrator composition (`WorkItemsImportCapabilityValidator`,
   `WorkItemsNodeReadinessOrchestrator`, `ImportPreparer`, default failure patterns) into
   `WorkItemsOrchestratorFactory` / DI. Module receives a ready `IWorkItemsOrchestrator`.
3. Remove the three tool fields from the module — inject where used (orchestrator/extensions).
4. Delete dead `ApplyImportReplayLevers` from the module.
5. Result: ctor collapses from ~30 params to ~4. All phases delegate.
- **Gate**: suite green; byte-identical artifacts.

### Phase 2 — Establish the extension seam (mirror Teams Phase 1)
1. Define `WorkItemExtensionContext : IExtensionContext` (entity = work item id; carries the current
   revision/folder-path, `Package`, `TargetEntityId` on import).
2. `WorkItemsModule` resolves `IEnumerable<IModuleExtension>`, applies default/mandatory/optional
   tiers, sorts by `Order`, passes `IReadOnlyList<IModuleExtension>` to the orchestrator.
3. Orchestrator invokes the extension list inside its existing per-work-item loop.
- No behaviour change yet (empty list). **Gate**: suite green.

### Phase 3 — Extract facets as extensions, one at a time (parity each)
For **Links → Attachments → Comments → EmbeddedImages** (simplest first):
- Create `{Facet}WorkItemExtension : IModuleExtension`, both directions, with its **own**
  `IOptions<{Facet}ExtensionOptions>` (`Enabled` + facet settings).
- Move capability logic out of `WorkItemsImportRuntime` / export path; remove its `if (extensions.X)`
  dispatch.
- Wire the tools it needs (EmbeddedImages → ref-rewrite/download tool; Links → none).
- **Parity gate after each** — artifacts and import results identical; one extension per commit.

### Phase 4 — Config: retire the god-object
- Replace `WorkItemsModuleExtensions` flag dispatch with each extension's own `IOptions<T>`.
- Retire `RevisionsEnabled` as an extension toggle (core; if a kill-switch is truly needed it is an
  orchestrator-level option).
- Add a package/config-schema **upgrader** if the on-disk shape changes (Constitution VII).

### Phase 5 — Conformance + docs
- Validate against `execution-contract.md` / `execution-model.md` (thin module, orchestrator owns
  loop, tools not held by module, extensions own config).
- Update WorkItems docs. Apply the tool-definition fix (§2) first.

---

## 5. Test handling (decided)

The unit tests construct `WorkItemsModule` **directly via its concrete constructor** and assert
inventory mechanics on it (`InventoryModuleFactory.CreateWorkItemsModule`, `WorkItemsModuleInventoryTests`,
`WorkItemsModuleImportTests`, `WorkItemsModulePrepareTests`). Both are exactly what the refactor
changes, so they **cannot** remain untouched.

Rule for this refactor:
- **System / parity tests (Simulated round-trips, artifact assertions): frozen and green** — the real
  safety net. If any needs changing, behaviour has changed and the refactor is wrong.
- **Unit tests coupled to the ctor / inventory-on-module: mechanical + relocation only** — fix
  `new WorkItemsModule(...)` call sites for the slim ctor, and **relocate** the inventory-mechanics
  assertions from the module's test to the orchestrator's test (same assertions, new owner). No
  assertion logic is weakened or deleted.

---

## 6. TDD workflow (test-first, parity-preserving)

This is a **refactor** — behaviour is preserved, not added — so classic red→green only applies
cleanly where something genuinely new is created (an extension, a new orchestrator method). For pure
relocation the "red" is a compile failure, not a behavioural one. Run two kinds of tests with
different jobs.

### Two test roles
1. **Frozen invariant tests** — the system / parity tests (Simulated round-trips, artifact
   assertions). They express the behaviour that must **not** change. **They never go red.** They are
   not "written first" — they already exist and stay green through every commit. A red parity test
   means behaviour changed and the step is wrong.
2. **Driver tests** — written or changed **first**, at the **new seam**, go red, then made green.
   These drive each move.

### The destination-first rule
Write the test against the **destination**, not the source. You no longer test "does the *module* do
X"; you write "does the *orchestrator* do X" or "does the *extension* do X" — watch it fail because
that thing does not exist yet — then move the code to make it pass — then delete the old path (the
frozen parity tests prove the deletion was safe).

### Per-extension recipe (the clean red→green→refactor — e.g. Links)
```
RED   1. Write {Facet}WorkItemExtensionTests: e.g. "given a work item with related links,
         ExportAsync writes the links artifact" — against a {Facet}WorkItemExtension class
         that does not exist yet.  → fails to compile / fails.
GREEN 2. Create {Facet}WorkItemExtension : IModuleExtension with its own IOptions<T>, moving
         the capability logic out of WorkItemsImportRuntime / the export path into it.  → green.
REFAC 3. Delete the `if (extensions.{Facet}Enabled)` dispatch; register the extension.
         All frozen parity tests stay green — proving the move preserved behaviour.
```
Repeat for **Links → Attachments → Comments → EmbeddedImages**, one extension per commit.

### Inventory-move recipe (Phase 1)
```
RED   1. Write WorkItemsInventoryOrchestratorTests asserting the inventory mechanics (drain the
         discovery stream, merge counts, write inventory.json) against an orchestrator method that
         does not exist yet.  → red.
GREEN 2. Move BuildEventStream + repo-count + merge logic out of WorkItemsModule.CaptureAsync into
         that orchestrator method.  → green.
REFAC 3. WorkItemsModule.CaptureAsync becomes a one-line delegate. Relocate the inventory assertions
         from the module test to the orchestrator test. Module test shrinks to "CaptureAsync
         delegates to the inventory orchestrator."
```

### The signature-change caveat (not runnable-red)
Slimming the ctor (~30 → ~4 params) cannot be a behavioural red: changing the test's
`new WorkItemsModule(...)` call first just stops the whole test project compiling, which blocks
running the green parity tests too. Therefore:
- **Keep signature changes atomic** — change the ctor and all its call sites in one commit. The "red"
  is compile-red, fixed immediately in the same step. This is test-first in spirit, not a runnable red.
- **Never leave the test project non-compiling across commits** — that loses the safety net.

### Ordering discipline (aligned with the corrected sequence in §9)
1. First slice = **Stage 1 (thin the module)** — Step 1.1 dead-code removal, then the inventory move,
   then composition-to-DI / ctor-slim — each as its own RED→GREEN→REFACTOR increment. Only after
   Stage 1 is green do Stages 2–4 (staged cursor pipeline, then Links → Attachments → Comments).
2. Each commit: driver test RED (shown failing) → minimal production GREEN → widen to full suite green
   → REFACTOR (consolidate behind the seam) staying green.
3. Never advance with a red parity test or a non-compiling project; never edit production before a
   pasted RED.

---

## 7. Risks / call-outs
- Highest risk: **Phase 1 (inventory move)** and **Phase 3 (flag-dispatch removal)** — both must be
  byte-parity behind the regression net.
- `WorkItemsImportRuntime` holds the real entanglement (resolution pipeline, node readiness, replay
  levers). It stays in the orchestrator; we relocate module-level concerns into it, we do not unpick it.
- The tool-definition fix (§2) is a prerequisite for the Phase 5 docs but blocks nothing earlier.

---

## 8. Seam-location correction (found at implementation kickoff)

**The original plan (§4) assumed `WorkItemsOrchestrator` owns the per-work-item loop, like
`TeamsOrchestrator`. Code review on kickoff proved this false.** Recording it so the plan is not
re-attempted on the wrong assumption.

Actual structure:
- `WorkItemsOrchestrator.ExportAsync` only *configures and delegates* to
  `_exportOrchestratorFactory.Create(...).ExportAsync(source, ct)`; `ImportAsync` delegates to
  `WorkItemsImportRuntime`. It does **not** loop entities.
- The per-entity loop **and** the capability dispatch live in **`RevisionFolderProcessor`** (import),
  interleaved with **`CursorStage` checkpoint staging**:
  - `ext.EmbeddedImages.Enabled` → field-value rewrite (~L253)
  - `ext.LinksEnabled` → `CursorStage.AppliedLinks` (~L278)
  - `ext.AttachmentsEnabled` → `CursorStage.UploadedAttachments` (~L296)
  - `ext.Comments.Enabled` → inline comments (~L317)

Consequences:
1. **Phase 2 (drop the extension list into the orchestrator loop) is inapplicable as written** — the
   loop is not in `WorkItemsOrchestrator`. The real extension seam is the **per-revision stage
   pipeline** in `RevisionFolderProcessor` (import) and the export equivalent.
2. **Phase 3 is higher-risk than Teams** — each facet is a **checkpointed resume stage**
   (`CursorStage`), not a free-standing `if`-block. Extracting it means restructuring the stage
   pipeline and its cursor/checkpoint semantics — the "do not unpick" zone.

**Resolved** (operator): the extension seam lands at the **revision-stage pipeline** in
`RevisionFolderProcessor` (the architecturally correct, forward-looking location). The cursor
*format* is preserved, not changed (see §9 "on-disk cursor format does NOT change"), so the
"touches checkpoint staging" risk is the *dispatch mechanism*, not the persisted contract.
Governance: paperwork before code — Stages 2+ gated on ADR + consent (see §10).

---

## 9. Corrected architecture — the sound, future-proof target (supersedes §4 Phase 2/3)

The kickoff discovery (§8) showed the real per-revision import pipeline is a **fixed, ordered,
checkpointed stage sequence** in `RevisionFolderProcessor`:

```
A CreatedOrUpdated     core   (create/update + id-map)
B AppliedFields        core   (fields + identity + node-translation; EmbeddedImages rewrite lives INSIDE here)
C AppliedLinks         Links capability        (own CursorStage)
D UploadedAttachments  Attachments capability  (own CursorStage)
  inline comments      Comments capability     (NO cursor stage)
E Completed            core
```

### The real problem
`CursorStage` is a **hard-coded enum** — the capability set is baked into the *checkpoint contract*.
Disabled capabilities still write their cursor to keep resume consistent. **This is the boolean-flag
anti-pattern at the checkpoint layer.** Adding a capability requires editing the enum and the resume
logic. Not future-proof.

### The keystone refactor
Generalise the pipeline so **stages are extension-keyed, not a fixed enum**:

```
stages = [ core:CreatedOrUpdated, core:AppliedFields, ...ordered extensions..., core:Completed ]
foreach revision:
  resumeAt = cursor.read(folder)              // last completed stage NAME
  foreach stage in stages:
    if already-done(stage.Name, resumeAt): continue
    await stage.Execute(ctx)                   // core step OR extension.ImportAsync
    cursor.write(folder, stage.Name)           // keyed by NAME, not a fixed enum value
```

Cursor keyed by **stage name** (the extension `Name`) is strictly more robust than the positional
enum — resume matches by identity; adding/removing/reordering a capability needs **no enum change and
no core edit**. `RevisionFolderProcessor` becomes the real **sub-orchestrator** owning the loop +
cursor; Links/Attachments/Comments become `IModuleExtension` stages with both directions.

### The on-disk cursor format does NOT change (no upgrader)
Restructuring *where capability logic lives* does not govern the persisted cursor format. The
generalised pipeline **reuses the existing marker strings** (`CreatedOrUpdated`, `AppliedFields`,
`AppliedLinks`, `UploadedAttachments`, `Completed`) as the stage `Name`s. Therefore:
- An in-flight migration paused on the old code **resumes unchanged** on the new code — same strings on disk.
- A *new* capability adds a *new* marker string. That is **additive and backward-compatible** (old
  packages simply lack it → "marker not present = run the stage"), which is the existing resume
  semantics — not a breaking change.
- **No version bump and no upgrader are required** for the pipeline generalisation (architecture-boundary
  Rule 9 is satisfied because the format is preserved, not changed). `CursorStage` may keep the existing
  constants as the canonical stage-name registry; the change is the *dispatch mechanism* (ordered list
  vs hard-coded switch), not the persisted contract.

### EmbeddedImages placement (decided)
EmbeddedImages is **not** a peer pipeline stage — it is a field-value rewrite *inside* the core
AppliedFields step (download + store + rewrite `<img>` refs). It is modelled as a **field-rewrite
contributor invoked by the core fields stage** (a FieldTransform-with-I/O), **not** a standalone
stage. This is the decided forward-looking placement: it keeps the AppliedFields cursor marker
meaning intact and avoids fragmenting field application across two stages.

### Corrected sequence
- **Stage 1 — Thin the module** (old Phase 1, unchanged): move inventory down, composition to
  DI/factory, drop tool fields, delete dead `ApplyImportReplayLevers`, slim ctor. Independent and
  safe; do first.
- **Stage 2 — Generalise the cursor/stage pipeline** (NEW keystone, replaces old Phase 2): convert the
  fixed `CursorStage` enum + inline stage `if`-blocks into an ordered, **name-keyed** stage pipeline
  in `RevisionFolderProcessor` (and the export-side folder writer). Behaviour-preserving; parity-gated.
  Resume now matches by stage name.
- **Stage 3 — Introduce the extension stage abstraction**: `WorkItemExtensionContext : IExtensionContext`;
  the pipeline runs an ordered `IReadOnlyList<IModuleExtension>` as its non-core stages. Wire existing
  capabilities as stages — still hard-coded, no behaviour change.
- **Stage 4 — Extract facets one per commit** (Links → Attachments → Comments), each moving its stage
  logic into `{Facet}WorkItemExtension` with its own `IOptions<T>`; cursor is now name-keyed so resume
  keeps working. Handle EmbeddedImages per the field-rewrite-contributor decision above.
- **Stage 5 — Retire god-object + `RevisionsEnabled`; per-extension config; docs; tool-definition fix.**

### Why this is the future-proof end-state
- New capability = new `IModuleExtension` stage. No enum edit, no core edit, no resume-logic edit.
- Checkpoint/resume is generic and identity-based, not positional.
- Core (create/update, fields, id-map) stays core; facets are pluggable; tools are consumed within
  stages. Module is thin; `RevisionFolderProcessor` is the sub-orchestrator that owns the loop+cursor.

---

## 10. Governance (mandatory before Stage 2+ code)

Per `.agents/20-guardrails/core/change-governance.md` and `change-classes.yaml`. **Paperwork before
code — always.**

### Change classification (all stages NOT STARTED)
| Stage | Class | Why | Required evidence (test-first) |
|---|---|---|---|
| Step 1.1 — delete dead `ApplyImportReplayLevers` | **A** | internal, behind surface, no entrypoint | failing test first, then green suite; Rule 30 remediation |
| Stage 1 — thin the module | **B** | behaviour at a canonical surface (orchestrator gains inventory ownership), no new public surface | RED→GREEN→REFACTOR per increment · behavioural tests across slices · 5-perspective evidence · doc update |
| Stages 2–5 — cursor pipeline + extension seam | **C** | adds/replaces a canonical surface (extension seam) + alters dispatch behind the cursor contract | operator consent · ADR · contract-compatibility tests · test-first RED→GREEN→REFACTOR trace |

**Every stage, including Stage 1 and Step 1.1, is failing-test-first.** Per
`test-first-workflow.md` line 47, no addition, fix, or behaviour change may begin from existing code
without a failing test — the RED test is written and run (and shown failing) **before** any production
edit. The ADR for the Class C scope is recorded and accepted:
[ADR 0019](../../docs/adr/0019-workitems-extension-seam-and-staged-cursor-pipeline.md).

### Capability Seam Decision (mandatory design evidence — capability-ethos rules)
- **Concern**: per-work-item capability application (links, attachments, comments) during import/export.
- **Canonical seam owner**: `RevisionFolderProcessor` (the per-revision sub-orchestrator) drives an
  ordered, named **stage pipeline**; each non-core stage is an `IModuleExtension`.
- **Canonical public surface**: `IModuleExtension` (single extension contract) over
  `WorkItemExtensionContext : IExtensionContext`. No `I{Domain}Extension`.
- **Allowed adapter/policy responsibilities**: enablement (`IsEnabled` from own `IOptions<T>`), order,
  capability gating, checkpoint interaction. Phase/slice policy lives in the extension, not in a new engine.
- **Prohibited parallel entry points**: no second per-revision dispatch path; extensions **call** the
  canonical seams (`IWorkItemTarget`, `IIdMapStore`, the tools) — they must **not** reimplement
  create/update, id-map, field, or translation engines (capability-ethos rules 2–3, 5).
- **Tools consumed (not reimplemented)**: `IIdentityTranslationTool`, `INodeTranslationTool`,
  `IFieldTransformTool`, embedded-image replay service.

### Mandatory remediation note (Rule 30)
Touching `RevisionFolderProcessor` / `WorkItemsImportRuntime` obligates remediating *known*
non-compliance in those classes within the touched scope as the first task — or an explicit
operator-approved bounded follow-up. To be enumerated in the ADR.

### Architecture-perspective evidence (required for Class B/C)
ADR must carry evidence across: Modular Monolith · Clean · Hexagonal · Vertical Slice · Screaming ·
Architecture Deepening (`.agents/20-guardrails/core/architecture-perspectives-ethos.md`).

---

## 11. Cross-references
- [execution-contract.md](../../.agents/10-contracts/specs/execution-contract.md) — canonical layer rules
- [execution-model.md](../../.agents/30-context/architecture/execution-model.md) — full layer model
- [contracts/IModuleExtension.md](contracts/IModuleExtension.md) — the single extension contract
- [contracts/BoardConfigTeamExtension.md](contracts/BoardConfigTeamExtension.md) — worked Teams example

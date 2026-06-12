# Spec Addendum — WorkItems Module Refactor (extension-model alignment)

**Status**: Context-capture / planning. No code or tests changed by this addendum.
**Parent feature**: [039-team-board-settings](spec.md) (extension architecture established here).
**Scope**: `WorkItemsModule` and `WorkItemsOrchestrator` (and the `WorkItems/**` runtime).
**Why it lives here**: 039 established the canonical extension model
([execution-contract](../../.agents/10-contracts/specs/execution-contract.md),
[execution-model](../../.agents/30-context/architecture/execution-model.md)). This addendum
records how WorkItems is brought into line with it, captured during design discussion so the
context is not lost. It is **not** an approval to edit Teams or WorkItems code.

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

### Ordering discipline
1. First real slice = **Phase 2 (seam) + one extension (Links)** as a single red→green→refactor, so a
   working extension path exists before mass-moving the rest.
2. Each commit: driver test red → production green → old path deleted → **all** parity tests green.
3. Never advance with a red parity test or a non-compiling project.

---

## 7. Risks / call-outs
- Highest risk: **Phase 1 (inventory move)** and **Phase 3 (flag-dispatch removal)** — both must be
  byte-parity behind the regression net.
- `WorkItemsImportRuntime` holds the real entanglement (resolution pipeline, node readiness, replay
  levers). It stays in the orchestrator; we relocate module-level concerns into it, we do not unpick it.
- The tool-definition fix (§2) is a prerequisite for the Phase 5 docs but blocks nothing earlier.

---

## 8. Cross-references
- [execution-contract.md](../../.agents/10-contracts/specs/execution-contract.md) — canonical layer rules
- [execution-model.md](../../.agents/30-context/architecture/execution-model.md) — full layer model
- [contracts/IModuleExtension.md](contracts/IModuleExtension.md) — the single extension contract
- [contracts/BoardConfigTeamExtension.md](contracts/BoardConfigTeamExtension.md) — worked Teams example

# Default spec update to architecture

## Purpose

This update captures:

1. the current WorkItems import architecture (as implemented), and
2. the target architecture you want, where generic cache/management is centralized in a wrapper service and connector-specific code only does actual find/get operations.

---

## Current architecture (as-is)

## Top-level composition

- `WorkItemsImportOrchestrator` composes target, strategy, idmap, node readiness, revision processor, then runs `WorkItemImportOrchestrator`.
- Source: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemsImportOrchestrator.cs`

## Streaming orchestration

- `WorkItemImportOrchestrator` currently:
  - initializes idmap,
  - calls `IWorkItemResolutionStrategy.SeedAsync`,
  - checks mapping integrity,
  - streams folders and delegates revision handling to `IRevisionFolderProcessor`.
- Source: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs`

## Revision processing

- `RevisionFolderProcessor` currently does:
  - FieldTransform + NodeTranslation,
  - resolve via idmap then strategy fallback,
  - create/update,
  - links/attachments/comments replay.
- Source: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs`

## Resolution strategies in use

1. `TargetFieldResolutionStrategy`
2. `TargetHyperlinkResolutionStrategy`

Both are currently ADO-specific implementations.

TFS factory currently returns `NullResolutionStrategy` (no equivalent find strategy path).

---

## What changes in target architecture

You want to keep `IWorkItemResolutionStrategy` as the strategy contract and introduce a generic wrapper that owns cache/management.

That wrapper is:

- `WorkItemResolutionService` (new)

This is the right model.

---

## Target responsibilities by class

## `WorkItemImportOrchestrator`

Owns:

- startup policy assembly (FieldTransform defaults, NodeTranslation defaults),
- asks `WorkItemResolutionService` to initialize/seed/rebuild cache as needed,
- deterministic revision streaming and dispatch to revision processor.

## `WorkItemImportRevisionProcessor`

Owns per-revision flow:

1. apply transforms + translation,
2. resolve via `WorkItemResolutionService`,
3. create when unresolved,
4. update when resolved,
5. replay links/attachments/comments and checkpoint updates.

## `WorkItemResolutionService` (new generic wrapper)

Owns all generic bits:

- idmap/cache lifecycle,
- cache hit/miss logic,
- rebuild policy decisions,
- stale mapping handling,
- orchestration around provenance writes and mapping persistence.

Delegates only strategy-specific lookup behavior to `IWorkItemResolutionStrategy`.

## `IWorkItemResolutionStrategy` (keep contract)

Stays focused on strategy behavior:

- how to find candidate target item(s),
- lookup behavior used by service-driven seeding,
- strategy-specific provenance semantics.

Important: `WorkItemResolutionService` owns seeding orchestration and generic cache management.

---

## Connector boundary rule (explicit)

ADO/TFS-specific classes should contain only connector query mechanics for find/get operations.

That means:

- ADO/TFS code handles SDK query execution and mapping raw results to common DTOs.
- Generic service handles cache/index/idmap lifecycle and decision logic.
- Orchestrators/processors call generic service, not connector-specific strategy internals.

---

## Current-to-target gap

| Concern | Current | Target |
|---|---|---|
| Generic cache wrapper | None | `WorkItemResolutionService` owns cache/management |
| Strategy scope | Mixed with cache lifecycle | Strategy narrowed to find/provenance behavior |
| TFS parity | `NullResolutionStrategy` | Real strategy path behind same wrapper contract |
| Orchestrator dependency | Direct strategy + idmap lifecycle in orchestrator | Orchestrator calls wrapper service |
| Revision dependency | Direct fallback to strategy | Revision processor resolves through wrapper |

---

## Field and node policy alignment

This remains unchanged from your requirement:

- field default-ignore policy must be centralized in one seam,
- NodeTranslation default first-level project mapping must be explicit,
- `System.TeamProject` may be default ignored where needed,
- `System.State` must not be globally excluded.

---

## Implementation shape (spec-level)

1. Add `WorkItemResolutionService`.
2. Move idmap init/seed/rebuild/integrity orchestration from `WorkItemImportOrchestrator` into this service.
3. Keep `IWorkItemResolutionStrategy` contract and existing strategy variants, but narrow service ownership boundaries.
4. Introduce TFS strategy implementation(s) for same find modes (field/hyperlink as feasible), replacing no-op path when strategy configured.
5. Replace direct strategy fallback usage in `RevisionFolderProcessor` with service call.
6. Rename/split revision processor to `WorkItemImportRevisionProcessor` to make role explicit.

---

## Verdict

Yes, this wrapper model is the correct direction:

- `WorkItemResolutionService` = generic cache/management brain.
- `IWorkItemResolutionStrategy` = strategy behavior.
- ADO/TFS classes = only connector-specific find/get mechanics.

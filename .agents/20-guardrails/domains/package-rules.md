# Package Rules

These rules are mandatory for all code that reads or writes the migration package.

## Package as Source of Truth

1. The package is the source of truth for all migration state. No migration state may be held in memory, databases, or external services as the authoritative store.
2. Runtime module/orchestrator code must access the package through `IPackageAccess`. `IArtefactStore` and `IStateStore` are lower-level boundary internals and must not be used as alternate caller-facing seams. Direct filesystem access remains forbidden in module code.

## Unified Package Boundary Contract

1. The canonical caller-facing runtime package boundary is `IPackageAccess`.
2. The canonical module-owned relative addressing contract beneath that boundary is `IPackageContentAddress`.
3. The typed package intent vocabulary is fixed to `PackageContentContext`, `PackageMetaContext`, `PackageLogContext`, `PackageContentKind`, `PackageMetaKind`, and `PackageLogStream` unless an ADR explicitly amends the contract.
4. Runtime modules, orchestrators, workers, checkpointing, phase tracking, continuation-token handling, and package-backed logging MUST use `IPackageAccess` for package-facing reads and writes. Rebuilding package routing directly over `IArtefactStore`, `IStateStore`, or raw package-relative strings in runtime flow code is forbidden.
5. `IPackageContentAddress` may supply only the module-owned relative suffix beneath the module root. Package prefixes, metadata locations, and run-log destinations are package-owned routing concerns.
6. The caller-facing boundary does not include delete. Cleanup, force-fresh removal, and compatibility maintenance remain lower-level concerns.
7. The package boundary contract is concrete and repository-binding. Renaming, replacing, widening, or bypassing these contracts without an explicit ADR amendment is a reject condition.
8. `.agents/30-context/domains/package-manager.md` is the frozen agent-facing mirror of this package boundary contract. Agents must not edit, compress, expand, reinterpret, or reorganize that file unless the task is an explicit package-contract amendment approved by the human and recorded in an ADR update in the same change.

## Path Determinism

1. Package paths must be deterministic and reproducible given the same source data. No random components in path generation.
2. All folders and files must follow the canonical naming conventions in `docs/package-format-reference.md`. Agent summaries in `.agents/30-context/domains/` must stay consistent with that reference.
3. WorkItems must use the `yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` folder structure to ensure lexicographic chronological ordering.

## Zip-Friendly Layout

1. No path component may exceed the filesystem path length limits on Windows (260 characters total without long path support).
2. No special characters in path components beyond those allowed by all major filesystems.

## Binary Access

1. Binary attachments must be streamed through package-boundary streaming paths (`IPackageAccess` caller surface with boundary-owned binary persistence). Buffering attachments in memory is forbidden.

## Hidden State

1. No migration state may be stored outside root `.migration/` (authoritative package state), project `/{org}/{project}/.migration/` (project-scoped cursors), or `.migration/runs/<runId>/` (run-scoped audit copies and logs). No hidden state in other locations.
2. No external databases or service-side state stores may be used as the authoritative resume mechanism.
3. Files under `.migration/runs/<runId>/` are audit artefacts only. Resume, phase gates, and orchestration MUST use root `.migration/` and project `/{org}/{project}/.migration/`, never run-scoped copies.
4. Current run-log stream filenames are fixed as `.migration/runs/<runId>/logs/progress.ndjson` and `.migration/runs/<runId>/logs/diagnostics.ndjson` unless an ADR explicitly changes the contract.

## Attachment Placement

1. Attachments must be stored beside the revision data in `attachments/` sub-folders within each revision directory. Global attachment dumping (all attachments in one folder) is forbidden.

## Enumeration

1. `IArtefactStore.EnumerateAsync()` returns results in lexicographic order. Do not sort the results in memory. Do not call `ToList()` or `ToArray()` on an `EnumerateAsync` result set.

## Reject Conditions

Reject any change that:

- introduces a new caller-facing runtime package abstraction instead of using `IPackageAccess`
- renames or replaces `IPackageAccess`, `IPackageContentAddress`, or the typed package context and enum vocabulary without an ADR amendment
- lets runtime flow code choose between `IArtefactStore` and `IStateStore` for routine package-facing operations
- builds package-root-relative paths directly in modules, orchestrators, workers, checkpointing, phase tracking, or package-backed logging
- treats `.migration/runs/<runId>/` copies as authoritative state for resume, gating, or orchestration
- changes the current run-log filenames or locations without updating the ADR and canonical package docs in the same change
- edits `.agents/30-context/domains/package-manager.md` without an explicit human-approved package-contract amendment task and matching ADR update

## Related

- [migration-rules.md](../domains/migration-rules.md) — migration behaviour rules
- [workitems-rules.md](../domains/workitems-rules.md) — WorkItems-specific layout rules
- [.agents/30-context/domains/migration-package-concept.md](../../30-context/domains/migration-package-concept.md) — package concept
- [docs/package-guide.md](../../../docs/package-guide.md) — operator package guide
- [docs/package-format-reference.md](../../../docs/package-format-reference.md) — canonical package format reference





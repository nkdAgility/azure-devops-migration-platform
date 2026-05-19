# ADR 0016 — Unified Package Access

## Status

Accepted

## Context

The package is already the repository's source of truth, and agent-only writes are already established architectural decisions. Even with those decisions in place, runtime code has still had too many opportunities to reason in raw path strings or to choose directly between `IArtefactStore` and `IStateStore` for package-facing operations.

That caused three recurring problems:

- path ownership drift between modules, orchestrators, workers, and logging infrastructure
- inconsistent handling of authoritative package state versus run-scoped audit copies
- documentation drift because the intended package boundary lived partly in agent context, partly in contributor docs, and partly in code

The runtime now has a concrete caller-facing package boundary in place. This decision records that boundary as the canonical model so future work does not regress to direct path assembly or store-by-store routing in runtime flow code.

## Decision

The package boundary is unified under `IPackageAccess`.

`IPackageAccess` is the canonical caller-facing package boundary for runtime package access. Runtime code that reads or writes package content, package metadata, checkpoint state, phase markers, continuation tokens, or run-log streams must use `IPackageAccess`.

The package boundary uses these implemented contract names:

- `IPackageAccess`
- `IPackageContentAddress`
- `PackageContentContext`
- `PackageMetaContext`
- `PackageLogContext`
- `PackagePayload`
- `PackageMetaPayload`
- `PackageLogPayload`
- `PackageContentKind`
- `PackageMetaKind`
- `PackageLogStream`

The boundary owns:

- package-controlled routing
- separation between content, metadata, and append-only run logs
- authoritative-versus-run-audit write behavior
- the mapping from typed package intent to `IArtefactStore` and `IStateStore`

The underlying stores remain persistence primitives beneath that boundary. They are not the primary runtime caller API.

The boundary does not include delete. Cleanup, force-fresh removal, and other maintenance behavior remain dedicated lower-level concerns rather than broadening the routine runtime package contract.

## Alternatives Considered

### Keep `IArtefactStore` and `IStateStore` as the only public runtime package interfaces

- Summary: Let each runtime caller choose the store and assemble the path it needs.
- Pros: Minimal abstraction and no additional routing layer.
- Cons: Reintroduces path ownership drift, duplicates authoritative-versus-audit decisions, and makes package-boundary rules hard to enforce consistently.

### Introduce a package façade but keep raw path strings as the caller contract

- Summary: Add a façade but still let callers pass package-root-relative strings.
- Pros: Easier migration from the old model.
- Cons: Does not solve the actual design problem because callers still own path selection and package semantics leak outward.

### Use a unified package boundary with typed contexts

- Summary: Callers express package intent through `IPackageAccess` plus typed contexts and module-owned relative addresses where needed.
- Pros: Centralizes routing, preserves package semantics, reduces drift, and matches the runtime direction already adopted in the codebase.
- Cons: Requires boundary vocabulary in both code and docs, and requires maintenance work when package semantics expand.

## Consequences

- Runtime modules, orchestrators, workers, checkpointing, phase tracking, and package-backed logging should treat `IPackageAccess` as the normal package-facing API.
- `IArtefactStore` and `IStateStore` remain subordinate persistence contracts beneath the boundary.
- Package-controlled routing remains centralized rather than being reconstructed in each caller.
- Run logs are modeled as append-only package log streams, not ordinary metadata.
- Contributor-facing documentation must describe the package boundary in docs rather than relying on agent context as the canonical human reference.
- Agent context should summarize the package boundary and link to the docs-owned reference rather than duplicating full contract sketches.

## Related Documents

- [0002-filesystem-package-as-source-of-truth.md](0002-filesystem-package-as-source-of-truth.md)
- [0005-agent-only-package-write-access.md](0005-agent-only-package-write-access.md)
- [0008-configuration-travels-in-package.md](0008-configuration-travels-in-package.md)
- [../package-boundary-reference.md](../package-boundary-reference.md)
- [../package-format-reference.md](../package-format-reference.md)
- [../../.agents/30-context/domains/package-manager.md](../../.agents/30-context/domains/package-manager.md)

# Package Boundary Reference

Audience: Contributors touching agent runtime internals.

This is a lightweight contributor overview of the runtime package boundary. The binding rules are in [.agents/guardrails/package-rules.md](../.agents/guardrails/package-rules.md), [.agents/guardrails/architecture-boundaries.md](../.agents/guardrails/architecture-boundaries.md), and [.agents/guardrails/data-sovereignty-rules.md](../.agents/guardrails/data-sovereignty-rules.md). The architectural decision is recorded in [adr/0016-unified-package-access.md](adr/0016-unified-package-access.md). The exact package layout remains in [package-format-reference.md](package-format-reference.md).

## What Contributors Need To Know

If you are changing runtime package access code, the contract is intentionally narrow:

- `IPackageAccess` is the caller-facing runtime boundary.
- `IPackageContentAddress` supplies only the module-owned relative suffix beneath module roots.
- `IArtefactStore` and `IStateStore` remain persistence primitives beneath that boundary.
- Root `.migration/` and project `/{org}/{project}/.migration/` are authoritative state.
- `.migration/runs/<runId>/` is audit-only.
- Current run-log streams are `progress.ndjson` and `diagnostics.ndjson` under `.migration/runs/<runId>/logs/`.

If you need the enforceable version of those rules, read the guardrails, not this page.

## Why The Boundary Exists

The boundary keeps runtime callers from owning package layout decisions. It centralizes:

- package-controlled routing
- authoritative versus run-audit writes
- append-only run-log routing

That lets modules, orchestrators, workers, and runtime services express package intent without rebuilding path logic.

## What Stays Below The Boundary

These concerns remain implementation detail beneath the caller-facing contract:

- concrete artefact-store implementation choice
- low-level state-store details
- delete and cleanup behavior
- compatibility fallback reads for legacy layouts

## Related Documents

- [package-format-reference.md](package-format-reference.md)
- [package-guide.md](package-guide.md)
- [module-development-guide.md](module-development-guide.md)
- [adr/0002-filesystem-package-as-source-of-truth.md](adr/0002-filesystem-package-as-source-of-truth.md)
- [adr/0005-agent-only-package-write-access.md](adr/0005-agent-only-package-write-access.md)
- [adr/0008-configuration-travels-in-package.md](adr/0008-configuration-travels-in-package.md)

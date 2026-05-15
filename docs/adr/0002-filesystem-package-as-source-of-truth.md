# ADR 0002 — Filesystem Package as Source of Truth

## Status

Accepted

## Context

The migration process spans multiple phases (Inventory, Export, Prepare, Import, Validate) which may run at different times on different machines. A shared authoritative store is needed for all migration state.

## Decision

The filesystem package (a directory tree rooted at `Package.WorkingDirectory`) is the single source of truth for all migration state. All modules read from and write to the package exclusively through `IArtefactStore`. No migration state is held in databases, memory, or external services.

## Alternatives Considered

**Control Plane database**: Centralises state but creates a remote dependency for migration execution. The agent cannot resume offline.

**In-memory state**: Fast but not resumable and not auditable.

## Consequences

- Migrations are resumable from the package alone.
- The package can be zipped and transferred between environments.
- All module output is inspectable by operators.
- `IArtefactStore` is the only permitted persistence interface for module code.
- Package layout must be deterministic and stable.

## Related

- [ADR 0001](0001-source-files-target.md) — Source→Files→Target model
- [.agents/30-context/domains/migration-package-concept.md](../../.agents/30-context/domains/migration-package-concept.md) — package structure details
- [.agents/20-guardrails/core/architecture-boundaries.md](../../.agents/20-guardrails/core/architecture-boundaries.md) — enforced constraints

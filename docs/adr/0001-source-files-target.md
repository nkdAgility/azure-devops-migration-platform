# ADR 0001 — Source → Files → Target Architecture

## Status

Accepted

## Context

A direct Source → Target migration couples the export and import phases, makes the process non-resumable, prevents independent validation, and requires both systems to be simultaneously available.

## Decision

All migration data flows through an intermediate filesystem package:

```
Source → Files (package) → Target
```

Export writes to the package. Import reads from the package. The package is the source of truth.

## Alternatives Considered

**Direct Source → Target**: Simpler to implement initially, but not resumable, not auditable, requires simultaneous connectivity to both systems, and cannot support partial re-runs.

**Database as intermediate store**: More queryable, but not portable, not human-readable, and adds infrastructure dependency.

## Consequences

- The package is required for every migration.
- Export and Import can run at different times and on different machines.
- The package is auditable and inspectable by operators.
- Resuming a migration requires only the package and target credentials.
- The package format must be stable and versioned.

## Related

- [ADR 0002](0002-filesystem-package-as-source-of-truth.md) — package structure
- [docs/architecture.md](../architecture.md) — full architectural description
- [.agents/guardrails/architecture-boundaries.md](../../.agents/guardrails/architecture-boundaries.md) — enforced constraints
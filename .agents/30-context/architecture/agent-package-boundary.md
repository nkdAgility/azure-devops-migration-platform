# agent_package_boundary — Typed Package Boundary System

Compressed subsystem summary for targeted architecture work. Canonical human-facing reference: [../../../docs/package-boundary-reference.md](../../../docs/package-boundary-reference.md). Architectural decision: [../../../docs/adr/0016-unified-package-access.md](../../../docs/adr/0016-unified-package-access.md).

- Tag: `agent_package_boundary`
- Responsibility: Own typed package access, authoritative metadata routing, run-audit mirroring, and run-log routing without exposing package paths to runtime callers.

## Core Role

This subsystem prevents modules, orchestrators, and runtime services from deciding package layout themselves.

Callers provide typed package intent:

- content via `PackageContentContext`
- metadata via `PackageMetaContext`
- append-only run logs via `PackageLogContext`

The boundary then resolves the authoritative destination and any run-scoped audit or log target.

## Implemented Boundary Vocabulary

- `IPackageAccess`
- `IPackageContentAddress`
- `PackageContentContext`
- `PackageMetaContext`
- `PackageLogContext`
- `PackageContentKind`
- `PackageMetaKind`
- `PackageLogStream`
- `PackagePathRouter`

## Stable Subsystem Rules

- Runtime callers should use `IPackageAccess` for package-facing operations.
- `IPackageContentAddress` supplies only the module-owned relative suffix.
- The boundary owns package prefixes and package-controlled routing.
- Authoritative package state stays in root `.migration/` and project `/{org}/{project}/.migration/`.
- Run-scoped files under `.migration/runs/<runId>/` stay audit-only.
- Run-log streams are append-only and currently route to `progress.ndjson` and `diagnostics.ndjson`.
- Delete remains outside the normal caller-facing boundary.

## Integration Points

- package config loading
- execution-plan persistence
- phase tracking
- checkpoint cursor and continuation-token routing
- package-backed progress logging
- package-backed diagnostics logging

## Read Next

- [../../../docs/package-boundary-reference.md](../../../docs/package-boundary-reference.md)
- [../../../docs/adr/0016-unified-package-access.md](../../../docs/adr/0016-unified-package-access.md)
- [agent-package-persistence.md](agent-package-persistence.md)
- [agent-checkpoint-phase-tracking.md](agent-checkpoint-phase-tracking.md)
- [agent-runtime-context.md](agent-runtime-context.md)





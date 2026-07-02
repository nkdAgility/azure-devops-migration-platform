# ADR 0025 — Storage-Neutral Error Contract for Package Meta Reset

## Status

Accepted

Executes architecture-audit item **HX-H1** (analysis/archcheck/report.md) as a Class C change under explicit operator consent, with contract compatibility tests and a test-first trace as required by `.agents/20-guardrails/core/change-governance.md`.

## Context

`IPackageAccess` is the storage-neutral package boundary: callers address typed package intents and must not know which backing store serves them. Yet `JobAgentWorker` caught `System.IO.FileNotFoundException` around its four `ResetMetaAsync` call sites (ForceFresh cursor/plan/marker deletion), coupling the use-case ring to the filesystem adapter's exception surface. A future non-filesystem store (blob, zip, database) would throw different types and the catches would silently stop matching.

## Decision

The `ResetMetaAsync` error contract is owned by the abstraction:

1. **`PackageMetaNotFoundException`** is added to `DevOpsMigrationPlatform.Abstractions.Storage`, mirroring the existing `PackageConfigNotFoundException` pattern. It carries the addressed `PackageMetaKind` and optionally the translated storage-specific inner exception.
2. **Contract semantics** (documented on `IPackageAccess.ResetMetaAsync`): implementations either treat a missing meta artefact as an idempotent no-op or throw `PackageMetaNotFoundException`. Filesystem exception types must never escape the seam.
3. **FileSystem adapter** (`ActivePackageAccess.ResetMetaAsync`) translates `FileNotFoundException`/`DirectoryNotFoundException` into `PackageMetaNotFoundException`. (Its `FileSystemStateStore.DeleteAsync` is already delete-if-exists, so the translation is the defensive boundary for observability wrappers and future store implementations.)
4. **Consumers** — the four `JobAgentWorker` catch sites now catch `PackageMetaNotFoundException`. No `IPackageAccess` consumer catches `System.IO` types from the package boundary.

## Contract Tests (RED → GREEN)

`PackageMetaResetErrorContractTests` (tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/):

- pins that `PackageMetaNotFoundException` is owned by the Abstractions.Storage contract assembly;
- pins that `ResetMetaAsync` on a missing meta artefact leaks no `System.IO` exception through the FileSystem adapter (idempotent-or-neutral);
- source-scan guard: `JobAgentWorker` contains no `catch (FileNotFoundException)`.

RED: 2/3 failing before the change (exception type absent; worker catching the filesystem type). GREEN after.

## Consequences

- The package boundary's blackbox error contract is complete: absence is reported neutrally, and new storage adapters implement the same translation obligation.
- `ResetMetaAsync` remains idempotent in the FileSystem adapter — existing behaviour is unchanged; only the exception *type* contract moved to the abstraction.

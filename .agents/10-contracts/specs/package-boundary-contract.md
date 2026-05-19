# Package Boundary Contract

Canonical contract for typed package access and boundary-owned routing.

## Contract Surface

- `IPackageAccess`
- `IPackageContentAddress`
- `PackageContentContext`
- `PackageMetaContext`
- `PackageLogContext`
- `PackageContentKind`
- `PackageMetaKind`
- `PackageLogStream`
- `PackagePathRouter`

## Required Semantics

1. Runtime callers use `IPackageAccess` for package-facing operations.
2. `IPackageContentAddress` supplies only module-owned relative suffixes.
3. Package prefixes and metadata/log routing are boundary-owned.
4. Authoritative state is root `.migration/` and project `/{org}/{project}/.migration/`.
5. `.migration/runs/<runId>/` is audit-only and not resume authority.
6. Run-log streams are append-only and route to `progress.ndjson` and `diagnostics.ndjson`.
7. Delete is outside the normal caller-facing boundary.

## Governance (No Bypass)

1. Any change to any interface in this contract surface is a Class C surface/contract change.
2. Class C changes require explicit operator approval under `.agents/10-contracts/consent-policy.yaml`.
3. No bypass path is allowed: without operator approval evidence, the change is blocked.

## Integration Points

- package config loading
- execution-plan persistence
- phase tracking
- checkpoint cursor and continuation-token routing
- package-backed progress logging
- package-backed diagnostics logging


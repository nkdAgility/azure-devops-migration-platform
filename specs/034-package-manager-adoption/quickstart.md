# Quickstart: Package Manager Adoption

## Goal

Implement and adopt the explicit package boundary so runtime package path ownership moves from callers to `IPackageAccess`, without changing migration semantics.

## Steps

1. Confirm the package contract surface is `IPackageAccess`, `IPackageContentAddress`, `PackageContentContext`, and `PackageContentKind`, not the older `IPackage` and path-based content model.
2. Verify `PackagePathRouter` and `ActivePackageAccess` preserve package-owned prefixes, require caller-supplied module-relative suffixes where applicable, and reject absolute or escaping addresses.
3. Migrate or verify package-facing runtime state paths (plan, checkpoints, phase markers) use `IPackageAccess` or the explicit legacy shim only.
4. Verify `PackageMigrationConfigLoader` loads `migration-config.json` through mandatory `IPackageAccess` usage with no direct package-store fallback.
5. Verify progress and diagnostics log streams use the package boundary append APIs.
6. Audit remaining `LegacyPackagePathShim` call sites and record them as transitional debt rather than target architecture.
7. Run full build and full test validation.
8. Run at least one representative scenario from `.vscode/launch.json` and capture observable output for package-boundary routing, telemetry, and resume behavior.
9. Re-validate route safety, connector parity, and resume or phase no-regression behavior.

## Verification Commands

```powershell
dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo
dotnet test DevOpsMigrationPlatform.slnx --nologo
```

## Expected Outcome

- Runtime package-facing reads, writes, and appends are routed via `IPackageAccess`.
- Canonical package structure and semantics are unchanged.
- Resume and phase gating behavior remains deterministic.
- Progress/diagnostics logs still appear in run-scoped package logs.
- At least one launched scenario confirms the operator-visible output still matches the updated package-boundary contract.
- Unsafe absolute or escaping module-relative addresses are rejected before any package write occurs.

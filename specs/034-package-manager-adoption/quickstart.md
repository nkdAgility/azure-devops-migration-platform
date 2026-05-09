# Quickstart: Package Manager Adoption

## Goal

Implement and adopt the package boundary so runtime package path ownership moves from callers to `IPackage`, without changing migration semantics.

## Steps

1. Implement/confirm typed package contracts in abstractions.
2. Implement package router/boundary in infrastructure over existing stores.
3. Migrate orchestration state paths (plan, checkpoints, phase markers) to boundary APIs.
4. Migrate package config persistence/read to boundary APIs.
5. Migrate progress/diagnostic sink appends to `AppendLogAsync`.
6. Migrate remaining module/orchestrator direct path composition seams.
7. Run full build and full test suite.
8. Re-validate connector parity and resume/phase no-regression behavior.

## Verification Commands

```powershell
dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo
dotnet test DevOpsMigrationPlatform.slnx --nologo
```

## Expected Outcome

- Runtime package writes are routed via package boundary calls.
- Canonical package structure and semantics are unchanged.
- Resume and phase gating behavior remains deterministic.
- Progress/diagnostics logs still appear in run-scoped package logs.

# Feature Assessment: exclusive-package-lock

**Family:** package-lock-exclusive-package-lock
**Feature file:** features/platform/package-lock/exclusive-package-lock.feature

## Scenarios
1. Second agent is hard-bounced when live lock exists
2. Stale lock is replaced and agent proceeds normally
3. Lock is released when job completes

## Wiring state
Wired — Reqnroll step bindings existed in:
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ExclusivePackageLockSteps.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ExclusivePackageLockContext.cs

## Source types under test
- `DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem.ActivePackageAccess` (AcquireLockAsync, PackageLockHandle.Dispose)
- `DevOpsMigrationPlatform.Abstractions.Storage.PackageLockConflictException`
- `DevOpsMigrationPlatform.Abstractions.ControlPlaneApi.IControlPlaneAgentClient`

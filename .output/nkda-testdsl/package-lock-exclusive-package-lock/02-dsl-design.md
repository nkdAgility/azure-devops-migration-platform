# DSL Design: ExclusivePackageLockTests

New MSTest class: `ExclusivePackageLockTests`
Location: tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ExclusivePackageLockTests.cs

## Design decisions
- Re-used the setup helpers from ExclusivePackageLockContext inline (no separate context class needed)
- DeterministicGuid helper produces stable agent GUIDs from string IDs
- Each test sets up its own temp directory via [TestInitialize]/[TestCleanup]
- MockControlPlane uses Moq with MockBehavior.Strict

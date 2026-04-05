# Architecture Discrepancies

**Feature**: Fix CLI Architecture and Add Command Testing
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### CLI Architecture Implementation Pattern
- **Source doc**: `docs/cli.md` (Line: "CLI is the operator's entry point to the migration platform. It is a **thin shell**")
- **Section**: Purpose / Architecture
- **Issue**: Current Program.cs implementation violates the "thin shell" principle by containing 100+ lines of DI container setup, service registration, telemetry configuration, and infrastructure concerns. The spec requires a host builder pattern where commands manage their own hosting lifecycle.
- **Suggested update**: Update docs/cli.md to include a detailed "Implementation Pattern" section describing the MigrationPlatformHost pattern, CommandBase inheritance model, and proper separation between Program.cs (bootstrapping only) and host builder (infrastructure setup).

### Command Testing Requirements
- **Source doc**: `docs/cli.md` 
- **Section**: Commands table
- **Issue**: Documentation describes CLI commands but does not specify testing requirements or patterns. The spec introduces comprehensive command testing using Spectre.Console.Cli.Testing which should be documented as a standard practice.
- **Suggested update**: Add a "Testing" section to docs/cli.md describing the CommandAppTester pattern, automated command validation requirements, and integration test expectations for all CLI commands.

### DI Container Management Responsibility
- **Source doc**: `.agents/guardrails/system-architecture.md` (Rule 16)
- **Section**: "The CLI must not contain migration logic"  
- **Issue**: While the rule correctly prohibits migration logic in CLI, it doesn't clarify the proper pattern for DI container management. The current Program.cs implementation aggregates infrastructure concerns that should be in a dedicated host builder.
- **Suggested update**: Expand Rule 16 to specify that CLI infrastructure setup should follow the host builder pattern, with Program.cs limited to bootstrapping and commands managing their hosting lifecycle through dependency injection.

### Configuration Flow Pattern
- **Source doc**: `docs/cli.md`
- **Section**: Commands description
- **Issue**: Documentation doesn't describe how --config parameter should flow through the system architecture. The spec identifies specific flow requirements through host builder before DI container creation.
- **Suggested update**: Add a "Configuration Flow" section to docs/cli.md describing the parameter extraction pattern, host builder integration, and IOptions binding requirements.
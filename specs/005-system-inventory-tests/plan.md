# Implementation Plan: System Test Framework for Inventory Command

**Branch**: `005-system-inventory-tests` | **Date**: April 6, 2026 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-system-inventory-tests/spec.md`

## Summary

Add system test framework to validate inventory command functionality against live Azure DevOps organizations. Primary requirement: Enable developers to run `dotnet test --filter "TestCategory=SystemTest"` for integration validation while maintaining security through environment variable configuration. Technical approach centers on extending existing MSTest infrastructure with `[TestCategory("SystemTest")]` tests that gracefully handle missing configuration and provide comprehensive documentation for local dev and CI/CD setup.

## Technical Context

**Language/Version**: C# 10+, .NET 10  
**Primary Dependencies**: MSTest, Microsoft.VisualStudio.TestTools.UnitTesting, existing DevOpsMigrationPlatform abstractions  
**Storage**: Temporary output directories (created/cleaned during tests), file system via existing `TokenResolver.Resolve()` pattern  
**Testing**: MSTest with `[TestCategory("SystemTest")]` for test categorization, `Assert.Inconclusive()` for graceful environment handling  
**Target Platform**: Cross-platform (.NET 10), supports both local development (Windows/macOS/Linux) and GitHub Actions CI  
**Project Type**: Test extension to existing CLI test project `DevOpsMigrationPlatform.CLI.Migration.Tests`  
**Performance Goals**: System test execution under 30 seconds for basic validation scenarios  
**Constraints**: Must not expose credentials in test output, must gracefully handle network issues and service unavailability  
**Scale/Scope**: Single system test initially, documentation for 5-10 common troubleshooting scenarios, support for multiple Azure DevOps organizations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirm that ALL files in
> `/.agents/20-guardrails/`, ALL files in `/.agents/30-context/`, and relevant `/docs/` files
> have been read. Skipping either `.agents/` subdirectory is a constitution violation.

✅ **Architecture files read**: `docs/cli-guide.md`, `.agents/20-guardrails/core/architecture-boundaries.md`, `.agents/20-guardrails/workflow/testing-rules.md`, `.agents/20-guardrails/workflow/acceptance-test-format.md`

- [x] **Package-First (I):** ✅ N/A - This is test code that validates CLI behavior, does not involve migration package operations
- [x] **Streaming (II):** ✅ N/A - This is test code, does not involve WorkItems processing or import logic
- [x] **WorkItems Layout (III):** ✅ N/A - This is test code, does not manipulate WorkItems folder structure
- [x] **Checkpointing (IV):** ✅ N/A - This is test code, does not involve module checkpointing
- [x] **Module Isolation (V):** ✅ System tests will use existing `IArtefactStore`/`IStateStore` abstractions for test output cleanup. Tests validate CLI configuration without direct store access.
- [x] **Separation of Planes (VI):** ✅ System tests validate CLI command orchestration only. No migration logic in test code. Tests verify CLI builds correct configuration and validates credentials, not migration execution.
- [x] **Determinism (VII):** ✅ System tests use environment variables for reproducible configuration. Test outcomes are deterministic given same environment setup.
- [x] **ATDD-First (VIII):** ✅ All 3 user stories in spec.md have Given/When/Then acceptance scenarios. Each scenario maps to independent test methods.
- [x] **SOLID & DI (IX):** ✅ System tests use existing DI patterns from CLI test infrastructure. Configuration via `IOptions<InventoryOptions>` pattern. Tests access services through `IServiceProvider`.

## Project Structure

### Documentation (this feature)

```text
specs/005-system-inventory-tests/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
tests/DevOpsMigrationPlatform.CLI.Migration.Tests/
├── Commands/
│   ├── Discovery/
│   │   └── InventoryCommandTests.cs      # ← EXTEND: Add system tests here
│   ├── TfsExportCommandTests.cs
│   └── ...
├── TestUtilities/
│   └── ...                               # ← POSSIBLE: Extend with system test helpers
└── DevOpsMigrationPlatform.CLI.Migration.Tests.csproj

docs/
└── contributors.md                       # ← NEW: Comprehensive system test documentation

# Repository-level support files
.github/
└── workflows/                            # ← EXTEND: Update CI to include system tests

# Existing infrastructure (no changes)
src/DevOpsMigrationPlatform.Abstractions/
├── Utilities/
│   └── TokenResolver.cs                  # ← USE: Existing token resolution
└── Options/
    └── InventoryOptions.cs               # ← USE: Existing configuration classes
```

**Structure Decision**: Extend existing test project `DevOpsMigrationPlatform.CLI.Migration.Tests` with system test methods in the current `InventoryCommandTests.cs` class. Add comprehensive documentation to new `docs/contributor-guide.md`. This minimizes structural changes while providing maximum value - leverages existing test infrastructure, follows established patterns, and centralizes system test documentation.

---

# Phase 0: Outline & Research

## Research Tasks

Based on the technical context requirements, the following research tasks will resolve implementation approach:

1. **MSTest System Test Patterns**: Research `[TestCategory("SystemTest")]` usage patterns, test filtering approaches, and best practices for environment-dependent tests
2. **Azure DevOps REST API Testing**: Research authentication patterns, rate limiting handling, and error scenarios for testing against live Azure DevOps organizations
3. **Environment Variable Security**: Research secure patterns for loading credentials in tests, avoiding exposing secrets in test output and CI logs
4. **CI/CD Integration Patterns**: Research GitHub Actions integration for system tests, repository secrets management, and conditional test execution
5. **Test Infrastructure Patterns**: Research existing CLI test patterns in the codebase, dependency injection for tests, and temporary resource cleanup
6. **Documentation and Troubleshooting**: Research comprehensive documentation patterns for system test setup, common error scenarios, and developer onboarding

## Research Findings

✅ **Phase 0 Complete**: All research tasks completed and consolidated in [research.md](research.md)

Key decisions resolved:
- **Test Framework**: MSTest with `[TestCategory("SystemTest")]` filtering
- **Authentication**: Azure DevOps PAT via existing `TokenResolver.Resolve()` pattern  
- **Environment Config**: `AZDEVOPS_SYSTEM_TEST_ORG` and `AZDEVOPS_SYSTEM_TEST_PAT` environment variables
- **CI Integration**: GitHub Actions with repository secrets and conditional execution
- **Error Handling**: `Assert.Inconclusive()` for environment issues, detailed error contexts
- **Performance**: 30-second maximum execution, lightweight API validation calls

---

# Phase 1: Design & Contracts

✅ **Phase 1 Complete**: Design artifacts generated

## Data Model
Extracted test execution entities: SystemTestConfiguration, SystemTestContext, TestArtifact, ValidationResult. See [data-model.md](data-model.md)

## Interface Contracts  
Defined test interface contract and documentation contract for system test framework integration. See [contracts/](contracts/)

## Quick Start Guide
Created 30-second setup guide for developers. See [quickstart.md](quickstart.md)

## Agent Context Update
✅ Updated GitHub Copilot context file with:
- Added language: C# 10+, .NET 10
- Added framework: MSTest, Microsoft.VisualStudio.TestTools.UnitTesting  
- Added data patterns: Environment variable configuration, temporary test artifacts

## Constitution Check (Re-evaluation Post-Design)

*GATE: Re-checking constitutional compliance after Phase 1 design artifacts*

✅ **Architecture files confirmed**: All design decisions align with existing patterns

- [x] **Package-First (I):** ✅ N/A - System tests validate CLI configuration, no package operations involved
- [x] **Streaming (II):** ✅ N/A - System tests do not process WorkItems or perform streaming operations  
- [x] **WorkItems Layout (III):** ✅ N/A - System tests do not manipulate WorkItems folder structure
- [x] **Checkpointing (IV):** ✅ N/A - System tests are stateless, no checkpoint management required
- [x] **Module Isolation (V):** ✅ Design maintains isolation - tests use existing abstractions, temporary artifact cleanup via established patterns
- [x] **Separation of Planes (VI):** ✅ Design preserves separation - system tests validate CLI orchestration behavior only, no migration logic in test code
- [x] **Determinism (VII):** ✅ Design ensures determinism - environment-based configuration, predictable test outcomes, documented error handling
- [x] **ATDD-First (VIII):** ✅ Design supports ATDD - each acceptance scenario maps to independent test methods with clear Given/When/Then structure
- [x] **SOLID & DI (IX):** ✅ Design follows DI patterns - leverages existing `IOptions<InventoryOptions>` configuration, `IServiceProvider` access, constructor injection for test dependencies

**Post-Design Assessment**: All constitutional requirements remain satisfied. The system test framework design maintains architectural integrity while adding valuable live system validation capabilities.

---

# Phase 2: Planning Summary

## Ready for Implementation

**Branch**: `005-system-inventory-tests`  
**Implementation Plan**: Complete and validated  
**Generated Artifacts**:
- ✅ [research.md](research.md) - Comprehensive technology decision rationale
- ✅ [data-model.md](data-model.md) - Test execution entity model  
- ✅ [contracts/test-interface.md](contracts/test-interface.md) - System test integration contract
- ✅ [contracts/documentation-contract.md](contracts/documentation-contract.md) - Contributors guide requirements
- ✅ [quickstart.md](quickstart.md) - 30-second developer setup guide

## Next Phase Command
Execute: `/speckit.tasks` - Generate actionable task breakdown for implementation

## Quality Gates Passed
- ✅ Constitutional compliance verified (pre and post-design)
- ✅ Architecture alignment confirmed  
- ✅ Technology decisions researched and documented
- ✅ Interface contracts defined
- ✅ Agent context updated
- ✅ All Phase 0-1 deliverables complete

## Implementation Readiness
The system test framework design is ready for task generation and implementation. All research questions resolved, design artifacts complete, and architectural compliance verified.

---

## Current status

Reconciled against repository truth: this plan is stale in key execution details. The repository no longer has `Commands/Discovery/InventoryCommandTests.cs`; live and simulated system coverage is now centered on `queue` command test classes and split documentation guides.

## Remaining incomplete work (IDs)

T001, T013, T014, T015, T017, T022, T023, T024, T026, T027, T035, T036, T038, T040, T041, T042.

## Completed because superseded (IDs + source)

- T018 superseded by `CliRunner` output-folder cleanup pattern.
- T020 superseded by `.github/workflows/main.yml` system-test stages.
- T021 superseded by queue-command-centric live system tests.
- T025, T028-T034, T039 superseded by `docs/contributor-guide.md` + `docs/testing-guide.md` + `docs/live-system-testing-guide.md`.
- T037 superseded by existing diagnostics capture and documentation.

## Contradictions and reconciliation

1. **Path contradiction**: planned InventoryCommand test file path no longer exists; reconciled to queue-command test architecture.
2. **Doc contradiction**: planned `docs/contributors.md` target is stale; reconciled to `docs/contributor-guide.md` and testing guides.
3. **Behavior contradiction**: plan/spec assumed inconclusive environment gating, but current live-test policy discourages committed self-skipping tests; reconciled as unresolved for this spec and tracked as incomplete tasks.

## Verification evidence

- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs`
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/MigrationExportCommandTests.cs`
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/PrepareCommandTests.cs`
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestConfiguration.cs`
- `.github/workflows/main.yml`
- `docs/contributor-guide.md`
- `docs/testing-guide.md`
- `docs/live-system-testing-guide.md`


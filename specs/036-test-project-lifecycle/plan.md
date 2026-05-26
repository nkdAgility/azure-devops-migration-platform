# Implementation Plan: Test Project Lifecycle for Connector Tests

**Branch**: `[036-test-project-lifecycle]` | **Date**: 2026-05-22 | **Spec**: `specs/036-test-project-lifecycle/spec.md`

**Input**: Feature specification from `/specs/036-test-project-lifecycle/spec.md`

## Summary

Add a connector-test lifecycle capability so eligible tests can provision an isolated project before execution and always attempt teardown after execution (including failed runs), with explicit run-correlated lifecycle outcomes for Azure DevOps Services and Team Foundation Server, while preserving deterministic test behavior and connector parity expectations.

## Technical Context

**Language/Version**: C# targeting .NET 10 (`global.json` SDK `10.0.201`) with net481 isolation for TFS Object Model agent concerns

**Primary Dependencies**: MSTest + Reqnroll.MSTest + Moq; Azure DevOps client libraries (`Microsoft.VisualStudio.Services.Client`, `Microsoft.TeamFoundationServer.*`); `Microsoft.Extensions.*` DI/options/logging

**Storage**: N/A for feature persistence; lifecycle evidence emitted through existing test output/logging channels

**Testing**: MSTest unit tests, Reqnroll feature tests, `SystemTest_Simulated` and live connector coverage tests

**Target Platform**: Windows/Linux/macOS for .NET 10 test execution; Windows for TFS Object Model-backed flows

**Project Type**: Multi-project .NET modular monolith (CLI, agents, infrastructure, connector implementations, test suites)

**Performance Goals**: Project setup fails fast when creation fails; teardown attempt completion target aligns with spec SC-002 (>=98% within 5 minutes for successful creates)

**Constraints**:
- No connector stubs/placeholders across Simulated/AzureDevOps/TFS where APIs support capability
- No migration-engine boundary violations; feature remains in test/connector infrastructure scope
- Cleanup safety: teardown only for project created by current run
- Observable lifecycle outcomes required for every eligible run

**Scale/Scope**: Connector-test lifecycle only (no migration package format changes; no production migration behavior changes)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate

- **Change class**: **Class A** (internal implementation/planning changes, no canonical surface contract break in this planning phase)
- **Applicable guardrails**: architecture boundaries, connector coverage, testing rules, observability requirements, test-first workflow, change governance
- **Capability seam decision (required)**:
  - **Concern**: Ephemeral project lifecycle for connector tests
  - **Canonical seam owner**: New connector-facing lifecycle service abstraction in `Abstractions.Agent` consumed by test infrastructure/orchestration
  - **Canonical reusable surface**: `IProjectLifecycleService` (planned)
  - **Allowed adapters**: Connector-specific implementations for Simulated, AzureDevOps, TFS
  - **Prohibited parallel entry points**: Ad-hoc project create/delete logic inside tests or command handlers
- **Gate result**: PASS (no Class C change, no consent requirement)

### Post-Phase 1 Re-check

- Modular Monolith: PASS (single seam, connector adapters only)
- Clean Architecture: PASS (abstraction-first, outer infra implementations)
- Hexagonal: PASS (core/test orchestration calls ports, not SDK directly)
- Vertical Slice: PASS (test lifecycle concern remains cohesive)
- Screaming Architecture: PASS (naming reflects lifecycle concern)
- Architecture Deepening: PASS (eliminates ad-hoc per-test setup logic in favor of a canonical lifecycle seam)

## Project Structure

### Documentation (this feature)

```text
specs/036-test-project-lifecycle/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── project-lifecycle-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions.Agent/
├── DevOpsMigrationPlatform.Infrastructure.Agent/
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
├── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
└── DevOpsMigrationPlatform.Infrastructure.Simulated/

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
├── DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/
├── DevOpsMigrationPlatform.TfsMigrationAgent.Tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/

features/
└── platform/
```

**Structure Decision**: Keep existing modular connector/test architecture; add lifecycle capability through abstraction + connector adapters + test harness integration, preserving current layering and registration patterns.

## Phase 0: Research Output Plan

Produce `research.md` with decisions for:
1. lifecycle seam placement and connector adapter strategy
2. eligibility declaration mechanism for tests
3. guaranteed cleanup orchestration pattern for pass/fail runs
4. observability of setup/teardown outcomes

All prior `NEEDS CLARIFICATION` placeholders resolved by repository evidence.

## Phase 1: Design & Contracts Output Plan

1. Create `data-model.md` defining lifecycle entities, state transitions, and validation rules.
2. Create `contracts/project-lifecycle-contract.md` for lifecycle service behavior and invariants.
3. Create `quickstart.md` covering authoring/running lifecycle-enabled connector tests.
4. Update agent context pointers to this plan via `.specify/scripts/powershell/update-agent-context.ps1`.

## Complexity Tracking

No constitution violations requiring justification at planning phase.

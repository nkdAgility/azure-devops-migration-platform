# Implementation Plan: Package Manager Adoption

**Branch**: `[034-package-manager-adoption]` | **Date**: 2026-05-09 | **Spec**: `specs/034-package-manager-adoption/spec.md`  
**Input**: Feature specification from `specs/034-package-manager-adoption/spec.md`

## Summary

Introduce a typed package boundary (`IPackage`) above `IArtefactStore`/`IStateStore` and migrate runtime package-path composition to intent-based package operations. Preserve canonical package layout, streaming import guarantees, checkpoint/phase semantics, and run-log routing while delivering full connector-compatible behavior and observability.

## Technical Context

**Language/Version**: C# 10+ (`net10.0`) with multi-targeted shared abstractions (`net481;net10.0`)  
**Primary Dependencies**: `Microsoft.Extensions.*` DI/options/logging/hosting, `System.Text.Json`, OpenTelemetry via platform telemetry abstractions  
**Storage**: Package persistence via `IArtefactStore` + `IStateStore` (filesystem + Azure Blob implementations)  
**Testing**: MSTest + Reqnroll + Moq; full repository validation with `dotnet test DevOpsMigrationPlatform.slnx --nologo`  
**Target Platform**: .NET 10 agents/services cross-platform; TFS connector path remains Windows/net481 agent-only  
**Project Type**: Multi-project migration platform (CLI + control plane + agents + shared abstractions)  
**Performance Goals**: No degradation to streaming behavior; maintain one-item-at-a-time import processing and bounded in-memory log batching  
**Constraints**: Preserve deterministic package paths and lexicographic enumeration; no direct filesystem access in modules; no control-plane package writes; full connector coverage; package-boundary contracts/types are introduced in `src/DevOpsMigrationPlatform.Abstractions.Agent/` only (not `src/DevOpsMigrationPlatform.Abstractions/`)  
**Scale/Scope**: Migrate package access in abstractions + infrastructure runtime surfaces (plan, checkpoint, package config, diagnostics/progress logs, module/orchestrator path composition seams)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate

- **I. Package-First Migration**: PASS — design keeps Source → Package → Target and centralizes package access semantics.
- **II. Streaming Import & Memory Safety**: PASS — boundary preserves lazy enumeration and forbids sorting/materialization.
- **III. Canonical WorkItems Layout**: PASS — no layout changes are introduced.
- **IV. Cursor-Based Checkpointing**: PASS — checkpoint semantics remain authoritative and path routing is centralized, not altered.
- **V. Module Isolation via Abstractions**: PASS — modules continue to depend on abstractions; package boundary reduces direct path handling.
- **X. Engineering Practice Discipline / XI Connector Coverage**: PASS — plan includes Simulated/AzureDevOps/TFS parity for package-boundary call paths and tests.

### Post-Design Re-check

- **Status**: PASS  
- Research/design artifacts define typed package contracts, routing rules, and migration steps without violating guardrails or constitution reject conditions.

## Phase 0: Research Output

`specs/034-package-manager-adoption/research.md` resolves contract/routing decisions and migration strategy:

1. Typed package contract shape and responsibilities
2. Routing matrix for authoritative state, run-audit copies, and run logs
3. Migration strategy for existing runtime callers
4. Connector and observability compliance strategy

## Phase 1: Design & Contracts Output

- `specs/034-package-manager-adoption/data-model.md`
- `specs/034-package-manager-adoption/contracts/package-boundary-contract.md`
- `specs/034-package-manager-adoption/quickstart.md`
- Agent context marker update in `.github/copilot-instructions.md`

## Project Structure

### Documentation (this feature)

```text
specs/034-package-manager-adoption/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── package-boundary-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   └── Storage/
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Context/
│   ├── Checkpointing/
│   ├── Telemetry/
│   └── Storage/
├── DevOpsMigrationPlatform.MigrationAgent/
└── DevOpsMigrationPlatform.TfsMigrationAgent/

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/
```

**Structure Decision**: Keep existing modular monolith solution structure; implement package-boundary contracts in abstractions, routing/implementation in infrastructure agent, and migrate existing runtime callers incrementally.

## Phase 2 Planning Approach (for `/speckit.tasks`)

1. Add/confirm `IPackage` contract and typed contexts/payloads.
2. Implement package router and boundary implementation over existing stores.
3. Migrate checkpoint, plan persistence, package config, and run-log sinks to boundary calls.
4. Migrate module/orchestrator direct path composition seams to boundary intents.
5. Add/adjust tests for routing, resume/phase semantics, log append behavior, and connector parity.
6. Update `.agents/context` and `/docs` references for package-boundary-first usage.

## Complexity Tracking

No constitution violations requiring justification.

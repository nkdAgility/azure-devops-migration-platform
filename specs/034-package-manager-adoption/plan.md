# Implementation Plan: Package Manager Adoption

**Branch**: `[034-package-manager-adoption]` | **Date**: 2026-05-09 | **Spec**: `specs/034-package-manager-adoption/spec.md`  
**Input**: Feature specification from `specs/034-package-manager-adoption/spec.md`

## Summary

Standardize package access on `IPackageAccess` as the only permitted caller-facing package boundary, using typed content, metadata, and log contexts plus caller-supplied `IPackageContentAddress` suffixes for module-owned layout. Preserve canonical package layout, streaming guarantees, checkpoint and phase semantics, and run-log routing while removing package-facing store bypasses and isolating remaining string-path compatibility behind `LegacyPackagePathShim`.

## Technical Context

**Language/Version**: C# 10+ (`net10.0`) with multi-targeted shared abstractions (`net481;net10.0`)  
**Primary Dependencies**: `Microsoft.Extensions.*` DI/options/logging/hosting, `System.Text.Json`, OpenTelemetry via platform telemetry abstractions  
**Storage**: Package persistence via `IArtefactStore` + `IStateStore` behind `IPackageAccess` (filesystem + Azure Blob implementations)  
**Testing**: MSTest + Reqnroll + Moq; full repository validation with `dotnet test DevOpsMigrationPlatform.slnx --nologo`  
**Target Platform**: .NET 10 agents/services cross-platform; TFS connector path remains Windows/net481 agent-only  
**Project Type**: Multi-project migration platform (CLI + control plane + agents + shared abstractions)  
**Performance Goals**: No degradation to streaming behavior; maintain one-item-at-a-time import processing and bounded in-memory log batching  
**Constraints**: Preserve deterministic package paths and lexicographic enumeration; no direct filesystem access in modules; no control-plane package writes; no package-facing runtime bypass of `IPackageAccess`; module-owned suffixes are caller-supplied and must not be inferred; metadata and logs remain distinct package surfaces; full connector coverage; package-boundary contracts/types are introduced in `src/DevOpsMigrationPlatform.Abstractions.Agent/` only (not `src/DevOpsMigrationPlatform.Abstractions/`)  
**Scale/Scope**: Align abstractions, routing, runtime package-facing services, and package-manager documentation with the explicit `IPackageAccess` boundary while containing remaining string-path compatibility behind the legacy shim

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate

- **I. Package-First Migration**: PASS — design keeps Source → Package → Target and centralizes package access semantics.
- **II. Streaming Import & Memory Safety**: PASS — boundary preserves lazy enumeration and forbids sorting/materialization.
- **III. Canonical WorkItems Layout**: PASS — no layout changes are introduced.
- **IV. Cursor-Based Checkpointing**: PASS — checkpoint semantics remain authoritative and path routing is centralized, not altered.
- **V. Module Isolation via Abstractions**: PASS — modules continue to depend on abstractions; package-facing runtime flows move away from direct store usage and raw path construction.
- **X. Engineering Practice Discipline / XI Connector Coverage**: PASS — plan keeps the typed boundary in `Abstractions.Agent`, preserves fail-fast validation, and includes Simulated/AzureDevOps/TFS parity for package-boundary behavior.
- **Security / Package Rules Alignment**: PASS — router validation rejects absolute or escaping relative paths before any package write occurs.

### Post-Design Re-check

- **Status**: PASS  
- Research/design artifacts define the `IPackageAccess` boundary, caller-supplied address ownership, no-bypass runtime policy, and transitional shim handling without violating guardrails or constitution reject conditions.

## Phase 0: Research Output

`specs/034-package-manager-adoption/research.md` resolves contract/routing decisions and migration strategy:

1. Canonical `IPackageAccess` contract shape and responsibilities
2. Package-owned prefix versus module-owned suffix ownership through `IPackageContentAddress`
3. Runtime no-bypass policy for package-facing reads and writes
4. Transitional compatibility strategy for `LegacyPackagePathShim`
5. Connector and observability compliance strategy

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
│   ├── Modules/
│   ├── Telemetry/
│   └── Storage/
├── DevOpsMigrationPlatform.MigrationAgent/
└── DevOpsMigrationPlatform.TfsMigrationAgent/

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
├── DevOpsMigrationPlatform.TfsMigrationAgent.Tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/
```

**Structure Decision**: Keep the existing modular monolith structure. The typed package contract remains in `Abstractions.Agent/Storage`, routing and boundary implementation remain in `Infrastructure.Agent/Storage`, runtime adoption is completed in infrastructure/agent workers and orchestrators, and the remaining path-based surface is explicitly isolated inside `LegacyPackagePathShim` until removed.

## Phase 2 Planning Approach (for `/speckit.tasks`)

1. Align package abstractions on `IPackageAccess`, `IPackageContentAddress`, `PackageContentContext`, `PackageContentKind`, and the explicit content API verb set.
2. Harden `PackagePathRouter` and `ActivePackageAccess` so package-owned prefixes stay in the boundary, module-owned suffixes stay caller-supplied, and route validation rejects absolute or escaping addresses.
3. Remove package-facing runtime bypasses by migrating checkpointing, plan persistence, package config, progress/diagnostics logging, and worker flows to `IPackageAccess`.
4. Keep unavoidable string-path compatibility isolated in `LegacyPackagePathShim` and treat every remaining shim call site as migration debt to be audited and reduced.
5. Add or refresh tests for route validation, explicit content API behavior, resume and phase semantics, package-config hardening, and Simulated/AzureDevOps/TFS parity.
6. Update `.agents/context` and `/docs` references so the package boundary is documented as `IPackageAccess` only, with no package-facing store bypasses in runtime code.

## Complexity Tracking

No constitution violations requiring justification.

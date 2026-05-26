# Implementation Plan: Work Item Orchestrator and Resolution Architecture Alignment

**Branch**: `037-workitem-resolution-arch` | **Date**: 2026-05-26 | **Spec**: `specs/037-workitem-resolution-arch/spec.md`

**Input**: Feature specification from `specs/037-workitem-resolution-arch/spec.md`

## Summary

Standardize WorkItems orchestration around the canonical runtime chain:
`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

Primary implementation intent:
- keep deterministic Import sequencing explicit and observable
- move generic resolution lifecycle into one shared resolution service
- remove inline concrete orchestrator construction in module wrappers
- standardize orchestrator abstraction shape (`Export`, `Prepare`, `Import`, `Validate`) across module orchestrators
- preserve parity across Simulated, AzureDevOpsServices, and TeamFoundationServer where APIs support behavior

## Technical Context

**Language/Version**: C# (`net10.0` primary, `net481` for TFS agent/runtime compatibility seams)

**Primary Dependencies**:
- `Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging`
- OpenTelemetry (`ActivitySource`, metrics, logs via existing platform abstractions)
- existing platform abstractions in `DevOpsMigrationPlatform.Abstractions*`

**Storage**:
- package boundary through `IPackageAccess`
- persistence through `IArtefactStore` and `IStateStore` under the package boundary
- cursor/idmap/checkpoint artifacts in canonical package locations

**Testing**:
- MSTest
- Reqnroll.MSTest + Moq (strict)
- `SystemTest_Simulated`, `SystemTest_AzureDevOps`, and TFS coverage where supported

**Target Platform**:
- MigrationAgent (`net10.0`)
- TfsMigrationAgent (`net481`, Windows-only)

**Project Type**: Modular monolith with CLI/control-plane/agent runtime separation

**Performance Goals**:
- preserve streaming import/export behavior (no full materialization)
- preserve lexicographic traversal and deterministic resume
- no regression in import correctness/completion vs current baseline

**Constraints**:
- must satisfy guardrails and constitution (package-first, streaming, checkpointing, full adapter coverage)
- no parallel concern engines or surface bypass
- Class C consent required before abstraction/contract shape changes

**Scale/Scope**:
- WorkItems import orchestration and resolution path, with module/orchestrator split standardization
- no unrelated phase redesign

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Gate

1. **Package-first + streaming invariants**: PASS  
   Design keeps Source -> Files -> Target and preserves streaming + checkpoint resume rules.

2. **Canonical runtime chain**: PASS  
   Design enforces `Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

3. **Full adapter coverage (Simulated/AzureDevOps/TFS)**: PASS (planned)  
   All behavior changes are planned across all adapters where API permits.

4. **Surface/contract governance**: PASS WITH CONDITION  
   Standardizing orchestrator abstraction shape may trigger Class C when changing public contract surfaces. Operator consent and linked contract evidence required before implementation.

5. **Observability obligations**: PASS (planned)  
   Stage progression and sequence conformance remain runtime-visible via existing telemetry channels.

### Post-Design Gate

All gates remain PASS at plan stage.  
Class C consent remains a mandatory pre-implementation condition for any public abstraction contract change.

## Phase 0 — Research Output

`research.md` resolves architecture unknowns and trade-offs for:
- orchestrator standardization strategy
- WorkItems export/import abstraction parity
- inventory discovery naming and layering cleanup boundaries
- adapter parity and failure-mode handling

## Phase 1 — Design Output

- `data-model.md` defines orchestration and resolution entities/state transitions
- `contracts/` defines planned runtime contract changes and behavior contracts
- `quickstart.md` defines implementation and verification flow
- `AGENTS.md` plan reference updated to this plan

## Project Structure

### Documentation (this feature)

```text
specs/037-workitem-resolution-arch/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── orchestrator-shape-contract.md
│   └── workitems-resolution-service-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   ├── Import/
│   ├── Modules/
│   └── Discovery/
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Modules/
│   ├── Import/
│   └── Discovery/
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
├── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
└── DevOpsMigrationPlatform.Infrastructure.Simulated/

tests/
├── DevOpsMigrationPlatform.Tests/
├── DevOpsMigrationPlatform.SystemTests/
└── DevOpsMigrationPlatform.SystemTests.AzureDevOps/
```

**Structure Decision**: Keep existing modular monolith layout; implement only targeted abstraction and orchestration changes in Abstractions + Infrastructure.Agent + adapter projects.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |

# Feature Assessment: agent-job-context

**Feature file:** `features/platform/agent-job-context.feature`
**Family:** `agent-job-context`
**Wiring state:** `unwired` (no Reqnroll step bindings)

## Scenarios

| # | Title | Tags | Status |
|---|-------|------|--------|
| 1 | ModuleReadsMode_ContextProvided_NoFullOptionsGraph | @module-isolation | to-map |
| 2 | ModuleReadsPackagePath_ContextProvided_NoFullOptionsGraph | @module-isolation | to-map |
| 3 | ContextIsReadOnly_ModuleAccesses_NoWritePath | @context-read-only | to-build |
| 4 | TfsSourceOnlyJob_ContextResolved_NoTargetInfo | @tfs-source-only | to-build |

## Existing Test Coverage

### AgentJobContextIntegrationTests (partial coverage for S1, S2)
- `ActiveJobAgentJobContext_UsesExplicitCurrentContext_WhenAvailable` — sets Mode/PackagePath/ConfigVersion, asserts all three read back correctly via IAgentJobContext. Maps to S1 (Mode read) and S2 (PackagePath read). No target endpoint involved → also covers intent of S4.
- `ActiveJobAgentJobContext_ReturnsEmptyValues_WhenNoCurrentContextExists` — graceful fallback.

### AgentJobContextTests (no direct scenario coverage)
- Construction/validation tests for AgentJobContext concrete class.
- Logging tests (T054, T055).

## Scenario-to-Test Mapping

| Scenario | Pre-existing | Action |
|----------|-------------|--------|
| S1 ModuleReadsMode | `AgentJobContextIntegrationTests.ActiveJobAgentJobContext_UsesExplicitCurrentContext_WhenAvailable` | map (partial — Mode="Inventory", intent matches) |
| S2 ModuleReadsPackagePath | same test | map (same test also asserts PackagePath) |
| S3 ContextIsReadOnly | none | build: `AgentJobContextTests.IAgentJobContext_Interface_HasOnlyReadOnlyProperties` |
| S4 TfsSourceOnly | none | build: `AgentJobContextTests.AgentJobContext_ContextResolvesWithoutTargetEndpointDependency` |

## Key Types
- `IAgentJobContext` — `DevOpsMigrationPlatform.Abstractions.Agent.Context` — `{ Mode, PackagePath, ConfigVersion }` all `{ get; }`
- `AgentJobContext` — concrete sealed class, validates Mode and PackagePath on init
- `ActiveJobAgentJobContext` — proxy delegating to `ICurrentAgentJobContextAccessor.Current`
- `ICurrentAgentJobContextAccessor` — singleton holder, Set/Clear per job lifecycle

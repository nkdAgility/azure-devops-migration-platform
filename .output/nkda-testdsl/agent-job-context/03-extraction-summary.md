# Extraction Summary: agent-job-context

**Wiring state:** `unwired` — no Reqnroll step bindings existed; no `.feature.cs` generated files to purge.

## Scenario-to-Test Map

| Scenario | Test Class | Test Method | Action |
|----------|-----------|-------------|--------|
| S1 ModuleReadsMode | AgentJobContextIntegrationTests | ActiveJobAgentJobContext_UsesExplicitCurrentContext_WhenAvailable | map (pre-existing) |
| S2 ModuleReadsPackagePath | AgentJobContextIntegrationTests | ActiveJobAgentJobContext_UsesExplicitCurrentContext_WhenAvailable | map (pre-existing) |
| S3 ContextIsReadOnly | AgentJobContextTests | IAgentJobContext_Interface_HasOnlyReadOnlyProperties | build (new) |
| S4 TfsSourceOnlyJob | AgentJobContextTests | AgentJobContext_ContextResolvesWithoutTargetEndpointDependency | build (new) |

## Files Modified

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/AgentJobContextTests.cs`
  - Added `[TestCategory("UnitTest")]` to all 8 existing `[TestMethod]` entries
  - Added `using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors`
  - Added 2 new test methods (S3, S4)

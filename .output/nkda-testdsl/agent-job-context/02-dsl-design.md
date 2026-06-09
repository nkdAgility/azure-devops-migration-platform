# DSL Design: agent-job-context

**Pattern:** Direct MSTest unit tests using concrete types — no Reqnroll, no step bindings.

## Test Host

No DI host needed. Scenarios S3 and S4 are unit-level. S1/S2 are already covered by existing integration tests that wire `CurrentAgentJobContextAccessor` + `ActiveJobAgentJobContext` directly (no full DI host).

## New Tests

### S3 — ContextIsReadOnly_ModuleAccesses_NoWritePath

```
AgentJobContextTests.IAgentJobContext_Interface_HasOnlyReadOnlyProperties()
```

Uses reflection over `typeof(IAgentJobContext)` to assert:
- Every property has a public getter
- No property has a public setter

This validates the design constraint that the interface is read-only.

### S4 — TfsSourceOnlyJob_ContextResolved_NoTargetInfo

```
AgentJobContextTests.AgentJobContext_ContextResolvesWithoutTargetEndpointDependency()
```

Constructs `AgentJobContext` with Mode="Export", PackagePath=absolute path.
Wraps in `ActiveJobAgentJobContext` via `CurrentAgentJobContextAccessor`.
Asserts Mode and PackagePath read back correctly.
Does NOT involve `ITargetEndpointInfo` at all — confirming the interface resolves without target config.

## Tag Requirement

All new `[TestMethod]` entries must carry `[TestCategory("UnitTest")]` immediately above.
Existing methods in `AgentJobContextTests` that lack `[TestCategory]` must be updated to add it for class consistency.

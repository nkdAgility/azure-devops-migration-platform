# Quickstart: Lifecycle-Enabled Connector Test

## 1. Mark a qualifying test

Create an explicit lifecycle eligibility flag for the connector under test:

```csharp
var eligibility = SystemTestBase.EvaluateLifecycleEligibility(
    enabled: true,
    connectorType: "Simulated",
    namePrefix: "systemtest");
```

## 2. Run the test

Use normal MSTest execution path for the relevant test project, for example:

```powershell
dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj --filter "TestCategory=SystemTest_Simulated"
```

## 3. Expected behavior

1. Before test actions execute, `SystemTestContext.SetupLifecycleAsync(...)` creates an ephemeral project for the run.
2. Test execution binds to `SystemTestContext.ExecutionProjectName`.
3. After run completion (pass or fail), `SystemTestContext.TeardownLifecycleAsync(...)` attempts teardown.
4. Lifecycle record output includes create result, execution project identity, teardown result, blocking reason (if any), and latency.

## 4. Connector coverage expectations

- Simulated: deterministic in-memory lifecycle behavior for fast validation.
- AzureDevOpsServices: project create/delete through connector implementation.
- TeamFoundationServer: project create/delete through TFS connector implementation where API support exists.

## 5. Troubleshooting signals

- Setup failure: run fails fast with explicit setup error and no test action execution.
- Teardown failure: run completes with failed cleanup outcome and blocking reason visible in lifecycle record.

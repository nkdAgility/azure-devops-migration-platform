# Quickstart: Lifecycle-Enabled Connector Test

## 1. Mark a qualifying test

Declare lifecycle eligibility for a connector test (implementation detail: attribute/config marker to be finalized in tasks phase).

## 2. Run the test

Use normal MSTest execution path for the relevant test project, for example:

```powershell
dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj --filter "TestCategory=SystemTest_Simulated"
```

## 3. Expected behavior

1. Before test actions execute, an ephemeral project is created for the run.
2. The run executes against that created project context.
3. After run completion (pass or fail), teardown is attempted for the created project.
4. Lifecycle record output includes create result, execution project identity, teardown result, and blocking reason (if any).

## 4. Connector coverage expectations

- Simulated: deterministic in-memory lifecycle behavior for fast validation.
- AzureDevOpsServices: project create/delete through connector implementation.
- TeamFoundationServer: project create/delete through TFS connector implementation where API support exists.

## 5. Troubleshooting signals

- Setup failure: run fails fast with explicit setup error and no test action execution.
- Teardown failure: run completes with failed cleanup outcome and blocking reason visible in lifecycle record.

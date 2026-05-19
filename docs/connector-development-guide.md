# Connector Development Guide

Audience: Contributors.

## What Is a Connector?

A connector implements the interface between the platform and an external system. Connectors provide:

- **Source connectors** — enumerate items from a source system for export.
- **Target connectors** — accept items from the platform for import to a target system.
- **Simulated connectors** — deterministic, in-process implementations for testing.

Connectors are consumed by modules. They must never be called from CLI, TUI, or Control Plane code.

## Three Required Implementations

Every capability added to a connector must be implemented for all three connector types:

1. **Simulated** — deterministic, no external dependencies, used in unit/integration tests.
2. **AzureDevOps** — calls the Azure DevOps REST API via `IAzureDevOpsClientFactory`.
3. **TFS** — calls the TFS Object Model (net481 only), used in `TfsMigrationAgent`.

Implementing for one connector while leaving stubs in the others is not acceptable.

## Simulated Connector Requirements

- Must return at least 2 items per `EnumerateAsync` call.
- Must be deterministic given a `Seed` value.
- Must not make network calls.
- Zero-item returns silently make downstream tests vacuously pass — forbidden.

## Azure DevOps Connector Requirements

- Every method must call at least one method on a client from `IAzureDevOpsClientFactory`.
- Must not hard-code return values without calling the SDK.
- Must implement pagination — never fetch all pages in one call.
- Must implement retry with exponential back-off.
- Must respect throttle headers from the API.

## TFS Connector Requirements

- Uses the TFS Object Model — available only in net481.
- Runs in `TfsMigrationAgent` (Windows only).
- Must follow the same interface contracts as the ADO connector.
- Authentication is Windows Integrated only.

## Client Factory Pattern

Azure DevOps connectors must obtain HTTP clients via `IAzureDevOpsClientFactory`:

```csharp
var client = await _clientFactory.GetWorkItemTrackingClientAsync(cancellationToken);
var workItem = await client.GetWorkItemAsync(id, cancellationToken: cancellationToken);
```

Never instantiate `VssConnection` directly in connector code.

## Pagination

All list operations must page through results:

```csharp
int skip = 0;
const int pageSize = 200;
while (true)
{
    var page = await client.GetWorkItemsAsync(ids.Skip(skip).Take(pageSize), ...);
    if (page.Count == 0) break;
    foreach (var item in page) yield return item;
    skip += pageSize;
}
```

## Retry Behaviour

Use `IResiliencePipelineProvider` for retry. Do not implement retry inline. See [operator-advanced-guide.md](operator-advanced-guide.md) for resilience configuration.

## Testing Expectations

- Unit tests use Simulated connectors.
- `SystemTest_Simulated` tests assert that data flows through.
- `SystemTest_AzureDevOps` tests (tagged `[TestCategory("SystemTest")]`) use live credentials.
- Every connector method must be exercised by at least one test.

## Interfaces

All connector interfaces must be defined in `DevOpsMigrationPlatform.Abstractions`. Concrete implementations live in `DevOpsMigrationPlatform.Infrastructure.*` projects.

## Further Reading

- [client-integration-guide.md](client-integration-guide.md) — SDK usage patterns
- [.agents/20-guardrails/core/architecture-boundaries.md](../.agents/20-guardrails/core/architecture-boundaries.md) — boundary rules
- [module-development-guide.md](module-development-guide.md) — how modules consume connectors

# Connector Model

Compressed connector model for agents. See `docs/connector-development-guide.md` for the full guide.

## What Is a Connector?

A connector adapts between the platform and an external system. Every capability must be implemented for three variants:

| Variant | Description |
|---|---|
| Simulated | Deterministic, in-process, no network — used for unit/integration tests |
| AzureDevOps | Calls the Azure DevOps REST API via `IAzureDevOpsClientFactory` |
| TFS | Calls the TFS Object Model (net481 only, in `TfsMigrationAgent`) |

## Source vs Target

- **Source connector** — enumerates items from the source system.
- **Target connector** — accepts items and pushes them to the target.

All source connectors have Simulated, ADO, and TFS variants.
Target connector coverage evolves by capability; do not treat current TFS target gaps as a permanent architecture rule.

## Client Integration Boundary

Azure DevOps connectors obtain clients from `IAzureDevOpsClientFactory`. This is the only permitted way to create API clients. No direct `VssConnection` instantiation. (The identity adapter uses `IAzureDevOpsClientFactory.CreateIdentityClientAsync` → `IdentityHttpClient`.)

## Identity Adapter (`IIdentityAdapter`)

`IIdentityAdapter` is the connector abstraction for querying the **target** tenant by UPN/display name during the Prepare phase (distinct from `IIdentitySource`, which enumerates the source at export time). Each connector lives at its own project boundary — **no `#if` guards**:

- `SimulatedIdentityAdapter` (Infrastructure.Simulated, net10) — deterministic in-memory target.
- `AzureDevOpsIdentityAdapter` (Infrastructure.AzureDevOps, net10) — `IdentityHttpClient.ReadIdentitiesAsync`.
- `TfsIdentityAdapter` (Infrastructure.TfsObjectModel, net481) — reduced capability: returns empty + structured Warning (the TFS Identity Service does not expose UPN/display-name search). Modeled explicitly in the contract result, not a stub.

Registered via `AddIdentityAdapter<T>("<connectorType>")` and dispatched by `CompositeIdentityAdapter` on `ITargetEndpointInfo.ConnectorType`.

## Key Rules

- Simulated must return ≥2 items per `EnumerateAsync`.
- ADO methods must call the SDK — no hard-coded returns.
- TFS methods use Windows authentication.
- Pagination is mandatory for all list operations.
- Retry is via `IResiliencePipelineProvider`, not inline.
- All interfaces in `DevOpsMigrationPlatform.Abstractions`.
- Concrete implementations in `DevOpsMigrationPlatform.Infrastructure.*`.

## Simulated Dependency Discovery

`SimulatedDependencyDiscoveryServiceFactory` implements `IDependencyDiscoveryServiceFactory` for the Simulated connector. It is registered via `AddSimulatedDependencyAnalysis()` using `TryAddSingleton` (ADO factory takes precedence when both are present). The factory delegates to `SimulatedWorkItemLinkAnalysisService` (keyed `"Simulated"`) which returns an empty link sequence — no network calls are made.




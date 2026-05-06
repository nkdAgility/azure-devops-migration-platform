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
All target connectors have Simulated and ADO variants (TFS is always source-only).

## Client Integration Boundary

Azure DevOps connectors obtain clients from `IAzureDevOpsClientFactory`. This is the only permitted way to create API clients. No direct `VssConnection` instantiation.

## Key Rules

- Simulated must return ≥2 items per `EnumerateAsync`.
- ADO methods must call the SDK — no hard-coded returns.
- TFS methods use Windows authentication.
- Pagination is mandatory for all list operations.
- Retry is via `IResiliencePipelineProvider`, not inline.
- All interfaces in `DevOpsMigrationPlatform.Abstractions`.
- Concrete implementations in `DevOpsMigrationPlatform.Infrastructure.*`.
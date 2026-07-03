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
- Pagination is mandatory for all list operations. Where an upstream REST endpoint is
  genuinely unpaged, a documented exemption is recorded below (see "Pagination
  Exemptions") and at the call site — silent non-compliance is not permitted.
- Retry is via `IResiliencePipelineProvider`, not inline.
- All interfaces in `DevOpsMigrationPlatform.Abstractions`.
- Concrete implementations in `DevOpsMigrationPlatform.Infrastructure.*`.

## ConnectorCapability Mechanism

`ConnectorCapability` is an enum declared in `DevOpsMigrationPlatform.Abstractions.Agent`.
Each connector registers its supported capabilities via `IConnectorCapabilityProvider`.
Extensions gate their work behind `_capProvider.Has(Cap.X)` — no null guards, no try/catch
for capability detection.

### Capability Flags

| Flag | Meaning | Simulated | ADO | TFS |
|---|---|---|---|---|
| `BoardConfig` | Composite: board columns, swimlanes, card rules | ✔ | ✔ | ✗ |
| `Backlogs` | Backlog level metadata | ✔ | ✔ | ✗ |
| `TaskboardColumns` | Sprint taskboard columns | ✔ | ✔ | ✗ |
| `TeamSettings` | Team settings (backlog levels, bug behaviour, working days) | ✔ | ✔ | ✗ |
| `TeamIterations` | Team iteration assignments | ✔ | ✔ | ✗ |
| `TeamMembers` | Team membership | ✔ | ✔ | ✗ |
| `TeamCapacity` | Per-member iteration capacity | ✔ | ✔ | ✗ |
| `TeamAreaPaths` | Team area path assignments | ✔ | ✔ | ✗ |
| `WorkItemComments` | Work-item comment export/replay | ✔ | ✔ | ✗ |

Team and comment flags were introduced by ADR-0024 (EC-H1): extensions gate on
`Has(Cap.X)` instead of null-checking optional seam members. TFS Object Model declares
`ConnectorCapability.None` explicitly via `TfsConnectorCapabilityProvider`.

When `BoardConfig` is absent the `BoardConfigTeamExtension` emits a `BoardConfigSkipped`
progress event and returns immediately — no artefact is written, no error is raised.

### Explicit-registration rule

A connector registers its capabilities explicitly at DI startup.
No implicit defaults. If a flag is not registered, `Has(Cap.X)` returns `false`.
This prevents silent capability leakage when a new connector type is added.

## Pagination Exemptions (ADR-0024, EC-M2 — operator-consented)

The mandatory-pagination rule is exempted for the following Azure DevOps REST
endpoints, which expose **no** pagination parameters (`$top`/`$skip`/continuation
token) in api-version 7.1. Each call site carries a matching exemption comment.

| Operation | Endpoint (api-version 7.1) | Evidence |
|---|---|---|
| `AzureDevOpsBoardAdapter.GetBoardsAsync` | `GET {org}/{project}/{team}/_apis/work/boards` | [Boards - List](https://learn.microsoft.com/rest/api/azure/devops/work/boards/list?view=azure-devops-rest-7.1) — URI parameters are org/project/team/api-version only; returns the full `BoardReference[]` |
| `AzureDevOpsBoardAdapter.GetBacklogsAsync` | `GET {org}/{project}/{team}/_apis/work/backlogs` | [Backlogs - List](https://learn.microsoft.com/rest/api/azure/devops/work/backlogs/list?view=azure-devops-rest-7.1) — no paging parameters; returns the full `BacklogLevelConfiguration[]` (bounded by process backlog levels) |
| `AzureDevOpsBoardAdapter.GetTaskboardColumnsAsync` | `GET {org}/{project}/{team}/_apis/work/taskboardcolumns` | Single `TaskboardColumns` resource — not a paged collection |
| `AzureDevOpsIdentityAdapter.SearchAsync` (`ReadIdentitiesAsync`) | `GET {org}/_apis/identities?searchFilter=…&filterValue=…` | [Identities - Read Identities](https://learn.microsoft.com/rest/api/azure/devops/ims/identities/read-identities?view=azure-devops-rest-7.1) — query parameters are search filters only; the endpoint exposes no continuation token |

These result sets are naturally bounded (boards/backlog levels per team; identities
matching an exact UPN/display-name filter). Should Microsoft add paging parameters to
these endpoints, the exemption lapses and paging must be implemented.

## Simulated Dependency Discovery

`SimulatedDependencyDiscoveryServiceFactory` implements `IDependencyDiscoveryServiceFactory` for the Simulated connector. It is registered via `AddSimulatedDependencyAnalysis()` using `TryAddSingleton` (ADO factory takes precedence when both are present). The factory delegates to `SimulatedWorkItemLinkAnalysisService` (keyed `"Simulated"`) which returns an empty link sequence — no network calls are made.




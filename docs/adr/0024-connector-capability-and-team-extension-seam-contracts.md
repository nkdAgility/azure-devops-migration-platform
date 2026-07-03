# ADR 0024 — Connector Capability Flags, Team/Comment Extension Seam Contracts, and ADO Pagination Exemptions

## Status

Accepted

Executes architecture-audit items **EC-H1**, **EC-M2**, **EC-M3**, **EC-M4**, and **EC-L1** (analysis/archcheck/report.md) as one themed Class C change under explicit operator consent, with contract compatibility tests and a test-first trace as required by `.agents/20-guardrails/core/change-governance.md`.

## Context

The architecture audit found five related violations in the connector/extension seam model:

1. **EC-H1** — Team and comment extensions gated their behaviour on nullable constructor dependencies (`ITeamSource?`/`ITeamTarget?` null-checks, `IWorkItemCommentSourceFactory? is not null`) instead of the explicit `ConnectorCapability` declaration mechanism that board configuration already used. Capability was inferred from DI wiring, not declared by the connector.
2. **EC-M2** — The ADO board/backlog/identity list operations issue single unpaged SDK calls, while `connector-model.md` mandates pagination for all list operations, with no visible ruling on whether the endpoints support paging at all.
3. **EC-M3** — `TeamSettingsTeamExtension` failed the extension-seam test: team settings are a sub-object of the team entity that the core Teams pipeline already owns, not an optional add-on capability.
4. **EC-M4** — The board-config merge/validation engine (`BuildValidStatesMap`, `FilterInvalidStateMappings`, `MergeByName`) was private policy-entangled logic inside `BoardConfigTeamExtension` rather than a canonical, independently testable seam.
5. **EC-L1** — Every `ITeamTarget` call site forged the `MigrationEndpointOptions endpoint` parameter with `null!` (extensions and `TeamImportOrchestrator`), because implementations either ignored the parameter or would have thrown on it — the parameter was a lie in the contract.

## Decision

### EC-H1 — Capability flags for team and comment extensions

`ConnectorCapability` (Abstractions.Agent) is widened with `TeamSettings`, `TeamIterations`, `TeamMembers`, `TeamCapacity`, `TeamAreaPaths`, `WorkItemComments`, and the composite `TeamCapabilities`. Connectors declare them explicitly:

- **Simulated** and **AzureDevOpsServices** register all team and comment flags (alongside the existing `BoardConfig | TaskboardColumns | Backlogs`) via `StaticConnectorCapabilityProvider`.
- **TeamFoundationServer** keeps its explicit `ConnectorCapability.None` declaration (`TfsConnectorCapabilityProvider`) — the TFS Object Model connector exposes none of these seams, and the None-declaration is the established connector-coverage pattern (no silent stubs; the absence is declared, documented, and tested).

The team extensions (`TeamIterations`, `TeamMembers`, `TeamCapacity`, `TeamAreaPaths`) now take a required `IConnectorCapabilityProvider` plus **non-nullable** `ITeamSource`/`ITeamTarget` dependencies and gate `SupportsExport`/`SupportsImport` on `Has(Cap.X)`. `CommentsWorkItemExtension` gates export on `Has(Cap.WorkItemComments)`; a declared capability without a registered `IWorkItemCommentSourceFactory` now fails loud (`InvalidOperationException`) instead of silently skipping. Hosts that compose no connector fall back to an explicit `None` provider at the composition root (fail-closed).

### EC-M2 — Mandatory pagination: implement where supported, documented exemptions where not (operator-consented guardrail-challenge outcome)

The Azure DevOps REST API documentation (api-version 7.1) was checked for every operation in the finding:

| Operation | Endpoint | Verdict |
|---|---|---|
| `GetBoardsAsync` | `GET …/_apis/work/boards` | **Unpaged** — [Boards - List](https://learn.microsoft.com/rest/api/azure/devops/work/boards/list?view=azure-devops-rest-7.1) defines no `$top`/`$skip`/continuation parameters |
| `GetBacklogsAsync` | `GET …/_apis/work/backlogs` | **Unpaged** — [Backlogs - List](https://learn.microsoft.com/rest/api/azure/devops/work/backlogs/list?view=azure-devops-rest-7.1) defines no paging parameters |
| `GetTaskboardColumnsAsync` | `GET …/_apis/work/taskboardcolumns` | **Not a paged collection** — single `TaskboardColumns` resource |
| Identity `SearchAsync` (`ReadIdentitiesAsync`) | `GET …/_apis/identities?searchFilter=…` | **Unpaged** — [Identities - Read Identities](https://learn.microsoft.com/rest/api/azure/devops/ims/identities/read-identities?view=azure-devops-rest-7.1) exposes only search-filter parameters |

No endpoint in scope supports pagination, so no continuation-token implementation is possible; the consented outcome is a **documented exemption** at every call site (marker comment `PAGINATION EXEMPTION (ADR-0024, EC-M2)`) plus a "Pagination Exemptions" section in `.agents/30-context/domains/connector-model.md` citing the API version and docs. The connector-model rule now states that unpaged upstream endpoints require a recorded exemption — silent non-compliance remains forbidden. Should Microsoft add paging parameters, the exemptions lapse.

### EC-M3 — Team settings folded into the core Teams pipeline

`TeamSettingsTeamExtension` is deleted. Its behaviour moved into the core pipeline it always belonged to:

- **Export** — `TeamExportOrchestrator.ExportTeamAsync` writes `Teams/{slug}/settings.json`, governed by `TeamsModuleExtensionsOptions.TeamSettings` (module options).
- **Import** — `TeamImportOrchestrator.ImportTeamAsync` reads `Teams/{slug}/settings.json` and applies it via `ITeamTarget.SetTeamSettingsAsync` after team creation, using the same skip/warn semantics.

Package content is byte-for-byte identical (same serializer options, same artefact address, same skip conditions), pinned by `TeamExtensionParityTests.TeamsCorePipeline_WhenSettingsReturned_WritesSettingsJsonByteForByte`.

### EC-M4 — Canonical board-config merge/validation seam

`IBoardConfigMergeTool` (Abstractions.Agent.Tools) is the canonical contract for `BuildValidStatesMap`, `FilterInvalidStateMappings` (returning `BoardColumnValidationResult` with `OmittedStateMapping` records), and `MergeByName`. The pure, stateless `BoardConfigMergeTool` (Infrastructure.Agent.Teams) implements it and is registered as a DI singleton. `BoardConfigTeamExtension` consumes the seam and keeps policy only (import modes, warning logging, progress, metrics).

### EC-L1 — `ITeamTarget` endpoint parameter removed

Of the two consented options (remove the parameter vs. resolve the real endpoint), **both concerns were resolved in the same direction**: the `MigrationEndpointOptions endpoint` parameter is removed from all six `ITeamTarget` methods (a Class C contract narrowing), and each implementation resolves its own target endpoint — `AzureDevOpsTeamTarget` now injects `ITargetEndpointInfo` (previously it dereferenced the forged `null!` endpoint, a latent NRE), `SimulatedTeamTarget` never needed it, and `CompositeTeamTarget` dispatches unchanged. No `null!` forging remains in the Teams infrastructure.

## Contract Tests (RED → GREEN)

- `TeamTargetSeamContractTests` — pins that no `ITeamTarget` method takes `MigrationEndpointOptions` and that the Teams infrastructure forges no `null!` endpoint arguments (EC-L1). RED: 2/2 failing before the change.
- `ConnectorCapabilityTests` (extended) — pins the new capability flags and the Simulated/ADO registration declarations (EC-H1). RED before the enum/registration change.
- `TeamExtensionParityTests` — pins byte-for-byte `settings.json` output from the core pipeline and the removal of the `TeamSettingsTeamExtension` type (EC-M3).
- `BoardConfigMergeToolTests` — pins the canonical seam location and the engine behaviour (state-map construction, invalid-mapping omission with parity semantics, case-insensitive merge) (EC-M4).
- `AdoPaginationExemptionContractTests` — pins the exemption markers at all four call sites and the connector-model documentation with API evidence (EC-M2).

## Consequences

- Capability is now a declaration, not an inference: adding a connector without declaring team/comment capability cleanly disables those flows; wiring a capability without its seam fails loud.
- The Teams package format is unchanged; older packages import identically (the legacy upgrader in `TeamsOrchestrator` still writes split artefacts).
- The pagination rule stays enforceable: every unpaged call is visibly exempted with evidence rather than silently non-compliant.
- Tests and hosts constructing extensions must supply a capability provider and non-null seams (a shared `TestConnectorCapabilities` helper covers tests).

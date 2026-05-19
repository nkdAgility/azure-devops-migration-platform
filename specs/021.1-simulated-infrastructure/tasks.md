# tasks.md for 021.1-simulated-infrastructure

## Phase: Assembly and DI
- [X] TSK-001: Simulated connector DI registrations for export/import/dependency analysis
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs:35-43,83-90,108-113

- [X] TSK-002: Move SimulatedWorkItemImportTarget to Infrastructure.Simulated
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs:18; not present in shared Infrastructure

- [X] TSK-003: CatalogService is connector-agnostic and in shared infrastructure
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/CatalogService.cs:15-20

## Phase: Model and Factory
- [X] TSK-004: Factories accept MigrationEndpointOptions (or superseded by endpoint-context DI)
  - Status: complete/superseded; completed because superseded by composite factories and endpoint-context DI
  - Supersession source: IWorkItemRevisionSourceFactory.CreateAsync(CancellationToken) and IWorkItemImportTargetFactory.CreateAsync(CancellationToken) in src/DevOpsMigrationPlatform.Abstractions.Agent

- [X] TSK-005: Connector-specific endpoint option leaves live in connector assemblies
  - Status: complete/superseded; completed because superseded by current placement in Abstractions
  - Supersession source: AzureDevOpsEndpointOptions.cs, SimulatedEndpointOptions.cs in Abstractions

- [X] TSK-006: OrganisationEndpoint moves inward from Abstractions
  - Status: complete/superseded; completed because superseded by current usage in Abstractions
  - Supersession source: OrganisationEndpoint remains in Abstractions

## Phase: Boundary and Leak Coverage
- [X] TSK-007: Remove cross-connector leaks in ADO import/resolution factories
  - Status: complete
  - Evidence: ADO factories no longer route Simulated by type string/class; connector dispatch is composite/keyed

## Phase: Scenario/Extension Coverage
- [X] TSK-008: Core simulated scenarios represented in scenario configs
  - Status: complete
  - Evidence: scenarios/queue-export-workitems-simulated-source.json, queue-import-workitems-simulated-target.json, queue-import-workitems-simulated-fixture.json, inventory-simulated.json, roundtrip-simulated.json

- [X] TSK-009: Attachment/comment/embedded-image simulated requirements implemented
  - Status: complete/superseded; completed because superseded by implemented classes
  - Supersession source: SimulatedAttachmentBinarySource.cs, SimulatedWorkItemCommentSource.cs, SimulatedEmbeddedImageDownloader.cs

- [ ] TSK-010: Link-topology requirements (Flat/Tree/TreeWithCrossLinks) specified and matched by link-analysis implementation
  - Status: incomplete
  - Evidence: Spec expects topology-driven link behavior; current link analysis yields empty results (SimulatedWorkItemLinkAnalysisService.cs:29-33)

## Phase: Non-Functional/Determinism
- [X] TSK-011: Deterministic-output requirements for synthetic revisions and attachments
  - Status: complete
  - Evidence: deterministic epoch/fields in revision source, deterministic byte generation in SimulatedAttachmentBinarySource

- [X] TSK-012: Streaming/non-buffered generation for synthetic data
  - Status: complete
  - Evidence: lazy yield return async stream in SimulatedWorkItemRevisionSource

# architecture.md

## Requirement Completeness

- [X] CHK001 Are Simulated connector DI registrations explicitly defined for export/import/dependency analysis? [Completeness, Spec §New Assembly; Spec §Keyed DI]
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs:35-43,83-90,108-113

- [X] CHK002 Is the move of SimulatedWorkItemImportTarget out of shared Infrastructure reflected in implementation boundaries? [Consistency, Spec §Must Move to Correct Assembly]
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs:18; no class found under shared Infrastructure.

- [X] CHK003 Is CatalogService connector-agnostic and placed in shared infrastructure as required? [Completeness, Spec §Must Move to Correct Assembly]
  - Status: complete
  - Evidence: src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/CatalogService.cs:15-20

## Requirement Clarity / Supersession

- [X] CHK004 Is the requirement that factories accept MigrationEndpointOptions still current, or superseded by endpoint-context DI? [Ambiguity, Spec §Factory interface changes]
  - Status: complete/superseded
  - Supersession source: IWorkItemRevisionSourceFactory.CreateAsync(CancellationToken) and IWorkItemImportTargetFactory.CreateAsync(CancellationToken) in src/DevOpsMigrationPlatform.Abstractions.Agent

- [X] CHK005 Is the requirement “connector-specific endpoint option leaves live in connector assemblies” still current? [Conflict, Spec §Config model changes]
  - Status: complete/superseded
  - Supersession source: connector option leaves are currently in Abstractions (AzureDevOpsEndpointOptions.cs:13, SimulatedEndpointOptions.cs:15).

- [X] CHK006 Is the requirement “OrganisationEndpoint moves inward from Abstractions” still current? [Conflict, Spec §OrganisationEndpoint moves inward]
  - Status: complete/superseded
  - Supersession source: OrganisationEndpoint remains in Abstractions (src/DevOpsMigrationPlatform.Abstractions/Organisations/OrganisationEndpoint.cs), and endpoint options still convert to it (MigrationEndpointOptions.cs:49-52).

## Boundary & Leak Coverage

- [X] CHK007 Are prior cross-connector leaks in ADO import/resolution factories removed? [Consistency, Spec §Boundary Violations]
  - Status: complete
  - Evidence: ADO factories no longer route Simulated by type string/class (AzureDevOpsWorkItemImportTargetFactory.cs, AzureDevOpsResolutionStrategyFactory.cs:48-51 only ADO target). Connector dispatch is composite/keyed (CompositeWorkItemImportTargetFactory.cs:40-50).

## Scenario / Extension Coverage

- [X] CHK008 Are core simulated scenarios explicitly represented in scenario configs? [Coverage, Spec §Summary of Work]
  - Status: complete
  - Evidence: scenarios/queue-export-workitems-simulated-source.json, queue-import-workitems-simulated-target.json, queue-import-workitems-simulated-fixture.json, inventory-simulated.json, roundtrip-simulated.json.

- [X] CHK009 Are attachment/comment/embedded-image simulated requirements now implemented (and therefore status text in spec updated)? [Consistency, Spec §Extension Registry]
  - Status: complete/superseded
  - Supersession source: implemented classes exist — SimulatedAttachmentBinarySource.cs:18, SimulatedWorkItemCommentSource.cs:16, SimulatedEmbeddedImageDownloader.cs:13.

- [ ] CHK010 Are link-topology requirements (Flat/Tree/TreeWithCrossLinks) specified with measurable expected output and matched by link-analysis implementation? [Coverage, Spec §Config model; Spec §Extension Registry]
  - Status: incomplete
  - Evidence: Spec expects topology-driven link behavior; current link analysis yields empty results (SimulatedWorkItemLinkAnalysisService.cs:29-33).

## Non-Functional / Determinism

- [X] CHK011 Are deterministic-output requirements explicit and implemented for synthetic revisions and attachments? [Measurability, Spec §Simulation Data]
  - Status: complete
  - Evidence: deterministic epoch/fields in revision source (SimulatedWorkItemRevisionSource.cs:21-23,69-88), deterministic byte generation (SimulatedAttachmentBinarySource.cs:40-47).

- [X] CHK012 Is streaming/non-buffered generation requirement explicit and implemented? [Acceptance Criteria, Spec §Config-Driven Generator]
  - Status: complete
  - Evidence: lazy yield return async stream in SimulatedWorkItemRevisionSource.cs:50-52,81-89

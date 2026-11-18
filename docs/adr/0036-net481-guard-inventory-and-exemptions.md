# ADR 0036 — net481 Guard Inventory and Exemptions

## Status

Accepted. Delivered with #171 ("Restore TFS export features compiled out on net481"), which Martin folded into #169 on 2026-11-17. Applies ADR-0018 and Constitution rule 10.

## Context

ADR-0018 allows a `#if !NET481` guard only where net481 genuinely cannot run the code. Every other guard is a feature the TFS agent silently lacks. In 2026-11, shared code had about 60 such blocks. On the TFS agent they compiled out WorkItems export comments, include/exclude filters, WIQL selection, referenced-path tracking, export extensions, polymorphic job deserialisation and the HTML image parser.

## Decision

1. **Restored on net481 (guard removed).** In `Infrastructure.Agent`:
   - `WorkItemExportOrchestrator` and its factory: export extensions such as Comments, and referenced-path tracking.
   - `WorkItemsOrchestrator`: include/exclude filters, WIQL selection, the Comments extension and referenced paths.
   - `InventoryService`: the org scope fetch.
   - `EmbeddedImageReferenceTool`: HTML parsing with HtmlAgilityPack.
   - `AgentWorkerBase` and `ModulePipelineWorkerBase`: the polymorphic converters.
   - `IdentitiesOrchestrator`: logging data scopes.
   - `IdentityServiceCollectionExtensions`: identity mapping and matching strategies.
   - `TeamMigrationOrchestrator`: team area-path recording.
   - `NodeTranslationToolServiceCollectionExtensions`: the validators, referenced paths and classification capture.

   In `Abstractions`, `MigrationPlatformOptionsScope.Parameters` is now JSON on both runtimes. The TFS connector declares `WorkItemComments` and supplies a comments REST source, served by TFS 2019+ and Azure DevOps.

2. **Exempt: runtime incompatibility.** `ISourceEndpointInfo.OrganisationSlug` and `ITargetEndpointInfo.OrganisationSlug` are default interface members, which net481 cannot run. On net481 every implementer provides the member. Nothing is lost. Marked `// net481-exempt: ADR-0036 default-interface-member`.

3. **Exempt: TFS as a migration target.** Whether TFS is a supported target is undecided (Decision #139; #158 and #172 track the work). Until that is ruled, these stay compiled out:
   - Nodes import: `INodesOrchestrator.ImportAsync`, `NodesModule` target endpoint, `NodesOrchestrator` replication and node creation, and the `WorkItemsNodeReadinessOrchestrator` fallback.
   - Teams import: `ITeamsOrchestrator.ImportAsync`, `TeamsModule` import, `TeamsOrchestrator.ImportAsync`.

   The TFS agent never runs Prepare or Import while it advertises only the TeamFoundationServer connector as a source (ADR-0035). Marked `// net481-exempt: ADR-0036 tfs-target`. A ruling on #139 replaces this exemption with an implementation or a permanent "not supported".

4. **Pending inventory (#215).** These guards sit outside #171's scope, which is `Infrastructure.Agent` and `Abstractions.Agent`:
   - polymorphic options registration in `Infrastructure/Config/MigrationPlatformServiceExtensions`;
   - package-access metrics in `Infrastructure.Storage.FileSystem/ActivePackageAccess`.

   They are marked `// net481-pending: #215` and are not exemptions.

5. **Enforced.** `Net481GuardClauseTests.EveryNet481Guard_RecordsItsExemptionOrTrackingItem` fails on any `#if !NET481` in `src/` without one of these markers.

## Consequences

- The TFS agent now exports comment edits, comment images, filters, WIQL selection and referenced paths, as the Azure DevOps agent does.
- A new guard needs a recorded reason, or the test fails.
- Exemptions in point 3 end with the #139 ruling. Pending items in point 4 end with #215.

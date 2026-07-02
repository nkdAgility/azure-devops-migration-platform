# ADR 0026 — Tool-Contract Purification: Embedded-Image, Identity-Translation, and Field-Transform Tools

## Status

Accepted

Executes architecture-audit items **TC-H2**, **TC-M1**, **TC-M2**, and **TC-L3** as one themed Class C change under explicit operator consent. **TC-H1 (`AttachmentReplayTool`) is explicitly DEFERRED** pending the operator's Tool-taxonomy ruling; nothing in this ADR pre-empts that decision.

## Context

The Tool contract requires Tools to be pure, stateless, deterministic, and phase-agnostic engines: no I/O, no per-job state, no hidden lifecycle. The architecture audit found four violations:

1. **TC-H2** — `EmbeddedImageRewriteTool` (WorkItems/Attachments) mixed a pure parse/rewrite engine with impure replay orchestration: package binary reads and `IWorkItemTarget.UploadEmbeddedImageAsync` uploads inside a type named and positioned as a Tool.
2. **TC-M1** — `IdentityTranslationTool` owned per-job mutable state: it loaded `descriptors.jsonl`, `mapping.json`, and `prepared-identities.json` from the package at initialisation, cached the resulting dictionaries, and wrote `Identities/unresolved.json` — package I/O and job-scoped state inside a Tool.
3. **TC-M2** — `FieldTransformTool` was registered scoped because it captured per-job `FieldTransformOptions` at construction, contradicting the Tool contract's singleton expectation.
4. **TC-L3** — embedded-image reference parsing/rewriting logic was duplicated between the import path (`EmbeddedImageRewriteTool`) and the export path (`EmbeddedImageExportService`), with no canonical seam.

## Decision

**A Tool is a pure engine; all I/O and per-job state live with services and orchestrators.** Each finding was remediated by splitting the pure engine out and relocating the impure half, preserving behaviour:

1. **TC-H2 / TC-L3 — `IEmbeddedImageReferenceTool`** (`DevOpsMigrationPlatform.Abstractions.Agent.Tools`): the single canonical embedded-image reference engine. It exposes the import-side surface (`ParseImageReferences`, `RewriteImageUrls` — ordinal find/replace) and the export-side HTML/Markdown surfaces (`ParseHtmlImageSources`, `RewriteHtmlImageSources`, `ParseMarkdownImageReferences`, `RewriteMarkdownImageReferences`), pinning the deliberately different detection rules of each path rather than silently merging them. `EmbeddedImageReferenceTool` (Infrastructure.Agent/Tools/EmbeddedImages) is the stateless singleton implementation. Both paths consume it: the new **`EmbeddedImageReplayService`** (WorkItems/Attachments) carries the impure import half of the former `EmbeddedImageRewriteTool` (package binary reads, target uploads, field rewriting), and **`EmbeddedImageExportService`** now delegates its HTML/Markdown parsing and rewriting to the same engine instead of its private duplicates. `EmbeddedImageRewriteTool` is deleted.
2. **TC-M1 — pure `IIdentityTranslationTool`**: the tool is now a stateless engine with three pure operations — `ParseTranslationInputs(descriptorsJsonl, mappingJson, preparedIdentitiesJson) -> IdentityTranslationMap` (raw artefact text in, immutable case-insensitive map out; malformed inputs non-fatal), `Translate(sourceIdentity, map)` (resolution order preserved: explicit override → Prepare-phase UPN/display-name match → configured default → pass-through), and `ComputeUnresolved(map)`. Map ownership moved to the Identities module: **`IIdentitiesOrchestrator.TranslationMap`** exposes the job's resolved map, and `IdentitiesOrchestrator` owns reading the package artefacts and persisting `Identities/unresolved.json`. Consumers (`WorkItemResolutionProcessor`, `TeamMembersTeamExtension`) pass `_identitiesOrchestrator?.TranslationMap ?? IdentityTranslationMap.Empty` into `Translate`.
3. **TC-M2 — `FieldTransformTool` singleton**: the tool is now registered as a DI singleton per the Tool contract. Per-job configuration is honoured via config-accessor indirection: the singleton constructor takes `ICurrentPackageConfigAccessor` + `IOptionsFactory<FieldTransformOptions>` and rebuilds its cached pipeline whenever the current package configuration changes, so each job sees the options from its own `migration-config.json` while the tool holds no per-job state. A fixed-options constructor remains for tests and direct composition. `.agents/10-contracts/specs/field-transform-contract.md` was reviewed and contains no lifetime mandate, so no contract-text change was required.

### TC-H1 deferral

`AttachmentReplayTool` exhibits the same engine/orchestration mixing, but the operator has reserved the ruling on whether replay-shaped types are Tools at all (Tool taxonomy). The split pattern used here (pure engine + `*ReplayService`) was chosen precisely because it does not pre-empt that ruling: whichever way the taxonomy lands, `IEmbeddedImageReferenceTool` remains a valid pure seam and `EmbeddedImageReplayService` can be renamed/reclassified without contract churn.

## Contract Tests

- `Tools/EmbeddedImages/EmbeddedImageReferenceToolTests` — pins the engine's canonical location in Abstractions.Agent (TC-L3) and the import-side and export-side parse/rewrite behaviours previously duplicated across the two paths.
- `Tools/EmbeddedImages/EmbeddedImageReplayServiceTests` — the replay-orchestration behaviours ported verbatim from the deleted `EmbeddedImageRewriteToolTests` (upload + URL rewrite; Markdown relative-image candidate inference).
- `Tools/IdentityTranslation/IdentityTranslationToolPurityTests` — pins the pure-engine shape, `ParseTranslationInputs` parsing (including malformed-input tolerance), the four-step resolution order, and `ComputeUnresolved`.
- `Tools/FieldTransform/FieldTransformToolLifetimeTests` — pins the singleton registration and per-job option re-resolution through the config accessor.

## Alternatives Considered

- **Keeping `EmbeddedImageRewriteTool` and injecting the engine into it** — rejected: the type would still be a Tool performing package reads and target uploads; the name/contract mismatch is the finding.
- **One merged parse/rewrite algorithm for export and import** — rejected: the paths have deliberately different detection semantics (import's whitespace-terminated Markdown URL and whole-text ordinal replace vs. export's greedy-to-paren Markdown URL and DOM-based HTML rewrite); merging would be a behaviour change outside the consented scope.
- **Scoped `FieldTransformTool` with a documented contract exemption** — rejected: the config-accessor indirection achieves per-job options under a singleton with no contract carve-out.
- **Remediating TC-H1 in the same change** — rejected: explicitly reserved by the operator.

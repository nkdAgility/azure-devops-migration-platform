# Research: Work Item Field Transformation

**Feature**: 022-workitem-field-mapping  
**Date**: 2026-04-24  
**Status**: Complete

## Research Questions

### RQ-1: How does RevisionFolderProcessor integrate with tools?

**Finding**: `RevisionFolderProcessor` processes import in four sequential stages:
- Stage A: `CreatedOrUpdated` — create/resolve target work item
- Stage B: `AppliedFields` — apply field values with identity resolution
- Stage C: `AppliedLinks` — add links
- Stage D: `UploadedAttachments` — stream binaries

The FieldTransformTool must execute **within Stage B** (or immediately before it), transforming the field dictionary before fields are applied to the target. For export-phase transforms, the tool hooks into `WorkItemExportOrchestrator` after building `revision.json` content but before writing it to `IArtefactStore`.

**Integration point**: The tool receives a `Dictionary<string, object?>` (field name → value) and returns a modified copy. The caller (Stage B for import, orchestrator for export) is responsible for reading/writing `revision.json`.

### RQ-2: What is the existing extension + tool resolution pattern?

**Finding**: The current codebase has no existing "Tool" concept in the module/extension model. Extensions are named sub-data collections (`Revisions`, `Links`, `Attachments`, etc.) declared in `WorkItemsModuleExtensions`. The spec introduces the Tool concept as a cross-cutting service declared at `MigrationPlatform.Tools.*`.

**Architecture gap**: `docs/architecture.md`, `docs/modules.md`, and `docs/configuration.md` do not yet describe the Tool concept. This is logged in `discrepancies.md` items 1–3. These docs must be updated during implementation.

**Design decision**: The Tool is registered as a singleton DI service (`IFieldTransformTool`). Extensions reference it by name in config (`tools.FieldTransform`) and declare the phase. At runtime, the revision processor checks whether the FieldTransform tool is enabled for the current phase and invokes it if so.

### RQ-3: How should polymorphic transform resolution work?

**Finding**: The spec defines 14 transform types, each identified by a `type` discriminator string (e.g., `"CopyField"`, `"MapValue"`). The `System.Text.Json` polymorphic serialization in .NET 10 supports `[JsonDerivedType]` attributes on a base type for discriminator-based deserialization.

**Design decision**: 
1. Define `IFieldTransform` interface with `string Type { get; }` and `TransformResult Apply(IDictionary<string, object?> fields, FieldTransformContext context)`.
2. Define `FieldTransformRuleOptions` as a base options class with `[JsonDerivedType]` attributes for each of the 14 concrete options types.
3. Alternatively (simpler): Use a factory pattern — `IFieldTransformFactory.Create(FieldTransformRuleOptions rule)` that switches on `rule.Type` to construct the appropriate `IFieldTransform` implementation. This avoids complex polymorphic deserialization and aligns with the existing factory patterns in the codebase (e.g., `IWorkItemRevisionSourceFactory`).

**Recommendation**: Factory pattern. It's consistent with existing patterns and avoids tight coupling between JSON serialization and transform types.

### RQ-4: What expression evaluator to use for CalculateFieldTransform?

**Finding**: FR-019 restricts expressions to arithmetic (`+`, `-`, `*`, `/`, `%`), string concatenation, and field references. No method calls, no lambdas, no reflection.

**Options evaluated**:
1. **NCalc** — lightweight expression evaluator, supports arithmetic and custom functions. Well-maintained, widely used. Can be restricted by not registering functions.
2. **DataTable.Compute** — built-in, but limited to numeric expressions and has security concerns.
3. **Custom parser** — full control but high implementation cost for a well-understood problem.

**Recommendation**: NCalc or a similarly restricted evaluator. The expression language is simple enough that a custom recursive-descent parser is also viable if zero external dependencies are preferred. Decision deferred to implementation — the interface (`IExpressionEvaluator`) will abstract this choice.

### RQ-5: How do tag transforms handle the dedup requirement?

**Finding**: FR-022 requires all tag-producing transforms (ConditionalTag, FieldToTag, MergeToTag, TreeToTag) to use `"; "` separator and deduplicate case-insensitively.

**Design decision**: Tag deduplication is a post-processing step, not per-transform. After the entire transform pipeline completes for a revision, if `System.Tags` was modified by any tag transform, a final deduplication pass runs:
1. Split `System.Tags` by `"; "` (and handle edge cases: `;`, `; `, trailing separators).
2. Deduplicate case-insensitively (keep first occurrence's casing).
3. Rejoin with `"; "`.

This keeps individual tag transforms simple — they just append. The pipeline handles dedup.

### RQ-6: How does prepare-time validation access source/target field definitions?

**Finding**: FR-020 requires validating field references against actual field definitions in both source and target. The existing codebase has `IWorkItemFieldService` (or similar) for querying field metadata from ADO REST API.

**Design decision**: `IFieldTransformValidator` receives `IFieldDefinitionProvider` (new interface in Abstractions) that abstracts field metadata retrieval. For ADO, this calls the WorkItem Fields REST API. For Simulated connector, it returns a predefined set. The validator:
1. Resolves all configured field names against source fields.
2. Resolves all target field names against target fields.
3. Checks type compatibility (string→string OK, string→integer requires explicit conversion).
4. For MapValue transforms, checks picklist values against target allowed values.
5. Runs sample dry-run on N work items (default 10).

### RQ-7: What is the existing IOptions registration pattern for nested config?

**Finding**: The codebase uses `IOptions<T>` with `BindConfiguration(T.SectionName)` and sealed classes with `init`-only properties. Nested options (e.g., `WorkItemsModuleOptions.Extensions`) are modeled as nested sealed classes bound to sub-sections.

For the FieldTransform tool, the binding path is `MigrationPlatform:Tools:FieldTransform`. The `transformGroups` array binds naturally to `List<FieldTransformGroupOptions>` in the options class.

**Design decision**: Follow the established pattern:
```csharp
public sealed class FieldTransformOptions
{
    public static string SectionName => "MigrationPlatform:Tools:FieldTransform";
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<FieldTransformGroupOptions> TransformGroups { get; init; } = [];
}
```

## Open Questions (None)

All research questions resolved. No items require clarification before Phase 1 design.

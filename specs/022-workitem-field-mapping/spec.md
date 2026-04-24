# Feature Specification: Work Item Field Transformation

**Feature Branch**: `022-workitem-field-mapping`  
**Created**: 2026-04-24  
**Status**: Draft  
**Input**: User description: "M1: WorkItemsModule — FieldMapping from analysis/proposed-features.md. Check against azure-devops-migration-tools field mapping tool and advise on screaming architecture naming."

## Architecture References

| File | Status |
|------|--------|
| `docs/architecture.md` | Confirmed accurate — tool resolution model not yet documented (discrepancy logged) |
| `docs/modules.md` | Confirmed accurate — WorkItemsModule and extension model documented; no tool injection model yet (discrepancy logged) |
| `docs/configuration.md` | Confirmed accurate — `tools[]` top-level section not yet documented (discrepancy logged) |
| `.agents/guardrails/system-architecture.md` | Confirmed accurate — rules 6 (no direct S→T), 7 (IArtefactStore only), 8 (identity cross-cutting), 14 (lexicographic enumeration), 21 (mandatory reuse) all apply |
| `.agents/context/package-format.md` | Confirmed accurate — revision.json is the write target |
| `.agents/context/workitems-format.md` | Confirmed accurate — field values stored in revision.json |
| `analysis/proposed-features.md` | Source — M1 and T1 sections define the 14 map types and tool resolution model |

## Clarifications

### Session 2026-04-24

- Q: In which phase(s) should transforms run? → A: Both phases supported — each tool reference declares `phase: export | import | both` with **import** as the default. Export captures raw source data for auditability; transforms apply at import time by default.

## Naming Convention

Names follow Screaming Architecture: each name describes *what the transform does*, not what category it belongs to. The `Transform` suffix communicates the role unambiguously.

**Parent tool**: `FieldTransformTool` (interface: `IFieldTransformTool`)

| Transform | Type Discriminator | Purpose |
|---|---|---|
| `CopyFieldTransform` | `CopyField` | Copies one field value to another field |
| `CopyFieldBatchTransform` | `CopyFieldBatch` | Copies multiple fields in a single declaration |
| `SetFieldTransform` | `SetField` | Sets a field to a literal value |
| `MapValueTransform` | `MapValue` | Remaps discrete values via a lookup table |
| `MergeFieldsTransform` | `MergeFields` | Merges N fields into one using a format template |
| `CalculateFieldTransform` | `CalculateField` | Computes a value from an arithmetic/string expression |
| `ClearFieldTransform` | `ClearField` | Nulls out a field value |
| `ExcludeFieldTransform` | `ExcludeField` | Removes the field entirely from the revision |
| `ConditionalTagTransform` | `ConditionalTag` | Appends a tag when a field value matches a pattern |
| `FieldToTagTransform` | `FieldToTag` | Copies a field value as a tag |
| `MergeToTagTransform` | `MergeToTag` | Merges multiple fields into a tag-style value |
| `ConditionalFieldTransform` | `ConditionalField` | Conditionally sets a field based on multi-field evaluation |
| `RegexFieldTransform` | `RegexField` | Regex find-and-replace on a field value |
| `TreeToTagTransform` | `TreeToTag` | Flattens a tree path into tag values |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remap Field Values Between Process Templates (Priority: P1)

As a migration operator migrating from an Agile process template to a Scrum process template, I want to declare value-mapping rules (e.g., `Active → In Progress`, `Resolved → Done`) so that State and other picklist fields contain valid values in the target process.

**Why this priority**: Value remapping is the single most common field transformation need. Without it, every cross-process-template migration produces invalid field values, causing import failures or data corruption.

**Independent Test**: Can be fully tested by exporting a package with Agile-style field values, configuring a MapValueTransform, and verifying the imported target contains the remapped values.

**Acceptance Scenarios**:

1. **Given** a MapValueTransform configuration with a value lookup for `System.State` (`Active → In Progress`), **When** a revision with `System.State = Active` is processed during import, **Then** the field value sent to the target is `System.State = In Progress`.
2. **Given** a MapValueTransform lookup that does not contain the source value, **When** a revision with that unmapped value is processed, **Then** the original value is preserved and a warning is logged.
3. **Given** a transform with `applyTo: ["User Story"]`, **When** a Bug revision is processed, **Then** the transform is skipped for that revision.
4. **Given** multiple transforms declared in sequence, **When** a revision is processed, **Then** transforms are applied in declaration order and each receives the output of the previous transform.

---

### User Story 2 - Copy or Rename Fields (Priority: P1)

As a migration operator, I want to copy a field value from one field name to another (e.g., `Custom.OldField → Custom.NewField`) so that data migrates correctly when target field names differ from source field names.

**Why this priority**: Field renaming is the second most common need — organisations rename custom fields when migrating. Equally critical as value remapping for a successful migration.

**Independent Test**: Can be fully tested by exporting a revision with a source field, applying a CopyFieldTransform, and verifying the target field appears in the import output.

**Acceptance Scenarios**:

1. **Given** a CopyFieldTransform from `Custom.OldField` to `Custom.NewField`, **When** the source revision contains `Custom.OldField = "hello"`, **Then** the output revision contains `Custom.NewField = "hello"`.
2. **Given** a CopyFieldTransform with a default value, **When** the source revision does not contain the source field, **Then** the output revision contains the target field set to the default value.
3. **Given** a CopyFieldTransform where the source field is empty, **When** the revision is processed, **Then** the empty value is copied (default value is only used when the field is absent, not when it is empty).
4. **Given** a CopyFieldTransform where the target field already has a value, **When** the revision is processed, **Then** the target field is unconditionally overwritten with the source value.

---

### User Story 3 - Exclude or Clear Unwanted Fields (Priority: P2)

As a migration operator, I want to exclude certain fields from the revision entirely or set them to null, so that internal-only or obsolete fields do not pollute the target project.

**Why this priority**: Cleaning up legacy fields is essential for a tidy migration but does not block basic migration functionality.

**Independent Test**: Can be fully tested by applying an ExcludeFieldTransform and verifying the field is absent from the import output, or applying a ClearFieldTransform and verifying the field is null.

**Acceptance Scenarios**:

1. **Given** an ExcludeFieldTransform for `Custom.InternalOnly`, **When** a revision containing that field is processed, **Then** the field is removed from the output entirely.
2. **Given** a ClearFieldTransform for `Custom.LegacyId`, **When** a revision containing that field is processed, **Then** the field value is set to null in the output.
3. **Given** an ExcludeFieldTransform for a field that does not exist in the revision, **When** the revision is processed, **Then** processing succeeds silently (no error).

---

### User Story 4 - Set Literal and Computed Values (Priority: P2)

As a migration operator, I want to stamp each migrated work item with a literal marker (e.g., `Custom.MigratedBy = "migration-platform"`) or a computed value, so that I can identify migrated items and derive calculated fields.

**Why this priority**: Audit trails and computed fields add significant value but are not blocking for basic migrations.

**Independent Test**: Can be tested by configuring a SetFieldTransform and verifying the literal value appears, or configuring a CalculateFieldTransform and verifying the computed result.

**Acceptance Scenarios**:

1. **Given** a SetFieldTransform setting `Custom.MigratedBy` to `"migration-platform"`, **When** any revision is processed, **Then** `Custom.MigratedBy = "migration-platform"` appears in the output.
2. **Given** a CalculateFieldTransform with an arithmetic expression referencing other fields, **When** the revision is processed, **Then** the target field contains the computed result.
3. **Given** a CalculateFieldTransform expression that references a field not present in the revision, **When** the revision is processed, **Then** an error is reported for that revision and the transform is skipped.

---

### User Story 5 - Transform Fields into Tags (Priority: P3)

As a migration operator, I want to convert field values or tree paths into tags, so that hierarchical data (like area paths) can be preserved as flat tags when the target project has a different node structure.

**Why this priority**: Tag-based fallback for tree structures is a common advanced migration scenario, but most operators handle it via NodeStructureTool first.

**Independent Test**: Can be tested by configuring a TreeToTagTransform on `System.AreaPath` and verifying that `System.Tags` in the output contains the flattened path segments.

**Acceptance Scenarios**:

1. **Given** a FieldToTagTransform on `System.AreaPath`, **When** a revision has `System.AreaPath = "Project\Team\Sprint"`, **Then** `System.Tags` includes `"Project; Team; Sprint"` (semicolon-space separated).
2. **Given** a ConditionalTagTransform with pattern `^Resolved$` on `System.State`, **When** a revision has `System.State = Resolved`, **Then** `System.Tags` includes `"Resolved"`.
3. **Given** a ConditionalTagTransform, **When** a revision does not match the pattern, **Then** no tag is added.
4. **Given** a MergeToTagTransform merging `System.Tags` and `Custom.Labels`, **When** both fields have values, **Then** the output `System.Tags` contains the union of both, deduplicated (case-insensitive).

---

### User Story 6 - Regex Field Cleanup (Priority: P3)

As a migration operator, I want to apply regex find-and-replace to field values so that I can clean up prefixes, suffixes, or formatting inconsistencies during migration.

**Why this priority**: Useful for cosmetic cleanup but not required for data integrity.

**Independent Test**: Can be tested by configuring a RegexFieldTransform with a pattern and replacement and verifying the output field value.

**Acceptance Scenarios**:

1. **Given** a RegexFieldTransform on `System.Title` with pattern `^\[OLD\]\s*` and replacement `""`, **When** a revision has title `[OLD] My Item`, **Then** the output title is `My Item`.
2. **Given** a RegexFieldTransform where the pattern does not match, **When** the revision is processed, **Then** the field value is unchanged.

---

### User Story 7 - Merge Multiple Fields (Priority: P3)

As a migration operator, I want to merge multiple source fields into a single target field using a format template, so that I can consolidate data during migration.

**Why this priority**: Merge is an advanced transformation; most migrations use simple copy or value remapping.

**Independent Test**: Can be tested by configuring a MergeFieldsTransform with a format string and verifying the output.

**Acceptance Scenarios**:

1. **Given** a MergeFieldsTransform merging `System.Title` and `Custom.Subtitle` with format `"{0} — {1}"`, **When** both fields have values, **Then** the target field contains `"My Title — My Subtitle"`.
2. **Given** a MergeFieldsTransform where one source field is absent, **When** the revision is processed, **Then** the absent field is treated as empty string in the format.

---

### Edge Cases

- What happens when a transform references a field not present in the revision? Each transform type defines its own behaviour: copy uses default value, calculate reports error, clear/exclude succeed silently.
- What happens when two transforms modify the same field? They are applied in sequence — the second transform sees the output of the first.
- What happens when the `applyTo` filter is empty or omitted? The transform applies to all work item types.
- What happens when a value-mapping transform encounters a null field value? Null is treated as a distinct value; if null is not in the lookup, the original null is preserved.
- What happens when a regex transform has an invalid pattern? Configuration validation rejects the pattern before any processing begins.
- What happens when transforms are configured but the module has no revisions to process? The tool is loaded but never invoked — no error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support declaring field transforms at the migration-platform level (`tools[]` in configuration) with a unique identifier per transform set.
- **FR-002**: System MUST support the following 14 transform types: CopyField, CopyFieldBatch, SetField, MapValue, MergeFields, CalculateField, ClearField, ExcludeField, ConditionalTag, FieldToTag, MergeToTag, ConditionalField, RegexField, TreeToTag.
- **FR-003**: Each transform type MUST accept an optional `applyTo` filter — a list of work item type names. When specified, the transform is only applied to revisions of matching types. When empty or omitted, it applies to all types.
- **FR-004**: Transforms MUST be applied in declaration order within a transform set. Each transform receives the output of the previous transform.
- **FR-005**: The transform tool MUST be a pure transformation — it receives a revision's field collection and returns a modified copy. No I/O, no API calls, no side effects.
- **FR-006**: Extensions (e.g., `WorkItems/Revisions`) MUST load transforms by reference to a tool ID declared at the platform level and MAY override selected values (e.g., `applyTo`) for that extension only.
- **FR-007**: Effective transform configuration MUST be: platform-level defaults merged with extension-level overrides.
- **FR-008**: The system MUST validate all transform configurations at startup before processing any revisions. Invalid configurations (unknown fields, invalid regex patterns, circular references) MUST cause a fail-fast with a clear error message.
- **FR-009**: Each transform execution MUST be logged with structured telemetry: transform type, source/target fields, work item ID, and whether the transform modified the revision.
- **FR-010**: The CopyField transform MUST support a default value used when the source field is absent from the revision.
- **FR-011**: The MapValue transform MUST preserve the original value when no mapping match is found and log a warning.
- **FR-012**: The ExcludeField transform MUST remove the field entirely from the revision before any write operation — this is not import-only.
- **FR-013**: The CalculateField transform MUST support safe arithmetic and string expressions over field values, with no ability to execute arbitrary code.
- **FR-014**: The RegexField transform MUST validate the regex pattern at configuration load time and reject invalid patterns.
- **FR-015**: Transforms MUST NOT alter identity fields. Identity mapping remains the exclusive responsibility of `IIdentityMappingService` (guardrail rule 8).
- **FR-016**: Transform processing MUST be stateless across revisions — no transform may accumulate state from one revision to another.
- **FR-017**: Each tool reference on an extension MUST declare the phase in which it applies (`export`, `import`, or `both`). The default phase MUST be `import`. Export-phase transforms modify `revision.json` before it is written to the package; import-phase transforms modify field values before they are sent to the target. The configuration documentation MUST include guidance on recommended phase per transform type.

### Key Entities

- **FieldTransformTool**: A named, reusable collection of field transform rules declared at the platform configuration level. Has a unique `id`, an optional `applyTo` filter, and an ordered list of transform rules.
- **FieldTransformRule**: A single transformation instruction with a `type` discriminator, source/target field references, and type-specific parameters (e.g., value lookup table, regex pattern, format string).
- **WorkItemRevision (fields collection)**: The set of field name/value pairs within a `revision.json` that transforms operate on. The transform tool receives this collection and returns a modified copy.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A migration operator can configure a cross-process-template migration (Agile → Scrum) with value remapping and complete the migration with zero invalid-field-value errors.
- **SC-002**: All 14 transform types can be configured, validated, and applied in a single migration run without errors.
- **SC-003**: A migration of 10,000 work items with 5 active transforms completes with no measurable throughput degradation compared to a migration with no transforms.
- **SC-004**: An operator with no prior experience can configure a basic field copy + value remap transform set in under 10 minutes using the configuration documentation and examples.
- **SC-005**: 100% of transform executions produce structured telemetry entries allowing an operator to audit exactly which transforms were applied to each revision.

## Assumptions

- The operator is a migration administrator familiar with the platform's configuration file format and the concept of field mappings from migration tools.
- Transforms operate during the import phase by default (reading from `revision.json` and transforming before applying to target). Each tool reference on an extension declares `phase: export | import | both` (default: `import`). Export captures raw source data for auditability — transforms at export time are opt-in. The configuration documentation MUST include guidance on recommended phase for each transform type.
- Identity fields (`System.AssignedTo`, `System.CreatedBy`, `System.ChangedBy`) remain handled by `IIdentityMappingService` and are not transformed by this tool.
- The `CalculateField` expression language will use a safe, sandboxed evaluator (the specific evaluator library is an implementation detail not specified here).
- The `tools[]` top-level configuration section described in `analysis/proposed-features.md` is the intended design and will be added to `docs/configuration.md` as part of this feature.
- The 14 transform types defined in `analysis/proposed-features.md` represent the complete set for this feature; additional types may be added in future features.
- Node structure mapping (area/iteration paths) is handled by a separate `NodeStructureTool` (T2) and is out of scope for this feature.
- Work item type remapping is handled by a separate `WorkItemTypeMappingTool` (T3) and is out of scope for this feature.

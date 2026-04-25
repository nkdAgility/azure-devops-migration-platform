# Tasks: Work Item Field Transformation

**Input**: Design documents from `/specs/022-workitem-field-mapping/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

> **⚠️ 021.2 Project Boundary Update (2026-04-25)**: All file paths have been updated per the
> 021.2 Separation of Concerns spec. Agent-only tool interfaces go in `Abstractions.Agent/Tools/`,
> config-schema options stay in `Abstractions/Options/`, implementations go in
> `Infrastructure.Agent/Tools/FieldTransform/`, and tests go in `Infrastructure.Agent.Tests/`.
> The original spec referenced `Abstractions/Tools/` and `Infrastructure/Tools/` — those are now incorrect.

**Tests**: Included — this project follows ATDD-first (Constitution VIII). Each user story gets a Gherkin `.feature` file and Reqnroll step definitions. Unit tests for every method with branching logic.

**Organization**: Tasks grouped by user story. Each story is independently implementable and testable via the ATDD inner loop (Specification → Test Gen → Implementation → Review).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the foundational type system — interfaces, records, options, enums — that all user stories depend on. No implementations yet.

- [ ] T001 Define `FieldTransformPhase` enum (`Export`, `Import`, `Both`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldTransformPhase.cs` — addresses architecture review HX-M1
- [ ] T002 [P] Define `FieldTransformContext` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldTransformContext.cs` — includes `WorkItemId`, `RevisionIndex`, `WorkItemType`, `Phase` (using `FieldTransformPhase` enum)
- [ ] T003 [P] Define `FieldTransformAction` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldTransformAction.cs` — telemetry data: group name, transform name, type, field, modified flag, old/new values
- [ ] T004 [P] Define `FieldTransformResult` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldTransformResult.cs` — contains modified fields dictionary and actions list
- [ ] T005 [P] Define `FieldTransformValidationReport` and `FieldTransformValidationEntry` records with `FieldTransformValidationSeverity` enum in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldTransformValidationReport.cs`
- [ ] T006 [P] Define `FieldDefinition` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/FieldDefinition.cs` — field metadata: `ReferenceName`, `Name`, `Type`, `IsReadOnly`, `AllowedValues`
- [ ] T007 Define `IFieldTransform` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldTransform.cs` — `Type`, `Name` properties and `Apply(fields, context)` method returning `FieldTransformResult`
- [ ] T008 [P] Define `IFieldTransformTool` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldTransformTool.cs` — `ApplyTransforms(fields, context)` and `IsEnabledForPhase(phase)` methods
- [ ] T009 [P] Define `IFieldTransformFactory` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldTransformFactory.cs` — `Create(options, groupName, ordinal)` returning `IFieldTransform`
- [ ] T010 [P] Define `IFieldTransformValidator` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldTransformValidator.cs` — `ValidateAsync()` with `CancellationToken`; inject `IFieldDefinitionProviderFactory` via constructor per CA-M1
- [ ] T011 [P] Define `IFieldDefinitionProvider` and `IFieldDefinitionProviderFactory` interfaces in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldDefinitionProvider.cs` — `GetFieldDefinitionsAsync(workItemType?, ct)` and factory methods `CreateSourceProvider()` / `CreateTargetProvider()`
- [ ] T011b [P] Define `IExpressionEvaluator` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IExpressionEvaluator.cs` — `Evaluate(expression, fieldValues)` returning computed value; abstracts expression evaluation for CalculateFieldTransform (Constitution V: interfaces in Abstractions.Agent per 021.2)
- [ ] T012 Define sealed `FieldTransformOptions` class in `src/DevOpsMigrationPlatform.Abstractions/Options/FieldTransformOptions.cs` — `SectionName = "MigrationPlatform:Tools:FieldTransform"`, `Enabled`, `TransformGroups` list
- [ ] T013 [P] Define sealed `FieldTransformGroupOptions` class in `src/DevOpsMigrationPlatform.Abstractions/Options/FieldTransformGroupOptions.cs` — `Name?`, `Enabled`, `ApplyTo?`, `Transforms` list
- [ ] T014 [P] Define sealed `FieldTransformRuleOptions` class in `src/DevOpsMigrationPlatform.Abstractions/Options/FieldTransformRuleOptions.cs` — flat property bag with `Type` discriminator and all type-specific properties (per CA-H1: serialization boundary only)
- [ ] T015 [P] Define sealed `FieldTransformExtensionOptions` class in `src/DevOpsMigrationPlatform.Abstractions/Options/FieldTransformExtensionOptions.cs` — `Enabled`, `Phase` (using `FieldTransformPhase` enum)
- [ ] T016 Verify build: run `dotnet clean && dotnet build --no-incremental` — all new types must compile with zero warnings

**Checkpoint**: All interfaces, records, and options types exist in Abstractions. No implementations yet. Build passes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core pipeline infrastructure — the `FieldTransformTool`, `FieldTransformPipeline`, `FieldTransformFactory`, DI registration, and identity field guard. These MUST be complete before any individual transform type can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T017 Implement `FieldTransformFactory` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformFactory.cs` — switches on `Type` discriminator, validates required properties per type, projects flat `FieldTransformRuleOptions` into strongly-typed per-transform option records (CA-H1), generates default names per FR-009 naming rules (`{GroupName}.{Type}{ordinal}`)
- [ ] T018 Implement `FieldTransformPipeline` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformPipeline.cs` — executes groups in order, transforms within groups in order (FR-004), applies `applyTo` filtering at group and transform level (FR-003), respects `enabled` at all three levels (FR-007), collects `FieldTransformAction` telemetry (FR-009), runs tag deduplication post-pass on `System.Tags` if any tag transform fired (FR-022)
- [ ] T019 Implement `FieldTransformTool` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformTool.cs` — `IFieldTransformTool` implementation: constructs pipeline from `IOptions<FieldTransformOptions>` + `IFieldTransformFactory`, delegates to `FieldTransformPipeline.Execute()`, implements `IsEnabledForPhase()`, validates identity field guard at construction (FR-021), emits FR-023 warning when >100 transforms, validates config at startup (FR-008)
- [ ] T020 Create `FieldTransformToolServiceCollectionExtensions` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformToolServiceCollectionExtensions.cs` — `AddFieldTransformToolServices(this IServiceCollection)` registering `IFieldTransformTool`, `IFieldTransformFactory`, `IFieldTransformValidator` as singletons with `IOptions<FieldTransformOptions>` binding (per MM-M2 naming)
- [ ] T020b Wire `IFieldTransformTool` into `RevisionFolderProcessor` at Stage B (AppliedFields) — inject `IFieldTransformTool?` as optional constructor parameter (nullable, mirrors the pattern used for `IExportProgressStoreFactory?` in `WorkItemsModule`), call `IsEnabledForPhase()` to check, call `ApplyTransforms(fields, context)` on each revision's field collection during import (FR-006). Export-phase wiring follows same pattern in `WorkItemExportOrchestrator`. This is the integration point that makes the tool actually execute during migration.
- [ ] T021 [P] Write unit tests for `FieldTransformPipeline` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformPipelineTests.cs` — test: group ordering, transform ordering, `applyTo` filtering (group + transform level), `enabled` flags at all 3 levels, empty pipeline returns input unchanged, tag dedup post-pass, pipeline output feeds next transform
- [ ] T022 [P] Write unit tests for `FieldTransformFactory` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformFactoryTests.cs` — test: known type creates correct transform, unknown type throws, missing required property throws, default name generation, identity field rejection (FR-021)
- [ ] T023 [P] Write unit tests for `FieldTransformTool` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformToolTests.cs` — test: `IsEnabledForPhase()` routing, startup validation (FR-008), >100 transforms warning (FR-023), stateless across invocations (FR-016)
- [ ] T024 Verify build and tests: run `dotnet clean && dotnet build --no-incremental && dotnet test` — all must pass

**Checkpoint**: Pipeline infrastructure complete. Factory can create transforms (but only built-in types so far). Pipeline executes groups/transforms in order with filtering. DI registration works. All unit tests pass.

---

## Phase 3: User Story 1 — Remap Field Values Between Process Templates (Priority: P1) 🎯 MVP

**Goal**: Implement `MapValueTransform` — the most common field transformation. An operator can declare value-mapping rules (e.g., `Active → In Progress`) to remap picklist fields between process templates.

**Independent Test**: Configure a MapValueTransform for `System.State`, process a revision with `Active`, verify output is `In Progress`.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T025 [US1] Create `features/import/workitems/field-transform/value-remapping.feature` — translate spec.md US1 acceptance scenarios (4 scenarios: value mapped, unmapped value preserved with warning, applyTo filter skips non-matching type, sequential transforms in declaration order) into conformant Gherkin per `.agents/guardrails/acceptance-test-format.md`

### Implementation

- [ ] T026 [US1] Implement `MapValueTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/MapValueTransform.cs` — looks up `field` value in `valueMap` dictionary, replaces if found (FR-011: preserve original + warn if not found), supports `applyTo` work item type filter
- [ ] T027 [US1] Register `MapValue` type discriminator in `FieldTransformFactory` — add case for `"MapValue"` → `MapValueTransform`, validate `field` and `valueMap` are present
- [ ] T028 [P] [US1] Write unit tests for `MapValueTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/MapValueTransformTests.cs` — test: value found and replaced, value not found preserves original + logs warning, null value handling, empty valueMap, case sensitivity of lookup keys
- [ ] T029 [US1] Write Reqnroll step definitions for US1 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/ValueRemappingSteps.cs` and `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/ValueRemappingContext.cs` — implement Given/When/Then bindings for all 4 scenarios in the feature file
- [ ] T030 [US1] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test` — all US1 acceptance scenarios and unit tests pass

**Checkpoint**: MapValueTransform works. An operator can remap field values via config. MVP is functional.

---

## Phase 4: User Story 2 — Copy or Rename Fields (Priority: P1)

**Goal**: Implement `CopyFieldTransform` and `CopyFieldBatchTransform` — copy field values from one field name to another, supporting default values and batch operations.

**Independent Test**: Configure a CopyFieldTransform from `Custom.OldField` to `Custom.NewField`, process a revision, verify the target field appears.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T031 [US2] Create `features/import/workitems/field-transform/copy-field.feature` — translate spec.md US2 acceptance scenarios (4 scenarios: copy succeeds, default value used when source absent, empty value copied not defaulted, target overwritten unconditionally) into conformant Gherkin

### Implementation

- [ ] T032 [US2] Implement `CopyFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/CopyFieldTransform.cs` — copies `sourceField` to `targetField`, supports `defaultValue` when source is absent (FR-010), empty value is copied (not defaulted), overwrites existing target value
- [ ] T033 [P] [US2] Implement `CopyFieldBatchTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/CopyFieldBatchTransform.cs` — iterates `fieldMappings` dictionary, applies `CopyFieldTransform` logic for each pair
- [ ] T034 [US2] Register `CopyField` and `CopyFieldBatch` type discriminators in `FieldTransformFactory` — validate required properties (`sourceField`/`targetField` for CopyField, `fieldMappings` for CopyFieldBatch)
- [ ] T035 [P] [US2] Write unit tests for `CopyFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/CopyFieldTransformTests.cs` — test: basic copy, default value, empty vs absent, overwrite existing, missing source without default
- [ ] T036 [P] [US2] Write unit tests for `CopyFieldBatchTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/CopyFieldBatchTransformTests.cs` — test: multiple mappings applied, partial source fields, empty batch
- [ ] T037 [US2] Write Reqnroll step definitions for US2 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/CopyFieldSteps.cs` and `CopyFieldContext.cs`
- [ ] T038 [US2] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test` — all US2 acceptance and unit tests pass

**Checkpoint**: CopyFieldTransform and CopyFieldBatchTransform work. Both P1 stories complete.

---

## Phase 5: User Story 3 — Exclude or Clear Unwanted Fields (Priority: P2)

**Goal**: Implement `ExcludeFieldTransform` and `ClearFieldTransform` — remove fields entirely or set them to null.

**Independent Test**: Configure ExcludeFieldTransform for `Custom.InternalOnly`, verify the field is absent from output.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T039 [US3] Create `features/import/workitems/field-transform/exclude-clear.feature` — translate spec.md US3 acceptance scenarios (3 scenarios: exclude removes field, clear nulls value, exclude on absent field succeeds silently)

### Implementation

- [ ] T040 [P] [US3] Implement `ExcludeFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/ExcludeFieldTransform.cs` — removes field key from dictionary entirely (FR-012), silent no-op if field absent
- [ ] T041 [P] [US3] Implement `ClearFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/ClearFieldTransform.cs` — sets field value to null
- [ ] T042 [US3] Register `ExcludeField` and `ClearField` type discriminators in `FieldTransformFactory` — validate `field` property present
- [ ] T043 [P] [US3] Write unit tests for `ExcludeFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/ExcludeFieldTransformTests.cs`
- [ ] T044 [P] [US3] Write unit tests for `ClearFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/ClearFieldTransformTests.cs`
- [ ] T045 [US3] Write Reqnroll step definitions for US3 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/ExcludeClearSteps.cs` and `ExcludeClearContext.cs`
- [ ] T046 [US3] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`

**Checkpoint**: Exclude and Clear transforms work. Field cleanup is functional.

---

## Phase 6: User Story 4 — Set Literal and Computed Values (Priority: P2)

**Goal**: Implement `SetFieldTransform` and `CalculateFieldTransform` — stamp migrations with literal values or compute field values from expressions.

**Independent Test**: Configure SetFieldTransform for `Custom.MigratedBy = "migration-platform"`, verify value appears.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T047 [US4] Create `features/import/workitems/field-transform/set-calculate.feature` — translate spec.md US4 acceptance scenarios (3 scenarios: literal value set, computed value from expression, missing field reference in expression produces error)

### Implementation

- [ ] T048 [US4] Implement `SetFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/SetFieldTransform.cs` — sets `field` to literal `value`, supports `${migration.timestamp}` variable substitution
- [ ] T049 [US4] Implement `CalculateFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/CalculateFieldTransform.cs` — safe expression evaluator restricted to arithmetic (`+`, `-`, `*`, `/`, `%`), string concatenation, and field references (FR-019); division by zero produces error and skips transform; no method calls, no lambdas, no reflection (FR-013); define `IExpressionEvaluator` abstraction to isolate evaluator choice
- [ ] T050 [US4] Register `SetField` and `CalculateField` type discriminators in `FieldTransformFactory` — validate `field`+`value` for SetField, `field`+`expression` for CalculateField
- [ ] T051 [P] [US4] Write unit tests for `SetFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/SetFieldTransformTests.cs`
- [ ] T052 [P] [US4] Write unit tests for `CalculateFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/CalculateFieldTransformTests.cs` — test: arithmetic, string concat, field refs, division by zero, missing field ref, restricted language (no method calls)
- [ ] T053 [US4] Write Reqnroll step definitions for US4 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/SetCalculateSteps.cs` and `SetCalculateContext.cs`
- [ ] T054 [US4] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`

**Checkpoint**: Set and Calculate transforms work. Both P2 stories complete.

---

## Phase 7: User Story 5 — Transform Fields into Tags (Priority: P3)

**Goal**: Implement all four tag-producing transforms — `FieldToTagTransform`, `ConditionalTagTransform`, `MergeToTagTransform`, `TreeToTagTransform` — with `"; "` separator and case-insensitive deduplication.

**Independent Test**: Configure TreeToTagTransform on `System.AreaPath`, verify flattened path segments appear in `System.Tags`.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T055 [US5] Create `features/import/workitems/field-transform/tag-transforms.feature` — translate spec.md US5 acceptance scenarios (5 scenarios: FieldToTag copies field as tag, ConditionalTag adds tag on pattern match, ConditionalTag skips on no match, MergeToTag deduplicates case-insensitively, TreeToTag flattens path segments into tags)

### Implementation

- [ ] T056 [US5] Implement shared `TagUtilities` helper in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/TagUtilities.cs` — `AppendTag(existingTags, newTag)` and `DeduplicateTags(tags)` using `"; "` separator, case-insensitive dedup (FR-022)
- [ ] T057 [P] [US5] Implement `FieldToTagTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/FieldToTagTransform.cs` — copies field value as tag via `TagUtilities.AppendTag()`
- [ ] T058 [P] [US5] Implement `ConditionalTagTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/ConditionalTagTransform.cs` — adds `tag` to `System.Tags` when `field` value matches `condition` regex pattern (uses regex timeout from FR-018)
- [ ] T059 [P] [US5] Implement `MergeToTagTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/MergeToTagTransform.cs` — merges multiple `sourceFields` tag values into `System.Tags`, deduplicates
- [ ] T060 [P] [US5] Implement `TreeToTagTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/TreeToTagTransform.cs` — splits tree path field by `\`, adds each segment as tag
- [ ] T061 [US5] Register `FieldToTag`, `ConditionalTag`, `MergeToTag`, `TreeToTag` type discriminators in `FieldTransformFactory`
- [ ] T062 [P] [US5] Write unit tests for `TagUtilities` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/TagUtilitiesTests.cs` — test: append, dedup case-insensitive, separator handling, empty tags
- [ ] T063 [P] [US5] Write unit tests for all four tag transforms in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/` — one test file per transform
- [ ] T064 [US5] Write Reqnroll step definitions for US5 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/TagTransformSteps.cs` and `TagTransformContext.cs`
- [ ] T065 [US5] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`

**Checkpoint**: All four tag transforms work with deduplication. Tag-based fallback for tree structures is functional.

---

## Phase 8: User Story 6 — Regex Field Cleanup (Priority: P3)

**Goal**: Implement `RegexFieldTransform` — regex find-and-replace with 1-second timeout (ReDoS mitigation).

**Independent Test**: Configure regex to strip `[OLD]` prefix from `System.Title`, verify output.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T066 [US6] Create `features/import/workitems/field-transform/regex-cleanup.feature` — translate spec.md US6 acceptance scenarios (2 scenarios: pattern matches and replaces, pattern does not match leaves unchanged)

### Implementation

- [ ] T067 [US6] Implement `RegexFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/RegexFieldTransform.cs` — compiled `Regex` with `TimeSpan.FromSeconds(1)` timeout (FR-018), pattern validated at construction (FR-014), timeout aborts with fail-fast error identifying pattern and field
- [ ] T068 [US6] Register `RegexField` type discriminator in `FieldTransformFactory` — validate `field`, `pattern`, `replacement` present; pre-compile and validate pattern at factory construction time
- [ ] T069 [P] [US6] Write unit tests for `RegexFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/RegexFieldTransformTests.cs` — test: match replaces, no match unchanged, invalid pattern rejected, timeout on catastrophic backtracking
- [ ] T070 [US6] Write Reqnroll step definitions for US6 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/RegexCleanupSteps.cs` and `RegexCleanupContext.cs`
- [ ] T071 [US6] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`

**Checkpoint**: Regex transform works with ReDoS protection.

---

## Phase 9: User Story 7 — Merge Multiple Fields (Priority: P3)

**Goal**: Implement `MergeFieldsTransform` and `ConditionalFieldTransform` — merge N fields into one using format template, conditionally set fields.

**Independent Test**: Configure MergeFieldsTransform with `"{0} — {1}"`, verify merged output.

### Gherkin Feature File (mandatory — ATDD Phase 1)

- [ ] T072 [US7] Create `features/import/workitems/field-transform/merge-fields.feature` — translate spec.md US7 acceptance scenarios (3 scenarios: both fields present merge succeeds, absent field treated as empty string, ConditionalField sets trueValue on match and falseValue on no-match)

### Implementation

- [ ] T073 [US7] Implement `MergeFieldsTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/MergeFieldsTransform.cs` — resolves `sourceFields` values, applies `formatString` via `string.Format()`, absent fields treated as empty string
- [ ] T074 [P] [US7] Implement `ConditionalFieldTransform` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/ConditionalFieldTransform.cs` — evaluates `condition` regex against specified field, sets target field to `trueValue` or `falseValue`
- [ ] T075 [US7] Register `MergeFields` and `ConditionalField` type discriminators in `FieldTransformFactory`
- [ ] T076 [P] [US7] Write unit tests for `MergeFieldsTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/MergeFieldsTransformTests.cs`
- [ ] T077 [P] [US7] Write unit tests for `ConditionalFieldTransform` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Transforms/ConditionalFieldTransformTests.cs`
- [ ] T078 [US7] Write Reqnroll step definitions for US7 in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/MergeFieldsSteps.cs` and `MergeFieldsContext.cs`
- [ ] T079 [US7] Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`

**Checkpoint**: All 14 transform types implemented. All P3 stories complete.

---

## Phase 10: Prepare-Time Validation & Export Phase

**Purpose**: Implement the `FieldTransformValidator` for prepare-time validation (FR-020) and the export-phase integration point (FR-017).

- [ ] T080 Create `features/platform/field-transform/field-transform-validation.feature` — scenarios: valid config passes validation, invalid field reference detected, field type mismatch detected, picklist value not in target detected, sample dry-run executes against N items (per VS-M1)
- [ ] T081 Implement `FieldTransformValidator` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/FieldTransformValidator.cs` — resolves field references against source+target via `IFieldDefinitionProviderFactory` (FR-020a,b), checks type compatibility (FR-020b), checks picklist values (FR-020c), executes sample dry-run (FR-020d), produces `FieldTransformValidationReport` (FR-020e)
- [ ] T082 [P] Write unit tests for `FieldTransformValidator` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformValidatorTests.cs` — test: all validation checks, sample dry-run with mock field definitions, validation report generation
- [ ] T083 Create `features/export/workitems/field-transform/export-phase-transform.feature` — scenarios: export-phase transform modifies revision.json before write, import-phase transform ignored during export, `both` phase runs in both directions
- [ ] T084 Write Reqnroll step definitions for validation scenarios in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/Steps/ValidationSteps.cs` and `ValidationContext.cs`
- [ ] T085 Verify: `dotnet clean && dotnet build --no-incremental && dotnet test`
- [ ] T085b Add OpenTelemetry structured logging to `FieldTransformTool` and `FieldTransformPipeline` — emit `Activity` spans for pipeline execution with group/transform name tags (FR-009, mandatory per Constitution X.7 Observability)

**Checkpoint**: Prepare-time validation catches configuration errors before migration. Export-phase transforms supported.

---

## Phase 11: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect what was implemented. Resolves discrepancies.md items 1–4.

- [ ] T086 Update `docs/configuration.md` — add `Tools` top-level section to Full Schema JSON example and Top-Level Fields table; add `FieldTransform` tool configuration reference with schema and example (discrepancy #1)
- [ ] T087 [P] Update `docs/modules.md` — add "Tool Resolution" subsection under Module Architecture explaining: tools declared in `MigrationPlatform.Tools.*`, extensions load tools by name, effective settings = singleton tool + phase from extension reference (discrepancy #2)
- [ ] T088 [P] Update `docs/architecture.md` — add Tool concept to Components and Responsibilities section: "A Tool is a shared, cross-cutting service declared once at the MigrationPlatform config root. Extensions load tools by reference and declare phase. Tools are pure transformations or lookup services — they perform no I/O." (discrepancy #3)
- [ ] T089 [P] Update `analysis/proposed-features.md` — rename `FieldMappingTool` → `FieldTransformTool`, `IFieldMappingTool` → `IFieldTransformTool`, and all 14 map type names to `*Transform` naming throughout M1 and T1 sections (discrepancy #4)
- [ ] T090 Mark all items in `specs/022-workitem-field-mapping/discrepancies.md` as `Resolved`
- [ ] T091 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] T092 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T093 Run `dotnet test` — ALL tests MUST pass
- [ ] T094 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Phase 12: Polish & Cross-Cutting Concerns (OPTIONAL)

**Purpose**: Performance benchmarking and final hardening.

- [ ] T095 [P] Add performance benchmark test — verify 10,000 work items with 5 transforms complete with <5% overhead vs no transforms (SC-003)
- [ ] T096 [P] Add integration test with full Agile→Scrum config example from spec.md — verify end-to-end pipeline produces expected output for all transform types (SC-001, SC-002)
- [ ] T097 Review all `Assert.Inconclusive()` — replace with real assertions or delete tests
- [ ] T098 Final build and test verification: `dotnet clean && dotnet build --no-incremental && dotnet test`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3–9 (User Stories)**: All depend on Phase 2 completion
  - US1 (P1) and US2 (P1): Can run in parallel after Phase 2
  - US3 (P2) and US4 (P2): Can run in parallel after Phase 2
  - US5 (P3), US6 (P3), US7 (P3): Can run in parallel after Phase 2
- **Phase 10 (Validation)**: Depends on all 14 transforms being implemented (Phase 3–9 complete)
- **Phase 11 (Docs)**: Depends on all implementation complete
- **Phase 12 (Polish)**: Depends on Phase 11

### User Story Dependencies

- **US1 (P1)**: No dependencies on other stories — implements MapValueTransform
- **US2 (P1)**: No dependencies on other stories — implements CopyField + CopyFieldBatch
- **US3 (P2)**: No dependencies — implements ExcludeField + ClearField
- **US4 (P2)**: No dependencies — implements SetField + CalculateField
- **US5 (P3)**: No dependencies — implements 4 tag transforms + TagUtilities
- **US6 (P3)**: No dependencies — implements RegexField
- **US7 (P3)**: No dependencies — implements MergeFields + ConditionalField

### Within Each User Story

1. Gherkin `.feature` file MUST exist before step definitions or production code
2. Transform implementation before factory registration
3. Unit tests in parallel with implementation
4. Reqnroll step definitions after implementation
5. Build + test verification at end

### Parallel Opportunities

- Phase 1: T002–T006 and T008–T015 all parallelizable (different files)
- Phase 2: T021–T023 parallelizable (different test files)
- User Stories: All 7 stories can run in parallel after Phase 2 (different transform files)
- Within each story: Unit test writing parallelizable with implementation

---

## Parallel Example: Phase 3 (User Story 1)

```text
# After T025 (feature file), these can run in parallel:
T026: Implement MapValueTransform
T028: Write MapValueTransform unit tests (can stub against interface)

# Then sequentially:
T027: Register in factory (depends on T026)
T029: Reqnroll step definitions (depends on T026, T027)
T030: Verify build + tests
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup (types + interfaces)
2. Complete Phase 2: Foundational (pipeline + factory + DI)
3. Complete Phase 3: US1 — MapValueTransform (most common need)
4. **STOP and VALIDATE**: Agile→Scrum state remapping works end-to-end
5. Complete Phase 4: US2 — CopyFieldTransform (second most common)
6. **MVP COMPLETE**: Value remapping + field copying covers ~80% of migration needs

### Incremental Delivery

1. Setup + Foundational → Pipeline infrastructure ready
2. US1 (MapValue) → Test → MVP for value remapping ✅
3. US2 (CopyField) → Test → Field renaming works ✅
4. US3 (Exclude/Clear) → Test → Field cleanup works ✅
5. US4 (Set/Calculate) → Test → Audit trails + computed fields ✅
6. US5–7 (Tags, Regex, Merge) → Test → Advanced transforms ✅
7. Validation + Docs + Polish → Production-ready ✅

Each increment adds value without breaking previous stories.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- ATDD: Feature file → failing tests → implementation → passing tests → review
- One scenario per ATDD session per commit (Constitution VIII)
- All transforms are pure functions — testable without mocks for I/O
- 14 transform types across 7 user stories, plus validation and docs

# Implementation Plan: Work Item Field Transformation

**Branch**: `022-workitem-field-mapping` | **Date**: 2026-04-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/022-workitem-field-mapping/spec.md`

## Summary

Implement `FieldTransformTool` — a pure-function transformation pipeline that modifies work item revision field values during export or import. The tool is declared as a singleton under `MigrationPlatform.Tools.FieldTransform` and provides 14 transform types (CopyField, MapValue, RegexField, etc.) organised into ordered transform groups. Each group and transform supports `applyTo` type filters and `enabled` flags. The tool hooks into `WorkItemsModule` via the extension tool reference model, executing transforms on each `revision.json` field collection.

Key technical decisions:
- **Pure transformation**: receives field dictionary, returns modified copy — no I/O, no side effects.
- **Ordered pipeline**: groups in array order, transforms within groups in array order. Output of one transform feeds the next.
- **Prepare-time validation**: `validate` method checks field names/types against source+target definitions, executes sample dry-run.
- **Fail-fast v1**: first transform error halts the revision (future: P4 Operator Interaction).

## Technical Context

**Language/Version**: C# 10+, targeting .NET 10  
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options, Reqnroll.MSTest + Moq  
**Storage**: `IArtefactStore` (reads/writes `revision.json`), `IStateStore` (cursor checkpoints)  
**Testing**: Reqnroll.MSTest (BDD acceptance), MSTest (unit), Moq (`MockBehavior.Strict`)  
**Target Platform**: Cross-platform (.NET 10) — runs in Migration Agent (container or local)  
**Project Type**: Library (tool within `DevOpsMigrationPlatform.Infrastructure`)  
**Performance Goals**: 10,000 work items with 5 active transforms — no measurable throughput degradation vs no transforms (SC-003)  
**Constraints**: Streaming — one revision at a time, no in-memory buffering of revision sets. Regex timeout 1s per pattern. CalculateField restricted to arithmetic/string/field-refs only.  
**Scale/Scope**: 14 transform types, ~24 functional requirements, 7 user stories with ~20 acceptance scenarios

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All files in `/.agents/20-guardrails/`, `/.agents/30-context/`, and relevant `/docs/` have been read during this planning session. Constitution v1.3.4 reviewed.

- [x] **Package-First (I):** FieldTransformTool is a pure transformation — it reads field values from the in-memory field collection (loaded from `revision.json` via `IArtefactStore`) and returns a modified copy. No direct source-to-target API calls. Export-phase transforms modify fields before `revision.json` is written to the package; import-phase transforms modify fields after reading from the package before sending to target.
- [x] **Streaming (II):** Transforms operate on a single revision's field collection at a time. FR-016 mandates statelessness across revisions — no accumulation. No in-memory buffering of multiple revisions.
- [x] **WorkItems Layout (III):** The tool does not modify folder structure. It operates on field values within `revision.json` content only.
- [x] **Checkpointing (IV):** Transform execution is part of the existing revision processing pipeline (Stage B: AppliedFields in `RevisionFolderProcessor`). Checkpointing is handled by the existing cursor mechanism. The transform tool itself is stateless and does not maintain its own cursor.
- [x] **Module Isolation (V):** All interfaces (`IFieldTransformTool`, `IFieldTransform`, `IFieldTransformValidator`) defined in `DevOpsMigrationPlatform.Abstractions.Agent` per the 021.2 project boundary rules (Agent-only contracts). Config options (`FieldTransformOptions` etc.) in `DevOpsMigrationPlatform.Abstractions` (cross-cutting config schema). Implementations in `Infrastructure.Agent`. No direct filesystem access — the tool receives a field dictionary and returns a modified copy. Identity fields are explicitly rejected (FR-021); identity mapping via `IIdentityMappingService` is unaffected.
- [x] **Separation of Planes (VI):** The transform tool lives in the Infrastructure layer, invoked by the Migration Agent during revision processing. No CLI, TUI, or control plane involvement in transform logic. Configuration validation runs during `prepare` command (which executes in the Agent).
- [x] **Determinism (VII):** Same configuration + same field values = same output. All transforms are pure functions. FR-024 requires `ConfigVersion` bump for breaking changes.
- [x] **ATDD-First (VIII):** Spec contains 7 user stories with 20+ acceptance scenarios (Given/When/Then). Each will be implemented via the ATDD inner loop. Feature files will live under `features/import/workitems/field-transform/` and `features/export/workitems/field-transform/`.
- [x] **SOLID & DI (IX):** Transform types registered via polymorphic DI (`IFieldTransform` with type discriminator resolution). Configuration bound via `IOptions<FieldTransformOptions>` with sealed options class and `SectionName` constant. Registration via `AddFieldTransformServices(this IServiceCollection)` extension method. All interfaces in Abstractions.

## Project Structure

### Documentation (this feature)

```text
specs/022-workitem-field-mapping/
├── plan.md              # This file
├── spec.md              # Feature specification (completed)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output — interface definitions
├── discrepancies.md     # Architecture gaps (4 logged)
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

Per the **021.2 Separation of Concerns** boundary rules:
- Agent-only interfaces → `Abstractions.Agent`
- Config schema (options) → `Abstractions` (cross-cutting; CLI + Agent both bind config)
- Agent-only implementations → `Infrastructure.Agent`
- Agent-only tests → `Infrastructure.Agent.Tests`

```text
src/DevOpsMigrationPlatform.Abstractions.Agent/
├── Tools/
│   ├── IFieldTransformTool.cs          # Top-level tool interface (Agent-only)
│   ├── IFieldTransform.cs              # Individual transform contract
│   ├── IFieldTransformValidator.cs     # Prepare-time validation contract
│   ├── IFieldTransformFactory.cs       # Creates IFieldTransform from options
│   ├── IFieldDefinitionProvider.cs     # Field metadata lookup (source + target)
│   ├── IExpressionEvaluator.cs         # Safe arithmetic/string expression evaluator
│   ├── FieldTransformPhase.cs          # Enum: Export | Import | Both
│   ├── FieldTransformContext.cs        # WorkItemId, RevisionIndex, WorkItemType, Phase
│   ├── FieldTransformAction.cs         # Telemetry: group, transform, field, old/new values
│   ├── FieldTransformResult.cs         # Modified fields dictionary + actions list
│   ├── FieldTransformValidationReport.cs  # Validation report + severity enum
│   └── FieldDefinition.cs              # Field metadata: ReferenceName, Type, AllowedValues

src/DevOpsMigrationPlatform.Abstractions/
├── Options/
│   ├── FieldTransformOptions.cs        # Sealed options: SectionName, transformGroups
│   ├── FieldTransformGroupOptions.cs   # Group: name, enabled, applyTo, transforms
│   ├── FieldTransformRuleOptions.cs    # Individual rule: type + all type-specific props
│   └── FieldTransformExtensionOptions.cs  # Extension reference: Enabled, Phase

src/DevOpsMigrationPlatform.Infrastructure.Agent/
├── Tools/
│   ├── FieldTransform/
│   │   ├── FieldTransformTool.cs             # IFieldTransformTool implementation
│   │   ├── FieldTransformPipeline.cs         # Ordered group→transform execution
│   │   ├── FieldTransformFactory.cs          # Switches on Type discriminator
│   │   ├── FieldTransformValidator.cs        # Prepare-time validation logic
│   │   ├── Transforms/
│   │   │   ├── CopyFieldTransform.cs
│   │   │   ├── CopyFieldBatchTransform.cs
│   │   │   ├── SetFieldTransform.cs
│   │   │   ├── MapValueTransform.cs
│   │   │   ├── MergeFieldsTransform.cs
│   │   │   ├── CalculateFieldTransform.cs
│   │   │   ├── ClearFieldTransform.cs
│   │   │   ├── ExcludeFieldTransform.cs
│   │   │   ├── ConditionalTagTransform.cs
│   │   │   ├── FieldToTagTransform.cs
│   │   │   ├── MergeToTagTransform.cs
│   │   │   ├── ConditionalFieldTransform.cs
│   │   │   ├── RegexFieldTransform.cs
│   │   │   ├── TagUtilities.cs
│   │   │   └── TreeToTagTransform.cs
│   │   └── FieldTransformToolServiceCollectionExtensions.cs  # AddFieldTransformToolServices()

tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
├── Tools/
│   └── FieldTransform/
│       ├── FieldTransformPipelineTests.cs
│       ├── FieldTransformFactoryTests.cs
│       ├── FieldTransformToolTests.cs
│       ├── FieldTransformValidatorTests.cs
│       ├── Transforms/
│       │   ├── CopyFieldTransformTests.cs
│       │   ├── MapValueTransformTests.cs
│       │   ├── RegexFieldTransformTests.cs
│       │   ├── CalculateFieldTransformTests.cs
│       │   └── ... (one per transform type)
│       └── Steps/                            # Reqnroll step definitions
│           ├── FieldTransformSteps.cs
│           └── FieldTransformContext.cs

features/
├── import/workitems/field-transform/
│   ├── value-remapping.feature
│   ├── copy-field.feature
│   ├── exclude-clear.feature
│   ├── set-calculate.feature
│   ├── tag-transforms.feature
│   ├── regex-cleanup.feature
│   └── merge-fields.feature
├── export/workitems/field-transform/
│   └── export-phase-transform.feature
└── platform/field-transform/
    └── field-transform-validation.feature    # Prepare-time validation (VS-M1)
```

**Structure Decision**: Per 021.2, Agent-only tool interfaces live in `Abstractions.Agent/Tools/`. Config options (the binding schema the CLI, CP, and Agent all parse) remain in `Abstractions/Options/`. Implementations live in `Infrastructure.Agent/Tools/FieldTransform/`, and tests in `Infrastructure.Agent.Tests/`. The `Transforms/` subfolder holds all 14 individual transform implementations. No new projects are needed — all code fits within existing projects.

## Architecture Review Findings

*Mandatory `after_plan` hook. Date: 2026-04-24.*

**Verdict**: No Critical violations. 1 High, 5 Medium, 2 Low, 2 Informational. **Cleared for `/speckit.tasks`.**

### Must Address During Implementation

| ID | Severity | Finding | Resolution |
|---|---|---|---|
| CA-H1 | High | `FieldTransformRuleOptions` flat property bag violates type safety (X.2) | Keep flat class for JSON binding; factory projects into strongly-typed per-transform records at construction time |
| HX-M1 | Medium | `Phase` as raw string — primitive obsession | Define `FieldTransformPhase` enum in `Abstractions.Agent/Tools/` |
| CA-M1 | Medium | Validator receives providers as method params, not constructor DI | Inject `IFieldDefinitionProviderFactory` via constructor; resolve internally |
| MM-M1 | Medium | `IFieldDefinitionProvider` may be single-use (rule 21) | Document expected second consumer (ValidationModule) in research.md |
| MM-M2 | Medium | Registration method name inconsistent | Use `AddFieldTransformToolServices()` in `Infrastructure.Agent` |
| VS-M1 | Medium | No feature file for prepare-time validation flow | Add `features/platform/field-transform/field-transform-validation.feature` |
| 021.2 | Architecture | Interfaces were in flat `Abstractions` project | Per 021.2: Agent-only interfaces → `Abstractions.Agent/Tools/`; Options remain in `Abstractions/Options/`; Implementations in `Infrastructure.Agent`; Tests in `Infrastructure.Agent.Tests` |

### Positive Findings

- [SA-I1] All 14 transforms use exemplary Screaming Architecture names
- [SA-I2] Configuration example uses business-meaningful group names

## Complexity Tracking

No constitution violations. All requirements fit within existing architectural patterns.


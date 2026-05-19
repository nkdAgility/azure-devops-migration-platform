# Field Transform Contract

Canonical contract for the Field Transform capability seam.

## Scope

This document is the authoritative contract for:

- runtime surface: `IFieldTransformTool`
- per-transform contract: `IFieldTransform`
- factory contract: `IFieldTransformFactory`
- validation contract: `IFieldTransformValidator`
- transform-failure behavior contract: `IFieldTransformFailureBehaviour`
- Field Transform configuration shape

## Canonical Runtime Surface

### `IFieldTransformTool`

- `ApplyTransforms(IReadOnlyDictionary<string, object?> fields, FieldTransformContext context)`
  - single entry-point for applying enabled transforms for the active phase
  - returns `FieldTransformResult` containing mutated fields and audit actions
- `IsEnabledForPhase(FieldTransformPhase phase)`
  - true when at least one enabled rule is active for that phase

### `IFieldTransform`

- `Type` is the discriminator used by `IFieldTransformFactory`.
- `Apply(...)` must not mutate input dictionary in-place.
- returns `FieldTransformResult` containing new field dictionary and action log.

## Transform-Failure Behavior Contract

### `IFieldTransformFailureBehaviour`

This behavior contract applies to Field Transform tools and transform implementations.
Failure policy is configured through `FieldTransformFailureBehaviourOptions`.

Implementations must return a `FieldTransformResult` for one of these behaviors:

1. **Skip** (default): leave target field unchanged.
2. **Error**: escalate as a hard failure.
3. **ResetValue**: assign configured fallback value.

Current behavior implementations:

- `SkipOnFailure`
- `ErrorOnFailure`
- `ResetToValueOnFailure`

Current factory wiring in `FieldTransformFactory`:

- `FailureRule.Type = "Error"` -> `ErrorOnFailure`
- `FailureRule.Type = "ResetValue"` -> `ResetToValueOnFailure` (requires `FailureRule.Value`)
- anything else / missing -> `SkipOnFailure`

## Configuration Contract (authoritative in `.agents`)

### Tool root

- section: `MigrationPlatform:Tools:FieldTransform`
- bound type: `FieldTransformOptions`

```json
{
  "MigrationPlatform": {
    "Tools": {
      "FieldTransform": {
        "Enabled": true,
        "TransformGroups": []
      }
    }
  }
}
```

### Group shape

`TransformGroups[]` (`FieldTransformGroupOptions`):

- `Name` (optional)
- `Enabled` (default `true`)
- `ApplyTo` (optional list of work item types)
- `Transforms` (ordered list of `FieldTransformRuleOptions`)

### Rule shape

`Transforms[]` (`FieldTransformRuleOptions`):

- common: `Name`, `Type`, `Enabled`, `ApplyTo`
- field selectors: `Field`, `SourceField`, `TargetField`, `SourceFields`
- values/mapping: `Value`, `DefaultValue`, `ValueMap`, `FieldMappings`
- expression/regex/condition: `Expression`, `Pattern`, `Replacement`, `Condition`
- tags/booleans: `Tag`, `TrueValue`, `FalseValue`
- transform failure behavior: `FailureRule` (contract shape `FieldTransformFailureBehaviourOptions`)

`FailureRule` (`FieldTransformFailureBehaviourOptions`, currently represented in code as `FieldTransformFailureRuleOptions`):

- `Type`: `Skip` (default), `Error`, `ResetValue`
- `Value`: required when `Type = ResetValue`

### WorkItems extension toggle

WorkItems extension configuration is bound from:

- `MigrationPlatform:Modules:WorkItems:Extensions:FieldTransform`
- type: `FieldTransformExtensionOptions`

Fields:

- `Enabled` (default `true`)
- `Phase` (`Export` | `Import` | `Both`, default `Import`)

## Runtime Constraints

- tool runs through a pipeline (`FieldTransformPipeline`) in configured order
- group/rule `Enabled` and `ApplyTo` filters gate execution
- identity fields are protected in factory (`System.CreatedBy`, `System.ChangedBy`, `System.AuthorizedAs`)
- unknown transform type is a hard configuration error
- options are validated at startup by `FieldTransformOptionsValidator`
  - enabled rules require non-empty `Type`
  - regex `Pattern` must compile
- tag deduplication post-pass runs when `System.Tags` is modified

## Observability

`FieldTransformTool` and pipeline emit:

- `ActivitySource` spans (`fieldtransform.apply`, `fieldtransform.pipeline.execute`, `fieldtransform.group.execute`)
- structured logs at start/complete/failure
- platform metrics for in-flight, applied/error, duration, and modified-field count

## Governance

- changes to this contract are at least Class B
- changes that add/replace/widen/narrow/bypass surface semantics are Class C
- Class C requires consent policy evidence from `.agents/10-contracts/consent-policy.yaml`

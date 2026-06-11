# DSL Design: field-transform-field-transform-pipeline

## Mapping

| Scenario | Test Class | Test Method |
|---|---|---|
| Tool-level enabled false prevents all transforms from running | FieldTransformToolTests | IsEnabledForPhase_WhenDisabled_ReturnsFalse |
| Group-level enabled false skips the entire group | FieldTransformPipelineTests | Execute_WithDisabledGroup_SkipsGroup |
| Transform-level enabled false skips only that transform | FieldTransformPipelineTests | Execute_WithDisabledTransform_SkipsTransform |
| Configuring a transform targeting an identity field is rejected | FieldTransformFactoryTests | Create_WithIdentityFieldAsField_ThrowsInvalidOperationException |

## Files
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformToolTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformPipelineTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/FieldTransform/FieldTransformFactoryTests.cs`

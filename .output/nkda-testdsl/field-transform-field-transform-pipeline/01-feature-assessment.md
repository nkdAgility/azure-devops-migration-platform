# Feature Assessment: field-transform-field-transform-pipeline

## Feature File
`features/platform/field-transform/field-transform-pipeline.feature` (deleted in commit 67a24250)

## Scenarios
1. Tool-level enabled false prevents all transforms from running
2. Group-level enabled false skips the entire group
3. Transform-level enabled false skips only that transform
4. Configuring a transform targeting an identity field is rejected

## Wiring State
Unwired — the feature file referenced step bindings in
`tests/.../Tools/FieldTransform/Steps/PipelineSteps.cs` and `PipelineContext.cs`,
but the feature file was deleted before this migration ran.

## Coverage Assessment
All four scenario intents are fully covered by existing MSTest [TestMethod] entries
in `FieldTransformToolTests`, `FieldTransformPipelineTests`, and `FieldTransformFactoryTests`.

# DSL Design: task-attribution

## Target Test Class
`TaskAttributionDslTests` in `DevOpsMigrationPlatform.ControlPlane.Tests/Progress/`

## Shared Infrastructure
- `ProgressControllerContext` (extended with public `TaskStore` property)

## Test Pattern
- Build context with execution plan pre-stored via `TaskStore.Store`
- Post ProgressEvent via `Controller.PostProgress`
- Assert task status via `TaskStore.GetLatest`

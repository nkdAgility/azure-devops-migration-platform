# Feature Assessment: Post-Flight Correctness Metrics

## Feature file
`features/platform/validation/post-flight-correctness-metrics.feature`

## Family
`validation-post-flight-correctness-metrics`

## Wiring state
**Unwired** — no step bindings found in tests/ for this feature file.

## Scenarios (4)

1. **Matching revision counts produce zero missing and zero delta** — verify that 20 work items with equal source/target revision counts produce 0 RevisionsMissing events and 0 mean RevisionDelta.
2. **Fewer target revisions increment the missing counter** — verify that 2 of 20 items with fewer target revisions produce exactly 2 RevisionsMissing events and 2 negative delta recordings.
3. **Broken links are detected and counted** — verify that 3 of 20 items with broken links produce exactly 3 BrokenLinks events.
4. **Sample rate zero skips all correctness checks** — verify that when sample rate = 0, no correctness metrics are emitted.

## Source types
- `PlatformMetrics` in `DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry`
- `IPlatformMetrics` in `DevOpsMigrationPlatform.Abstractions.Agent.Telemetry`
- `WellKnownAgentMetricNames` in `DevOpsMigrationPlatform.Abstractions`

## Migration risks
- None significant. All instruments already exist and are individually tested.
- The post-flight orchestrator is not yet implemented as a standalone class; tests are written against PlatformMetrics directly.

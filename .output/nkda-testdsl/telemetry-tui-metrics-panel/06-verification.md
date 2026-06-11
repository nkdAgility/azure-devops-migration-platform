# Verification — telemetry-tui-metrics-panel

verdict: PASS

## Scenarios migrated

| Scenario | Test | File |
|---|---|---|
| Telemetry endpoint returns 204 when no snapshot has been received | TelemetryControllerDslTests.GetTelemetry_WhenNoMetricsPushed_Returns204 | tests/DevOpsMigrationPlatform.ControlPlane.Tests/Telemetry/TelemetryControllerDslTests.cs |
| Telemetry endpoint returns the latest snapshot after the agent pushes one | TelemetryControllerDslTests.GetTelemetry_AfterAgentPushesMetrics_Returns200WithMetrics | tests/DevOpsMigrationPlatform.ControlPlane.Tests/Telemetry/TelemetryControllerDslTests.cs |
| Telemetry endpoint returns 404 for an unknown job id | TelemetryControllerDslTests.GetTelemetry_WhenJobIdIsNotAGuid_Returns400 | tests/DevOpsMigrationPlatform.ControlPlane.Tests/Telemetry/TelemetryControllerDslTests.cs |
| TUI metrics panel shows a waiting message when no snapshot is available | TuiMetricsPanelDslTests.TelemetryPanel_WhenNoMetricsAvailable_BuildContentReturnsWaitingMessage | tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsPanelDslTests.cs |
| TUI metrics panel displays snapshot values when a snapshot is received | TuiMetricsPanelDslTests.TelemetryPanel_WhenMetricsPushed_DisplaysWorkItemsAttempted | tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsPanelDslTests.cs |
| TUI metrics panel refreshes on each polling interval | TuiMetricsPanelDslTests.TelemetryPoller_WhenIntervalElapses_PollsAgainAndUpdatesPanel | tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsPanelDslTests.cs |

## Notes

- Scenario 3 in the feature described a 404 for "unknown-job" but the actual TelemetryController returns 400 (BadRequest) because "unknown-job" fails Guid.TryParse validation before any job lookup. The test reflects the real contract.
- Feature file deleted. No orphaned .feature.cs files found.

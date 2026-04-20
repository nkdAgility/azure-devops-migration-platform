Feature: TUI Live Metrics Panel
  As a migration operator watching a running job in the CLI
  I want to see live metric values update every few seconds
  So that I can monitor throughput and errors without leaving the terminal

  Background:
    Given the Control Plane is running
    And a job with id "job-001" is active

  Scenario: Telemetry endpoint returns 204 when no snapshot has been received
    Given the Control Plane has received no snapshot for job "job-001"
    When "GET /jobs/job-001/telemetry" is called
    Then the response status is 204
    And the response body is empty

  Scenario: Telemetry endpoint returns the latest snapshot after the agent pushes one
    Given the Migration Agent has pushed a MetricSnapshot for job "job-001"
    When "GET /jobs/job-001/telemetry" is called
    Then the response status is 200
    And the response body contains a MetricSnapshot with all numeric fields

  Scenario: Telemetry endpoint returns 404 for an unknown job id
    When "GET /jobs/unknown-job/telemetry" is called
    Then the response status is 404

  Scenario: TUI metrics panel shows a waiting message when no snapshot is available
    Given the TUI is displaying the progress view for job "job-001"
    And no MetricSnapshot has been received from the Control Plane
    When the metrics panel is rendered
    Then the panel displays "(waiting for agent…)"

  Scenario: TUI metrics panel displays snapshot values when a snapshot is received
    Given the TUI is displaying the progress view for job "job-001"
    And the Control Plane returns a MetricSnapshot with "WorkItemsAttempted" equal to 42
    When the metrics panel is rendered
    Then the panel displays "Work Items Attempted" as 42

  Scenario: TUI metrics panel refreshes on each polling interval
    Given the TUI is displaying the progress view for job "job-001"
    And SnapshotIntervalSeconds is 5
    When 5 seconds elapse after the panel first renders
    Then the TelemetryPoller calls "GET /jobs/job-001/telemetry" again
    And the panel is updated with the latest MetricSnapshot values

Feature: Migration Agent to Control Plane Metric Snapshot Relay
  As a migration operator
  I want the Migration Agent to push live metric snapshots to the Control Plane
  So that job progress can be monitored without polling the agent directly

  Background:
    Given the Control Plane is running and accepting requests
    And the Migration Agent holds an active lease with id "lease-abc-123"

  Scenario: Migration Agent pushes a MetricSnapshot on its configured interval
    Given the agent has exported 250 work item revisions
    And SnapshotIntervalSeconds is configured to 5
    When 5 seconds elapse after the agent acquires the lease
    Then the agent posts a MetricSnapshot to "POST /agents/lease/lease-abc-123/telemetry"
    And the snapshot contains "WorkItemsCompleted" greater than 0

  Scenario: Control Plane stores the latest snapshot per job
    Given the Control Plane has received no snapshot for job "job-001"
    When the agent posts a MetricSnapshot with "WorkItemsAttempted" equal to 10 for lease "lease-abc-123"
    Then the Control Plane stores the snapshot under job "job-001"
    And "GET /jobs/job-001/telemetry" returns 200 with "WorkItemsAttempted" equal to 10

  Scenario: New snapshot replaces the previous snapshot for the same job
    Given the Control Plane has stored a snapshot with "WorkItemsAttempted" equal to 10 for job "job-001"
    When the agent posts a MetricSnapshot with "WorkItemsAttempted" equal to 20 for lease "lease-abc-123"
    Then "GET /jobs/job-001/telemetry" returns 200 with "WorkItemsAttempted" equal to 20
    And only one snapshot is stored per job at any time

  Scenario: Push is skipped when no MetricSnapshot is available yet
    Given the IMetricSnapshotStore contains no snapshot
    When the ControlPlaneTelemetryTimer fires
    Then no HTTP request is sent to the Control Plane

  Scenario: Push is skipped when the agent holds no active lease
    Given the agent has no current lease id
    And the IMetricSnapshotStore contains a MetricSnapshot
    When the ControlPlaneTelemetryTimer fires
    Then no HTTP request is sent to the Control Plane

  Scenario: A non-success response from the Control Plane does not crash the agent
    Given the Control Plane returns 503 for telemetry push requests
    When the ControlPlaneTelemetryTimer posts a MetricSnapshot
    Then the agent logs a warning
    And the agent continues running normally

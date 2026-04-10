Feature: TUI Job Detail - Metrics Panel and Log Panel
  As an operator watching a running job
  I want to see real-time metrics and a live log stream beside the job list
  So that I can monitor job health on a single screen

  Scenario: Selecting a job populates Metrics and Log panels
    Given the TUI is open with the job list visible
    When the operator selects a job row
    Then the Metrics Panel shows per-module work item counts, revision counts, and throughput rates
    And the Log Panel shows a scrolling log of ProgressEvent messages in Progress mode

  Scenario: Log Panel updates in real time while job is running
    Given the operator has selected a running job
    When the Migration Agent pushes new ProgressEvent records via the lease protocol
    Then the Log Panel updates in real time without operator interaction

  Scenario: Metrics Panel refreshes on polling interval
    Given the operator has selected a running job
    When the 5 second polling interval elapses
    Then the Metrics Panel refreshes with the latest counts from GET /jobs/{jobId}/telemetry

  Scenario: Log Panel reconnects automatically after SSE drop
    Given the operator has selected a running job and the Log Panel is streaming
    When the TUI loses its SSE connection to the log stream
    Then the TUI reconnects automatically with exponential back-off up to a maximum of 30 seconds
    And resumes the live stream without operator intervention

  Scenario: Deselecting a job cancels SSE subscriptions
    Given the operator has selected a running job
    When the operator presses Escape to deselect the job
    Then all active SSE subscriptions for that job are cancelled immediately
    And no background threads remain subscribed

  Scenario: Viewing a completed job shows terminal state marker
    Given a job has reached a terminal state such as Completed or Failed
    When the operator selects it from the job list
    Then the Log Panel shows a final status separator line
    And the status bar shows the terminal state
    And no reconnection attempts are made

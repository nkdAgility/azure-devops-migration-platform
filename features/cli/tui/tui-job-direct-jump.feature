Feature: TUI Direct Job Jump via --job Flag
  As an operator who already knows their job ID
  I want to run "devopsmigration tui --job <jobId>" to open the TUI with that job pre-selected
  So that I can quickly rejoin monitoring a known job

  Scenario: --job pre-selects the job row on launch
    Given the operator runs "devopsmigration tui --job <jobId>"
    When the TUI launches
    Then the job list row for that job is pre-selected
    And the Metrics Panel and Log Panel are immediately populated for that job

  Scenario: --job with unknown job ID exits with error
    Given the operator runs "devopsmigration tui --job <jobId>" where the job does not exist
    When the lookup fails
    Then the TUI exits with a clear error identifying the unknown job ID

  Scenario: Escape from a --job pre-selected view deselects rather than exiting
    Given the operator launched via "devopsmigration tui --job <jobId>"
    When the operator presses Escape
    Then the job is deselected and the Metrics and Log panels are cleared
    And the TUI remains open showing the job list

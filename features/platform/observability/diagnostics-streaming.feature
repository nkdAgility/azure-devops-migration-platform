Feature: Diagnostics streaming panel
  As a migration operator
  I want to see live diagnostic log records in the TUI
  So that I can react to warnings and errors as they happen during a migration

  Scenario: TUI diagnostics panel displays agent log records in near real-time
    Given a running job and a TUI connected to the control plane
    When the Migration Agent emits a warning-level log record
    Then the TUI diagnostics panel displays the record within the configured streaming interval

  Scenario: TUI diagnostics panel supports level filter toggle
    Given a TUI diagnostics panel showing Warning-level records
    When the operator changes the level filter to Information
    Then subsequent records at Information level and above appear in the panel

  Scenario: TUI diagnostics panel replays recent records on reconnect
    Given a TUI that disconnected and reconnected
    When the reconnection occurs
    Then the diagnostics panel replays recent records from the control plane ring buffer

Feature: Package diagnostics sink persistence
  As a migration operator
  I want diagnostic log records persisted to the migration package
  So that I can troubleshoot failures from the package alone without access to original terminal output

  Background:
    Given a Migration Agent is executing an export job

  Scenario: Warning and error log records are written to the package
    When a warning or error log record is emitted by the agent
    Then a structured NDJSON log record is appended to ".migration/Logs/agent.jsonl" in the package

  Scenario: Diagnostic log records contain required fields
    Given the agent has written diagnostic records to the package
    When an operator opens ".migration/Logs/agent.jsonl"
    Then each line is a valid JSON object containing timestamp, level, category, and message
    And lines with exceptions also contain an exception field

  Scenario: Log records below configured minimum level are discarded
    Given the agent diagnostic log level is set to "Information"
    When a Trace or Debug log record is emitted
    Then the record is not written to ".migration/Logs/agent.jsonl"

  Scenario: Log records at or above configured minimum level are written
    Given the agent diagnostic log level is set to "Information"
    When an Information, Warning, or Error log record is emitted
    Then the record is written to ".migration/Logs/agent.jsonl"

  Scenario: Log sink failures do not halt the export
    Given the package store is temporarily unavailable
    When the diagnostic log sink attempts to flush
    Then the export continues without interruption
    And the dropped record count is incremented

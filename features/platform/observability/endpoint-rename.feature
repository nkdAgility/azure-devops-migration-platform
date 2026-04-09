Feature: Endpoint and CLI command rename
  As a migration operator
  I want progress event endpoints and CLI commands clearly named
  So that I do not confuse progress events with diagnostic logs

  Scenario: Progress event endpoint returns progress data at the new path
    Given a job with progress events has been submitted
    When a client calls the progress snapshot endpoint for the job
    Then it receives the same progress event data previously available at the logs endpoint

  Scenario: Progress SSE stream works at the new path
    Given a running job with a connected SSE client
    When the client subscribes to the progress follow endpoint
    Then it receives live progress events via SSE

  Scenario: Manage progress displays a snapshot of progress events
    Given a completed job with progress events
    When an operator runs "manage progress --job <id>"
    Then a snapshot of progress event records is displayed
    And no --follow option is available

  Scenario: Manage diagnostics downloads diagnostic logs from the package
    Given a completed job with diagnostic logs in the package
    When an operator runs "manage diagnostics --job <id>"
    Then diagnostic log records from "Logs/agent.jsonl" are downloaded and displayed

  Scenario: Manage diagnostics filters by level
    Given a completed job with diagnostic logs at multiple levels
    When an operator runs "manage diagnostics --job <id> --level Warning"
    Then only Warning and above records are displayed

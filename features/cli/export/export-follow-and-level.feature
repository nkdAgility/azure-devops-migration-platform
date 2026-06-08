Feature: Export follow and level options
  As a migration operator
  I want to control diagnostic verbosity and optionally follow diagnostics inline
  So that I can troubleshoot issues without switching to the TUI

  Scenario: Export with default log level writes Information and above
    Given an operator runs "export --config migration.json" without a --level option
    When the job executes on the agent
    Then ".migration/Logs/agent.jsonl" in the package contains only Information level records and above

  Scenario: Export without follow in remote mode prints job ID and exits
    Given an operator runs "export --config migration.json --url https://cp.example.com"
    When the job is submitted successfully
    Then the CLI prints the job ID and exits immediately

  Scenario: Ctrl+C during follow detaches without cancelling the job
    Given an operator is following diagnostics for a running export
    When the operator presses Ctrl+C
    Then the CLI detaches from the diagnostic stream and exits
    And the job continues running on the server
    And the CLI prints a message suggesting using the TUI to resume watching

  Scenario: Standalone mode implies follow
    Given an operator runs "export --config migration.json" without a --url option
    When the control plane and agent start locally
    Then diagnostics stream to the console automatically
    And the local control plane uses the operator's --level setting

Feature: TUI Job List View
  As an operator
  I want to open the TUI and see a live-refreshing list of all my migration jobs
  So that I can monitor everything at a glance without using CLI commands

  Scenario: TUI displays job list when control plane is reachable
    Given a control plane is reachable at the configured URL
    When the operator runs "devopsmigration tui"
    Then a Terminal.Gui window displays a table of jobs showing job ID, state, mode, and submission timestamp

  Scenario: Job list refreshes when jobs change
    Given the TUI is open showing the job list
    When jobs are added or their state changes on the control plane
    Then the job list refreshes within 10 seconds and shows the updated states without restarting the TUI

  Scenario: TUI exits with error when control plane is unreachable
    Given no control plane is reachable at the configured URL
    When the operator runs "devopsmigration tui"
    Then the TUI exits with a clear actionable error message identifying which URL was attempted

  Scenario: TUI connects to default local URL when no --url flag is set
    Given the operator has not set --url or MIGRATION_API_URL
    When the operator runs "devopsmigration tui"
    Then the TUI attempts to connect to http://localhost:5100 without starting any services
    And if nothing is listening there it exits with an error advising to run a migration command first or pass --url

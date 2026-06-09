Feature: CLI Job ID and Control Plane URL on Submit
  As an operator who has just submitted a job
  I want to see the assigned Job ID and the control plane URL printed to the terminal
  So that I can copy the job ID for other commands and know which control plane is managing the job

  Scenario: Standalone mode shows local control plane URL
    Given the operator is in standalone mode with no --url and no MIGRATION_API_URL set
    When the job is accepted
    Then the output shows the local control plane URL http://localhost:5100 alongside the job ID

  Scenario: Remote mode shows the supplied --url
    Given the operator runs with --url https://my-control-plane.example.com
    When the job is accepted
    Then the output shows that remote URL alongside the job ID

  Scenario: Submission failure still shows the attempted URL
    Given the operator runs a migration command
    When submission fails due to a network error or validation rejection
    Then the control plane URL that was attempted is shown in the error output

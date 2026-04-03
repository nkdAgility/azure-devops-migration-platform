Feature: migrate logs command
  As a platform operator
  I want to retrieve and tail live progress events for a running job
  So that I can observe job execution in real time from the terminal

  @us3 @p2 @cli @logs
  Scenario: Snapshot mode prints NDJSON lines and exits 0
    Given a job "44444444-4444-4444-4444-444444444444" has stored ProgressEvents on the Control Plane
    When I run "migrate logs --job 44444444-4444-4444-4444-444444444444"
    Then each event is written to stdout as a compact JSON line
    And the command exits with code 0

  @us3 @p2 @cli @logs
  Scenario: Follow mode streams live events until the job completes
    Given a job "55555555-5555-5555-5555-555555555555" is in progress on the Control Plane
    When I run "migrate logs --job 55555555-5555-5555-5555-555555555555 --follow"
    Then each arriving event is written to stdout as a compact JSON line
    And when the stream ends the command exits with code 0

  @us3 @p2 @cli @logs
  Scenario: Ctrl+C exits cleanly without stopping the job on the Control Plane
    Given a job "66666666-6666-6666-6666-666666666666" is in progress on the Control Plane
    When I run "migrate logs --job 66666666-6666-6666-6666-666666666666 --follow"
    And a cancellation is requested during streaming
    Then the command exits with code 0
    And the job on the Control Plane is not stopped

  @us3 @p2 @cli @logs
  Scenario: HTTP error in snapshot mode exits 1
    Given the Control Plane returns an HTTP error for job "77777777-7777-7777-7777-777777777777"
    When I run "migrate logs --job 77777777-7777-7777-7777-777777777777"
    Then an error message is printed to stdout
    And the command exits with code 1

  @us3 @p2 @cli @logs
  Scenario: HTTP error in follow mode exits 1
    Given the Control Plane returns an HTTP error for job "88888888-8888-8888-8888-888888888888"
    When I run "migrate logs --job 88888888-8888-8888-8888-888888888888 --follow"
    Then an error message is printed to stdout
    And the command exits with code 1

  @us3 @p2 @cli @logs
  Scenario: HTTP 403 causes a permissions error message
    Given the Control Plane returns 403 for job "99999999-9999-9999-9999-999999999999"
    When I run "migrate logs --job 99999999-9999-9999-9999-999999999999"
    Then an error message is printed to stdout
    And the command exits with code 1

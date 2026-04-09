Feature: Package log download
  As a migration operator
  I want to download log files from the package via the control plane API
  So that I can access diagnostic and progress logs without direct filesystem access

  Scenario: Download progress log file from the package
    Given a completed job with "Logs/progress.jsonl" in the package
    When a client calls the download endpoint with type "progress"
    Then the response body contains the contents of "Logs/progress.jsonl"
    And the content type is "application/x-ndjson"

  Scenario: Download diagnostics log file from the package
    Given a completed job with "Logs/agent.jsonl" in the package
    When a client calls the download endpoint with type "diagnostics"
    Then the response body contains the contents of "Logs/agent.jsonl"
    And the content type is "application/x-ndjson"

  Scenario: Download works with filesystem package URI
    Given a completed job with a "file:///" package URI
    When the download endpoint is called
    Then the control plane reads from the filesystem artefact store and returns the file

  Scenario: Download returns 404 when log file does not exist
    Given a completed job where "Logs/agent.jsonl" was not produced
    When a client calls the download endpoint with type "diagnostics"
    Then the response status is 404

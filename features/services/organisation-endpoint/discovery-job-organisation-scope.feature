Feature: Discovery job organisation scope
  As a developer
  I want the discovery job to use scoped organisation endpoints for its organisation list
  So that the connection context type is consistent from job definition through to service invocation

  Scenario: Discovery job deserialises organisation entries as scoped endpoints
    Given a discovery job JSON with organisation entries containing URL, authentication, and projects
    When the job is deserialised
    Then each organisation entry is a scoped endpoint with an endpoint and project list populated

  Scenario: CLI commands construct scoped endpoints for discovery jobs
    Given a CLI command that creates a discovery job from configuration
    When the command builds the organisation list
    Then each organisation is represented as a scoped endpoint wrapping an organisation endpoint
    And the project list matches the configured projects

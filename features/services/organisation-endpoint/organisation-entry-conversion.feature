Feature: Organisation entry conversion to endpoint
  As a developer
  I want the mutable configuration entry to convert to an immutable organisation endpoint
  So that the transition from user configuration to runtime connection context is explicit and type-safe

  Scenario: Configuration entry converts to an endpoint with matching values
    Given a configuration entry with a URL, authentication type, and access token
    When the conversion method is called
    Then an organisation endpoint is returned with the resolved URL matching the source
    And the authentication type and resolved access token match the source values

  Scenario: Configuration entry with environment variable token resolves the value
    Given a configuration entry with an access token containing an environment variable reference
    When the conversion method is called
    Then the resolved access token in the endpoint contains the expanded environment variable value

  Scenario: Configuration entry with Windows authentication converts with no access token
    Given a configuration entry with Windows authentication and no access token
    When the conversion method is called
    Then the organisation endpoint has Windows authentication type
    And the resolved access token is absent

  Scenario: Configuration entry preserves API version on conversion
    Given a configuration entry with an API version specified
    When the conversion method is called
    Then the organisation endpoint API version matches the source value

Feature: Organisation endpoint service interfaces
  As a developer
  I want all service interfaces to accept an organisation endpoint as a single connection context
  So that connection credentials are never passed as separate string parameters

  Scenario: Service receives connection context through a single endpoint parameter
    Given a configured organisation with a URL and authentication
    When a service method is invoked with the organisation endpoint
    Then the service resolves the connection using the endpoint URL and authentication
    And no separate URL or access token parameters are required

  Scenario: Endpoint with PAT authentication provides resolved access token
    Given an organisation endpoint with PAT authentication and a resolved access token
    When the endpoint is passed to any service method
    Then the service reads the access token from the endpoint authentication

  Scenario: Endpoint with Windows authentication has no access token
    Given an organisation endpoint with Windows authentication
    When the endpoint is passed to a service method
    Then the service uses Windows-integrated authentication
    And the resolved access token is absent

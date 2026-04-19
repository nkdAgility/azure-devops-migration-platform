Feature: Polymorphic Endpoint Configuration Deserialization
  As a migration operator
  I want the platform to deserialize endpoint configuration to the correct concrete type
  So that each connector receives its specific connection fields without runtime cast errors

  @unit
  Scenario: AzureDevOpsServices JSON deserializes to AzureDevOpsEndpointOptions
    Given a JSON config with Source.Type "AzureDevOpsServices"
    When the config is deserialized
    Then the Source is an AzureDevOpsEndpointOptions instance
    And the Url and Authentication fields are populated

  @unit
  Scenario: Simulated JSON deserializes to SimulatedEndpointOptions
    Given a JSON config with Source.Type "Simulated"
    When the config is deserialized
    Then the Source is a SimulatedEndpointOptions instance
    And the Generator field is populated

  @unit
  Scenario: Unknown type discriminator fails with clear error
    Given a JSON config with Source.Type "UnknownConnector"
    When the config is deserialized
    Then a JsonException is thrown
    And the exception message contains "UnknownConnector"

  @unit
  Scenario: EndpointOptionsTypeRegistry prevents duplicate key registration
    Given an EndpointOptionsTypeRegistry
    When the same key "AzureDevOpsServices" is registered twice with different types
    Then an InvalidOperationException is thrown on the second registration

  @unit
  Scenario: EndpointOptionsTypeRegistry returns false for unknown keys
    Given an EndpointOptionsTypeRegistry
    When TryGetType is called with an unregistered key "NonExistent"
    Then the method returns false
    And the output type parameter is null

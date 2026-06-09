# Extraction Summary: config-polymorphic-endpoint-config

## Scenario → Test Mapping

| Scenario | Test Class | Test Method |
|---|---|---|
| AzureDevOpsServices JSON deserializes to AzureDevOpsEndpointOptions | PolymorphicEndpointOptionsConverterTests | Deserialize_AzureDevOpsServices_ReturnsAzureDevOpsEndpointOptions |
| Simulated JSON deserializes to SimulatedEndpointOptions | PolymorphicEndpointOptionsConverterTests | Deserialize_Simulated_ReturnsSimulatedEndpointOptions |
| Unknown type discriminator fails with clear error | PolymorphicEndpointOptionsConverterTests | Deserialize_UnknownType_ThrowsJsonException + Deserialize_UnknownType_ExceptionMessageContainsDiscriminatorValue |
| EndpointOptionsTypeRegistry prevents duplicate key registration | EndpointOptionsTypeRegistryTests | Register_DuplicateKeyWithDifferentType_ThrowsInvalidOperationException |
| EndpointOptionsTypeRegistry returns false for unknown keys | EndpointOptionsTypeRegistryTests | TryGetType_UnknownKey_ReturnsFalseAndNullType |

All tests carry [TestCategory("UnitTest")].

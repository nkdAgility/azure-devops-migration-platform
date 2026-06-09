# DSL Design: config-polymorphic-endpoint-config

## Decision
All scenarios map directly to pre-existing MSTest [TestMethod] implementations. No new DSL helpers required.

## Test Classes
- `PolymorphicEndpointOptionsConverterTests` — converter deserialization scenarios
- `EndpointOptionsTypeRegistryTests` — registry behaviour scenarios

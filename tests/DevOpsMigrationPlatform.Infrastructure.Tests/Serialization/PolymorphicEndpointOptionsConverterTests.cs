// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Serialization;

[TestClass]
public sealed class PolymorphicEndpointOptionsConverterTests
{
    private static JsonSerializerOptions BuildOptions(EndpointOptionsTypeRegistry registry)
    {
        var converter = new PolymorphicEndpointOptionsConverter(registry);
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        return options;
    }

    [TestMethod]
    public void Deserialize_AzureDevOpsServices_ReturnsAzureDevOpsEndpointOptions()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));

        var json = """
            {
              "Type": "AzureDevOpsServices",
              "Url": "https://dev.azure.com/myorg",
              "Project": "MyProject"
            }
            """;

        var options = BuildOptions(registry);
        var result = JsonSerializer.Deserialize<MigrationEndpointOptions>(json, options);

        Assert.IsInstanceOfType<AzureDevOpsEndpointOptions>(result);
        var ado = (AzureDevOpsEndpointOptions)result!;
        Assert.AreEqual("AzureDevOpsServices", ado.Type);
        Assert.AreEqual("https://dev.azure.com/myorg", ado.Url);
    }

    [TestMethod]
    public void Deserialize_Simulated_ReturnsSimulatedEndpointOptions()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("Simulated", typeof(SimulatedEndpointOptions));

        var json = """
            {
              "Type": "Simulated",
              "Generator": {
                "Projects": []
              }
            }
            """;

        var options = BuildOptions(registry);
        var result = JsonSerializer.Deserialize<MigrationEndpointOptions>(json, options);

        Assert.IsInstanceOfType<SimulatedEndpointOptions>(result);
        Assert.AreEqual("Simulated", result!.Type);
    }

    [TestMethod]
    public void Deserialize_UnknownType_ThrowsJsonException()
    {
        var registry = new EndpointOptionsTypeRegistry();

        var json = """{ "Type": "UnknownConnector" }""";

        var options = BuildOptions(registry);
        Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<MigrationEndpointOptions>(json, options));
    }

    [TestMethod]
    public void Deserialize_UnknownType_ExceptionMessageContainsDiscriminatorValue()
    {
        var registry = new EndpointOptionsTypeRegistry();

        var json = """{ "Type": "SpecialConnector" }""";

        var options = BuildOptions(registry);

        var ex = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<MigrationEndpointOptions>(json, options));

        StringAssert.Contains(ex.Message, "SpecialConnector");
    }
}

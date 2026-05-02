// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Provides in-memory configuration test doubles for CLI command testing.
/// Enables isolated testing without external configuration file dependencies.
/// </summary>
public static class InMemoryTestConfiguration
{
    /// <summary>
    /// Creates a test configuration from key-value pairs using in-memory collection.
    /// </summary>
    /// <param name="values">Configuration key-value pairs</param>
    /// <returns>IConfiguration instance for testing</returns>
    public static IConfiguration Create(params (string key, string value)[] values)
    {
        var configData = values.ToDictionary(x => x.key, x => (string?)x.value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    /// <summary>
    /// Creates a test configuration with default CLI-specific settings.
    /// </summary>
    /// <returns>IConfiguration with common test defaults</returns>
    public static IConfiguration CreateDefault()
    {
        var defaultConfig = new Dictionary<string, string?>
        {
            ["Migration:Source:Type"] = "AzureDevOps",
            ["Migration:Source:Url"] = "https://dev.azure.com/test-org",
            ["Migration:Target:Type"] = "AzureDevOps",
            ["Migration:Target:Url"] = "https://dev.azure.com/target-org",
            ["Migration:PackageRoot"] = "C:\\temp\\test-package",
            ["Telemetry:ApplicationInsights:InstrumentationKey"] = "test-key",
            ["Telemetry:ApplicationInsights:Enabled"] = "false"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(defaultConfig)
            .Build();
    }

    /// <summary>
    /// Creates a test configuration for inventory command testing.
    /// </summary>
    /// <returns>IConfiguration with inventory-specific settings</returns>
    public static IConfiguration CreateInventoryConfig()
    {
        var inventoryConfig = new Dictionary<string, string?>
        {
            ["Migration:Source:Type"] = "TfsObjectModel",
            ["Migration:Source:Url"] = "http://tfs-server:8080/tfs/collection",
            ["Migration:Source:PersonalAccessToken"] = "test-pat",
            ["Inventory:OutputPath"] = "C:\\temp\\inventory-output",
            ["Inventory:AllProjects"] = "true"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inventoryConfig)
            .Build();
    }

    /// <summary>
    /// Creates a test configuration that will fail validation.
    /// Used for testing error handling scenarios.
    /// </summary>
    /// <returns>IConfiguration with invalid settings</returns>
    public static IConfiguration CreateInvalidConfig()
    {
        var invalidConfig = new Dictionary<string, string?>
        {
            ["Migration:Source:Type"] = "", // Invalid empty type
            ["Migration:Source:Url"] = "not-a-valid-url", // Invalid URL format
            // Missing required Target configuration
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(invalidConfig)
            .Build();
    }
}
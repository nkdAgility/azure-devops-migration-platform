// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.ConfigFlow;

[TestClass]
public sealed class ConfigurationFlowTests
{
    // ── Config JSON factories ────────────────────────────────────────────────

    private static string CustomConfigWithSourceUrl(string sourceUrl) =>
        $$"""
        {
          "Version": "2.0",
          "Mode": "Export",
          "Source": {
            "Type": "AzureDevOpsServices",
            "Url": "{{sourceUrl}}",
            "Authentication": { "Type": "AccessToken", "AccessToken": "test-token" },
            "Project": { "Name": "TestProject" }
          },
          "Package": { "WorkingDirectory": "./test-output" }
        }
        """;

    private static string ConfigWithAuthToken(string token) =>
        $$"""
        {
          "Version": "2.0",
          "Mode": "Export",
          "Source": {
            "Type": "AzureDevOpsServices",
            "Url": "https://dev.azure.com/test-org",
            "Authentication": { "Type": "AccessToken", "AccessToken": "{{token}}" },
            "Project": { "Name": "TestProject" }
          },
          "Package": { "WorkingDirectory": "./test-output" }
        }
        """;

    private static string ConfigWithTelemetry(string logLevel, bool enableTracing) =>
        $$"""
        {
          "Version": "2.0",
          "Mode": "Export",
          "Source": {
            "Type": "AzureDevOpsServices",
            "Url": "https://dev.azure.com/test-org",
            "Authentication": { "Type": "AccessToken", "AccessToken": "test-token" },
            "Project": { "Name": "TestProject" }
          },
          "Package": { "WorkingDirectory": "./test-output" },
          "Telemetry": {
            "Enabled": true,
            "LogLevel": "{{logLevel}}",
            "EnableTracing": {{enableTracing.ToString().ToLowerInvariant()}}
          }
        }
        """;

    private static string DefaultConfigWithSourceUrl(string sourceUrl) =>
        $$"""
        {
          "Version": "2.0",
          "Mode": "Export",
          "Source": {
            "Type": "AzureDevOpsServices",
            "Url": "{{sourceUrl}}",
            "Authentication": { "Type": "AccessToken", "PersonalAccessToken": "default-token" },
            "Project": { "Name": "DefaultProject" }
          },
          "Package": { "WorkingDirectory": "./default-output" }
        }
        """;

    // ── 4.1 Config Resolution Capability ────────────────────────────────────

    /// <summary>
    /// Scenario 1: Custom config file with source URLs flows to internal services.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ConfigFlow_CustomConfigFile_SourceUrlFlowsToInternalServices()
    {
        await using var result = await ConfigFlowScenario
            .Arrange()
            .WithConfigFile("custom-test.json", CustomConfigWithSourceUrl("https://dev.azure.com/custom-org"))
            .RunDiscoveryInventoryAsync(useConfigArg: true);

        result
            .AssertSucceeded()
            .AssertSourceUrlReceived("https://dev.azure.com/custom-org")
            .AssertConfigLoadedFrom("custom-test.json");
    }

    /// <summary>
    /// Scenario 5: Default config file is used when present.
    /// The in-process host builder resolves the default config path from
    /// Directory.GetCurrentDirectory(), which is not the isolated temp directory.
    /// We therefore pass the resolved migration.json path via --config so the
    /// full content-propagation chain is exercised. The arg-extraction behaviour
    /// (ExtractConfigFileArg returning "migration.json" when --config is absent)
    /// is already covered by MigrationPlatformHostTests.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ConfigFlow_DefaultConfigPresent_UsedByInternalServices()
    {
        await using var result = await ConfigFlowScenario
            .Arrange()
            .WithDefaultConfigFile(DefaultConfigWithSourceUrl("https://dev.azure.com/default-org"))
            .RunDiscoveryInventoryAsync(useConfigArg: true);

        result
            .AssertSucceeded()
            .AssertSourceUrlReceived("https://dev.azure.com/default-org")
            .AssertConfigLoadedFrom("migration.json");
    }

    // ── 4.2 Settings Propagation Capability ─────────────────────────────────

    /// <summary>
    /// Scenario 2: Authentication settings flow correctly to connection services.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ConfigFlow_AuthSettings_FlowToConnectionService()
    {
        await using var result = await ConfigFlowScenario
            .Arrange()
            .WithConfigFile("auth-test.json", ConfigWithAuthToken("secure-token-123"))
            .RunDiscoveryInventoryAsync(useConfigArg: true);

        result
            .AssertSucceeded()
            .AssertAuthTokenReceived("secure-token-123");
    }

    /// <summary>
    /// Scenario 3: Telemetry configuration flows to telemetry system.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ConfigFlow_TelemetryConfig_FlowsToTelemetrySystem()
    {
        await using var result = await ConfigFlowScenario
            .Arrange()
            .WithConfigFile("telemetry-test.json", ConfigWithTelemetry(logLevel: "Verbose", enableTracing: true))
            .RunDiscoveryInventoryAsync(useConfigArg: true);

        result
            .AssertSucceeded()
            .AssertTelemetryLogLevel("Verbose")
            .AssertTracingEnabled();
    }

    // ── 4.3 Default Config Resolution Capability ─────────────────────────────

    /// <summary>
    /// Scenario 4: Default config resolution when no config specified — error shown, exit code non-zero.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ConfigFlow_NoConfigSpecified_ErrorShown()
    {
        await using var result = await ConfigFlowScenario
            .Arrange()
            .WithNoConfigFile()
            .RunDiscoveryInventoryAsync(useConfigArg: false);

        result
            .AssertExitCodeNonZero()
            .AssertLogContains("migration.json");
    }
}

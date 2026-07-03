// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Config;

/// <summary>
/// ConfigVersion 2.0 hard-cutover gate (ADR 0028, MC-H2): v1 configs must fail
/// with a precise, actionable rewrite message — never bind silently.
/// </summary>
[TestClass]
public class ConfigVersionGateTests
{
    private static MigrationPlatformOptions ValidExportOptions(string configVersion) => new()
    {
        ConfigVersion = configVersion,
        Mode = "Export",
        Source = new AzureDevOpsEndpointOptions
        {
            Type = "AzureDevOpsServices",
            Url = "https://dev.azure.com/myorg",
            Project = "MyProject"
        },
        Package = new MigrationPackageOptions { WorkingDirectory = "C:\\tmp\\pkg" }
    };

    [TestMethod]
    [TestCategory("L0")]
    public void Validate_ConfigVersion1_FailsWithUpgradeInstructions()
    {
        var validator = new MigrationPlatformOptionsValidator();
        var result = validator.Validate(null, ValidExportOptions("1.0"));

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "configuration version '1.0', which is no longer supported");
        StringAssert.Contains(result.FailureMessage, "requires ConfigVersion '2.0'");
        StringAssert.Contains(result.FailureMessage, "Rename 'Scope' to 'Selection'");
    }

    [TestMethod]
    [TestCategory("L0")]
    public void Validate_ConfigVersion2_Passes()
    {
        var validator = new MigrationPlatformOptionsValidator();
        var result = validator.Validate(null, ValidExportOptions("2.0"));
        Assert.IsTrue(result.Succeeded, result.FailureMessage);
    }

    // ── JSON-level v1-shape detection in ConfigurationService ────────────────

    private static ConfigurationService CreateService()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("Simulated", typeof(SimulatedEndpointOptions));
        registry.Register("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        registry.RegisterOrganisationEntry("Simulated", typeof(SimulatedOrganisationEntry));
        return new ConfigurationService(
            NullLogger<ConfigurationService>.Instance,
            new PolymorphicEndpointOptionsConverter(registry),
            new PolymorphicOrganisationEntryConverter(registry));
    }

    private static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"migration-gate-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    [TestCategory("L0")]
    public async Task Load_V1Config_FailsWithUpgradeInstructions()
    {
        var path = WriteTempConfig("""
        { "MigrationPlatform": { "ConfigVersion": "1.0", "Mode": "Export",
          "Modules": { "WorkItems": { "Enabled": true, "Scope": { "Query": "Q" }, "Extensions": { "Revisions": { "Enabled": true } } } } } }
        """);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => CreateService().LoadConfigurationAsync(path));
            StringAssert.Contains(ex.Message, "no longer supported");
            StringAssert.Contains(ex.Message, "Rename 'Scope' to 'Selection'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("L0")]
    public async Task Load_V2ConfigWithStrayLegacyKeys_NamesTheKeys()
    {
        var path = WriteTempConfig("""
        { "MigrationPlatform": { "ConfigVersion": "2.0", "Mode": "Export",
          "Modules": { "WorkItems": { "Enabled": true, "Scope": { "Query": "Q" } } } } }
        """);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => CreateService().LoadConfigurationAsync(path));
            StringAssert.Contains(ex.Message, "legacy key(s) 'Scope'");
            StringAssert.Contains(ex.Message, "removed in ConfigVersion 2.0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("L0")]
    public async Task Load_TeamsV1StringScope_IsRejected()
    {
        var path = WriteTempConfig("""
        { "MigrationPlatform": { "ConfigVersion": "2.0", "Mode": "Export",
          "Modules": { "Teams": { "Enabled": true, "Scope": "all" } } } }
        """);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => CreateService().LoadConfigurationAsync(path));
            StringAssert.Contains(ex.Message, "'Modules.Teams' contains legacy key(s) 'Scope'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("L0")]
    public async Task Save_WritesConfigVersion2()
    {
        var path = Path.Combine(Path.GetTempPath(), $"migration-gate-{Guid.NewGuid():N}.json");
        try
        {
            await CreateService().SaveConfigurationAsync(ValidExportOptions("2.0"), path);
            StringAssert.Contains(File.ReadAllText(path), "\"ConfigVersion\": \"2.0\"");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

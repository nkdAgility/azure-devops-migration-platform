// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
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
}

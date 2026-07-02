// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

/// <summary>
/// TC-M2 / ADR-0026: the Tool contract mandates singleton lifetime for Tools.
/// <see cref="IFieldTransformTool"/> must be a DI singleton whose effective options
/// still follow the current package configuration (per-job config-accessor indirection),
/// preserving the per-job options behaviour the previous scoped registration provided.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public sealed class FieldTransformToolLifetimeTests
{
    private static IConfiguration BuildConfig(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Tools:FieldTransform:Enabled"] = enabled ? "true" : "false",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Name"] = "g1",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Transforms:0:Name"] = "r1",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Transforms:0:Type"] = "SetField",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Transforms:0:Field"] = "System.Title",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Transforms:0:Value"] = "x",
        }).Build();

    [TestMethod]
    public void FieldTransformTool_IsRegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFieldTransformToolServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentPackageConfigAccessor>().Set(BuildConfig(enabled: true));

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var tool1 = scope1.ServiceProvider.GetRequiredService<IFieldTransformTool>();
        var tool2 = scope2.ServiceProvider.GetRequiredService<IFieldTransformTool>();

        Assert.AreSame(tool1, tool2,
            "IFieldTransformTool must be a DI singleton (Tool contract, TC-M2).");
    }

    [TestMethod]
    public void FieldTransformTool_FollowsCurrentPackageConfig_AcrossJobs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFieldTransformToolServices();

        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ICurrentPackageConfigAccessor>();
        var tool = provider.GetRequiredService<IFieldTransformTool>();

        // Job 1: transforms enabled for import.
        accessor.Set(BuildConfig(enabled: true));
        Assert.IsTrue(tool.IsEnabledForPhase(FieldTransformPhase.Import),
            "Tool must see the enabled config of the current job.");

        // Job 2: same singleton, new package config with the tool disabled.
        accessor.Set(BuildConfig(enabled: false));
        Assert.IsFalse(tool.IsEnabledForPhase(FieldTransformPhase.Import),
            "Singleton tool must re-resolve options from the current package config per job (TC-M2).");
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools;

[TestClass]
public sealed class ToolOptionsConfigurationTests
{
    [TestMethod]
    public void FieldTransformToolServices_BindOptions_FromCurrentPackageConfigAccessor()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Tools:FieldTransform:Enabled"] = "true",
            ["MigrationPlatform:Tools:FieldTransform:TransformGroups:0:Name"] = "default"
        });

        var services = new ServiceCollection();
        services.AddFieldTransformToolServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentPackageConfigAccessor>().Set(config);
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<FieldTransformOptions>>().Value;

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(1, options.TransformGroups.Count);
        Assert.AreEqual("default", options.TransformGroups[0].Name);
    }

    [TestMethod]
    public void IdentityLookupToolServices_BindOptions_FromCurrentPackageConfigAccessor()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Tools:IdentityLookup:Enabled"] = "true",
            ["MigrationPlatform:Tools:IdentityLookup:DefaultIdentity"] = "contoso.local"
        });

        var services = new ServiceCollection();
        services.AddIdentityLookupToolServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentPackageConfigAccessor>().Set(config);
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityLookupOptions>>().Value;

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual("contoso.local", options.DefaultIdentity);
    }

    [TestMethod]
    public void NodeTranslationToolServices_BindOptions_FromCurrentPackageConfigAccessor()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Tools:NodeTranslation:Enabled"] = "true",
            ["MigrationPlatform:Tools:NodeTranslation:AutoCreateNodes"] = "true"
        });

        var services = new ServiceCollection();
        services.AddNodeTranslationToolServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentPackageConfigAccessor>().Set(config);
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<NodeTranslationOptions>>().Value;

        Assert.IsTrue(options.Enabled);
        Assert.IsTrue(options.AutoCreateNodes);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
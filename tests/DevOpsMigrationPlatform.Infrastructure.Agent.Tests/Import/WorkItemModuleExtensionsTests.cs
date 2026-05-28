// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemType;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class WorkItemModuleExtensionsTests
{
    [TestMethod]
    public void RegisterWorkItemServices_RegistersWorkItemOptionsValidator()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemServices(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidateOptions<WorkItemOptions>>();

        Assert.IsTrue(validators.Any(v => v is WorkItemOptionsValidator));
    }

    [TestMethod]
    public void RegisterWorkItemServices_InvalidWorkItemOptions_ThrowsOptionsValidationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{WorkItemOptions.SectionName}:RevisionReplay"] = "false",
                [$"{WorkItemOptions.SectionName}:LinkReplay"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.RegisterWorkItemServices(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WorkItemOptions>>();

        Assert.ThrowsExactly<OptionsValidationException>(() => _ = options.Value);
    }

    [TestMethod]
    public void RegisterWorkItemServices_RegistersNodePathValidatorFailurePattern()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemServices(new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(
            descriptor => descriptor.ServiceType == typeof(IImportFailurePattern)
                           && descriptor.ImplementationType == typeof(NodePathValidator)));
    }

    [TestMethod]
    public void RegisterWorkItemServices_RegistersWorkItemTypeValidatorFailurePattern()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemServices(new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(
            descriptor => descriptor.ServiceType == typeof(IImportFailurePattern)
                          && descriptor.ImplementationType == typeof(WorkItemTypeValidator)));
    }

    [TestMethod]
    public void RegisterWorkItemServices_RegistersIdentityMappingValidatorFailurePattern()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemServices(new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(
            descriptor => descriptor.ServiceType == typeof(IImportFailurePattern)
                          && descriptor.ImplementationType == typeof(IdentityMappingValidator)));
    }
}

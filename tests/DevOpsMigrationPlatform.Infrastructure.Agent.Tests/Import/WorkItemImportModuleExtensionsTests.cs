// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class WorkItemImportModuleExtensionsTests
{
    [TestMethod]
    public void RegisterWorkItemImportServices_RegistersWorkItemImportOptionsValidator()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemImportServices(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidateOptions<WorkItemImportOptions>>();

        Assert.IsTrue(validators.Any(v => v is WorkItemImportOptionsValidator));
    }

    [TestMethod]
    public void RegisterWorkItemImportServices_InvalidWorkItemImportOptions_ThrowsOptionsValidationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{WorkItemImportOptions.SectionName}:RevisionReplay"] = "false",
                [$"{WorkItemImportOptions.SectionName}:LinkReplay"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.RegisterWorkItemImportServices(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WorkItemImportOptions>>();

        Assert.ThrowsExactly<OptionsValidationException>(() => _ = options.Value);
    }

    [TestMethod]
    public void RegisterWorkItemImportServices_RegistersNodePathValidatorFailurePattern()
    {
        var services = new ServiceCollection();

        services.RegisterWorkItemImportServices(new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(
            descriptor => descriptor.ServiceType == typeof(IImportFailurePattern)
                          && descriptor.ImplementationType == typeof(NodePathValidator)));
    }
}

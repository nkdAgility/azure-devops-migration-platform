// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Export;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools;

[TestClass]
public sealed class SimulatedPackageConfigBindingTests
{
    [TestMethod]
    public async Task SimulatedWorkItemDiscoveryService_ReadsGenerator_FromCurrentPackageConfigAccessor()
    {
        var accessor = new CurrentPackageConfigAccessor();
        accessor.Set(BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Source:Generator:Projects:0:Name"] = "ProjectA",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:Type"] = "Bug",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:Count"] = "3",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:RevisionsPerItem"] = "2"
        }));

        var service = new SimulatedWorkItemDiscoveryService(accessor);

        ProjectDiscoverySummary? summary = null;
        await foreach (var item in service.CountWorkItemsAsync(new OrganisationEndpoint(), "ProjectA", cancellationToken: CancellationToken.None))
        {
            summary = item;
        }

        Assert.IsNotNull(summary);
        Assert.AreEqual(3, summary.WorkItemsCount);
        Assert.AreEqual(6, summary.RevisionsCount);
    }

    [TestMethod]
    public async Task SimulatedWorkItemRevisionSourceFactory_ReadsGenerator_FromCurrentPackageConfigAccessor()
    {
        var accessor = new CurrentPackageConfigAccessor();
        accessor.Set(BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Source:Generator:Projects:0:Name"] = "ProjectA",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:Type"] = "Task",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:Count"] = "2",
            ["MigrationPlatform:Source:Generator:Projects:0:WorkItemTypes:0:RevisionsPerItem"] = "1"
        }));

        var factory = new SimulatedWorkItemRevisionSourceFactory(accessor);
        var source = await factory.CreateAsync(CancellationToken.None);

        var revisions = new List<WorkItemRevision>();
        await foreach (var revision in source.GetRevisionsAsync(CancellationToken.None))
        {
            revisions.Add(revision);
        }

        Assert.AreEqual(2, revisions.Count);
        Assert.AreEqual("ProjectA", revisions[0].Fields.First(f => f.ReferenceName == "System.TeamProject").Value);
        Assert.AreEqual("Task", revisions[0].Fields.First(f => f.ReferenceName == "System.WorkItemType").Value);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
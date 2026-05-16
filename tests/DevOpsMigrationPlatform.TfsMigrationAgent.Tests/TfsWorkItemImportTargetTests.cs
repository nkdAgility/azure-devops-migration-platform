// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public sealed class TfsWorkItemImportTargetTests
{
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsTrue_ForKnownType()
    {
        var target = new TfsWorkItemImportTarget(["Bug", "User Story"]);

        var exists = await target.WorkItemTypeExistsAsync(" bug ", CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsFalse_ForUnknownType()
    {
        var target = new TfsWorkItemImportTarget(["Bug", "User Story"]);

        var exists = await target.WorkItemTypeExistsAsync("Task", CancellationToken.None);

        Assert.IsFalse(exists);
    }

    [TestMethod]
    public void AddTfsMigrationAgentServices_RegistersTfsWorkItemImportTargetFactory()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddTfsMigrationAgentServices(configuration, new Uri("http://localhost:5100"));

        var factoryDescriptor = services.Single(d => d.ServiceType == typeof(IWorkItemImportTargetFactory));
        Assert.AreEqual(typeof(TfsActiveJobWorkItemImportTargetFactory), factoryDescriptor.ImplementationType);
    }
}

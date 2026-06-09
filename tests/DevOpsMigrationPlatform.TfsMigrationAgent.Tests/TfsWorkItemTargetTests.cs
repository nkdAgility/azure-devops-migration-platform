// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemType;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public sealed class TfsWorkItemTargetTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsTrue_ForKnownType()
    {
        var target = new TfsWorkItemTypeReadinessTarget(["Bug", "User Story"]);

        var exists = await target.WorkItemTypeExistsAsync(" bug ", CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsFalse_ForUnknownType()
    {
        var target = new TfsWorkItemTypeReadinessTarget(["Bug", "User Story"]);

        var exists = await target.WorkItemTypeExistsAsync("Task", CancellationToken.None);

        Assert.IsFalse(exists);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddTfsMigrationAgentServices_RegistersTfsWorkItemTypeReadinessTargetFactory()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddTfsMigrationAgentServices(configuration, new Uri("http://localhost:5100"));

        var factoryDescriptor = services.Single(d => d.ServiceType == typeof(TfsActiveJobWorkItemTypeReadinessTargetFactory));
        Assert.AreEqual(typeof(TfsActiveJobWorkItemTypeReadinessTargetFactory), factoryDescriptor.ImplementationType);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddTfsMigrationAgentServices_DoesNotOverrideExistingReadinessFactoryRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var existingFactory = new Mock<IWorkItemTypeReadinessTargetFactory>(MockBehavior.Strict).Object;

        services.AddSingleton(existingFactory);
        services.AddTfsMigrationAgentServices(configuration, new Uri("http://localhost:5100"));

        var readinessFactoryDescriptors = services
            .Where(d => d.ServiceType == typeof(IWorkItemTypeReadinessTargetFactory))
            .ToArray();

        Assert.AreEqual(1, readinessFactoryDescriptors.Length);
        Assert.AreSame(existingFactory, readinessFactoryDescriptors[0].ImplementationInstance);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveProjectName_Throws_WhenProjectNameIsMissing()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => TfsActiveJobWorkItemTypeReadinessTargetFactory.ResolveProjectName("   ", ["ProjectA"]));

        StringAssert.Contains(exception.Message, "missing");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveProjectName_Throws_WithAvailableProjects_WhenProjectNameIsInvalid()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => TfsActiveJobWorkItemTypeReadinessTargetFactory.ResolveProjectName("MissingProject", ["ProjectA", "ProjectB"]));

        StringAssert.Contains(exception.Message, "MissingProject");
        StringAssert.Contains(exception.Message, "ProjectA");
        StringAssert.Contains(exception.Message, "ProjectB");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveProjectName_ReturnsCanonicalProjectName_WhenInputCaseDiffers()
    {
        var projectName = TfsActiveJobWorkItemTypeReadinessTargetFactory.ResolveProjectName("projecta", ["ProjectA", "ProjectB"]);

        Assert.AreEqual("ProjectA", projectName);
    }
}

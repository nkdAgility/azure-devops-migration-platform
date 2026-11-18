// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.TfsExecution;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #171 live E2E: the TFS agent registered no IWorkItemFetchService, so selection filters were
/// skipped with a warning and every work item was exported. The agent now gets the active job's
/// fetch service, like its revision source.
/// </summary>
[TestClass]
public class TfsActiveJobWorkItemFetchServiceTests
{
    // PR #222 review: resolve what DI binds, so a wrong registration fails, not only a missing one.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task AddTfsObjectModelModule_ResolvesFetchAndAttachmentSourcesFromTheActiveJob()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTfsObjectModelModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var scope = new WorkItemFetchScope(new[] { "System.Id" });
        var attachment = new AttachmentMetadata { OriginalName = "a.png" };
        var jobFetch = new Mock<IWorkItemFetchService>();
        jobFetch.Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "P", scope, It.IsAny<CancellationToken>()))
            .Returns(Items(new FetchedWorkItem(7, new Dictionary<string, object?>())));
        var jobAttachments = new Mock<IAttachmentBinarySource>();
        jobAttachments.Setup(s => s.GetBytesAsync(3, 1, attachment, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 9 });
        var jobServices = new Mock<ITfsJobServices>();
        jobServices.SetupGet(s => s.FetchService).Returns(jobFetch.Object);
        jobServices.SetupGet(s => s.AttachmentSource).Returns(jobAttachments.Object);
        provider.GetRequiredService<ActiveTfsJobServices>().Current = jobServices.Object;

        var ids = new List<int>();
        await foreach (var item in provider.GetRequiredService<IWorkItemFetchService>().FetchAsync(new OrganisationEndpoint(), "P", scope))
            ids.Add(item.Id);
        var bytes = await provider.GetRequiredService<IAttachmentBinarySource>().GetBytesAsync(3, 1, attachment, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 7 }, ids);
        CollectionAssert.AreEqual(new byte[] { 9 }, bytes);
    }
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FetchAsync_ReadsFromTheActiveJobsFetchService()
    {
        var scope = new WorkItemFetchScope(new[] { "System.Id" });
        var jobFetch = new Mock<IWorkItemFetchService>();
        jobFetch.Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "P", scope, It.IsAny<CancellationToken>()))
            .Returns(Items(new FetchedWorkItem(7, new Dictionary<string, object?>())));
        var jobServices = new Mock<ITfsJobServices>();
        jobServices.SetupGet(s => s.FetchService).Returns(jobFetch.Object);
        var active = new ActiveTfsJobServices { Current = jobServices.Object };

        var ids = new List<int>();
        await foreach (var item in new TfsActiveJobWorkItemFetchService(active).FetchAsync(new OrganisationEndpoint(), "P", scope))
            ids.Add(item.Id);

        CollectionAssert.AreEqual(new[] { 7 }, ids);
    }

    // #171 live E2E: "No IAttachmentBinarySource is registered — attachment binaries will NOT be
    // written to the package" on every TFS export.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AttachmentSource_IsRegisteredAndReadsFromTheActiveJob()
    {
        var services = new ServiceCollection();
        services.AddTfsObjectModelModule(new ConfigurationBuilder().Build());
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(DevOpsMigrationPlatform.Abstractions.Agent.Attachments.IAttachmentBinarySource)));

        var attachment = new AttachmentMetadata { OriginalName = "a.png" };
        var jobSource = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Attachments.IAttachmentBinarySource>();
        jobSource.Setup(s => s.GetBytesAsync(3, 1, attachment, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 9 });
        var jobServices = new Mock<ITfsJobServices>();
        jobServices.SetupGet(s => s.AttachmentSource).Returns(jobSource.Object);
        var active = new ActiveTfsJobServices { Current = jobServices.Object };

        var bytes = await new TfsActiveJobAttachmentBinarySource(active).GetBytesAsync(3, 1, attachment, CancellationToken.None);

        CollectionAssert.AreEqual(new byte[] { 9 }, bytes);
    }

    private static async IAsyncEnumerable<FetchedWorkItem> Items(params FetchedWorkItem[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }
}

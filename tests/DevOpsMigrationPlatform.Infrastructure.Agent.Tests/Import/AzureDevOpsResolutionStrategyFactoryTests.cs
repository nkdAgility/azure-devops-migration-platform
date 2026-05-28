// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public sealed class AzureDevOpsResolutionStrategyFactoryTests
{
    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsEmpty_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(out _, out _, out _);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.CreateAsync(
                new WorkItemResolutionStrategyOptions(),
                Mock.Of<IWorkItemTarget>(),
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateAsync_WhenTargetIsNotAzureDevOpsTarget_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(out _, out _, out _);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.CreateAsync(
                new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" },
                Mock.Of<IWorkItemTarget>(),
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsTargetField_ReturnsTargetFieldResolutionStrategy()
    {
        var sut = CreateSut(out var target, out _, out _);

        var strategy = await sut.CreateAsync(
            new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" },
            target,
            new TestTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType<TargetFieldResolutionStrategy>(strategy);
    }

    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsTargetHyperlink_ReturnsTargetHyperlinkResolutionStrategy()
    {
        var sut = CreateSut(out var target, out _, out _);

        var strategy = await sut.CreateAsync(
            new WorkItemResolutionStrategyOptions { Strategy = "TargetHyperlink", UrlPattern = "https://source.example/wi/{id}" },
            target,
            new TestTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType<TargetHyperlinkResolutionStrategy>(strategy);
    }

    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsUnknown_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(out var target, out _, out _);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.CreateAsync(
                new WorkItemResolutionStrategyOptions { Strategy = "UnknownMode" },
                target,
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    private static AzureDevOpsResolutionStrategyFactory CreateSut(
        out AzureDevOpsWorkItemTarget target,
        out WorkItemTrackingHttpClient witClient,
        out Mock<IAzureDevOpsClientFactory> clientFactory)
    {
        witClient = new WorkItemTrackingHttpClient(
            new Uri("https://dev.azure.com/contoso"),
            new VssBasicCredential(string.Empty, "token"));

        clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        clientFactory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient);

        target = new AzureDevOpsWorkItemTarget(witClient, "Shop", "https://dev.azure.com/contoso");
        return new AzureDevOpsResolutionStrategyFactory(
            clientFactory.Object,
            NullLogger<TargetFieldResolutionStrategy>.Instance);
    }

    private sealed class TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public string Url => "https://dev.azure.com/contoso";
        public string Project => "Shop";
        public string ConnectorType => "AzureDevOpsServices";
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }
}

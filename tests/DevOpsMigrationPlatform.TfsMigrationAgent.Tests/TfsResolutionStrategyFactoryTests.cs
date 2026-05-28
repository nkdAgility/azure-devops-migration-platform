// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Platform.Configuration;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public sealed class TfsResolutionStrategyFactoryTests
{
    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsEmpty_ReturnsNullResolutionStrategy()
    {
        var factory = new TfsResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions();

        var strategy = await factory.CreateAsync(
            options,
            Mock.Of<IWorkItemTarget>(),
            new TestTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType(strategy, typeof(NullResolutionStrategy));
    }

    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsExplicit_ThrowsInvalidOperationException()
    {
        var factory = new TfsResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "UnsupportedMode", FieldName = "Custom.SourceId" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(
                options,
                Mock.Of<IWorkItemTarget>(),
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateAsync_WhenTargetFieldStrategyProvided_ReturnsTfsTargetFieldResolutionStrategy()
    {
        var factory = new TfsResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" };

        var strategy = await factory.CreateAsync(
            options,
            Mock.Of<IWorkItemTarget>(),
            new TestTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType(strategy, typeof(TfsTargetFieldResolutionStrategy));
    }

    [TestMethod]
    public async Task CreateAsync_WhenTargetEndpointUrlHasWhitespace_StillReturnsTfsTargetFieldResolutionStrategy()
    {
        var factory = new TfsResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" };

        var strategy = await factory.CreateAsync(
            options,
            Mock.Of<IWorkItemTarget>(),
            new WhitespaceTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType(strategy, typeof(TfsTargetFieldResolutionStrategy));
    }

    [TestMethod]
    public async Task CreateAsync_WhenTargetFieldWithoutFieldName_ThrowsInvalidOperationException()
    {
        var factory = new TfsResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(
                options,
                Mock.Of<IWorkItemTarget>(),
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    [TestMethod]
    public void TeamFoundationServerEndpointOptions_ResolvedUrl_TrimsEnvironmentValue()
    {
        const string envName = "DMP_TEST_TFS_ENDPOINT_URL";
        Environment.SetEnvironmentVariable(envName, "  http://tfs.example.local:8080/tfs/DefaultCollection  ");
        try
        {
            var options = new TeamFoundationServerEndpointOptions { Url = $"$ENV:{envName}" };

            Assert.AreEqual("http://tfs.example.local:8080/tfs/DefaultCollection", options.ResolvedUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [TestMethod]
    public void TeamFoundationServerOrganisationEntry_ResolvedUrl_TrimsEnvironmentValue()
    {
        const string envName = "DMP_TEST_TFS_ORG_URL";
        Environment.SetEnvironmentVariable(envName, "  http://tfs.example.local:8080/tfs/DefaultCollection  ");
        try
        {
            var entry = new TeamFoundationServerOrganisationEntry { Url = $"$ENV:{envName}" };

            Assert.AreEqual("http://tfs.example.local:8080/tfs/DefaultCollection", entry.ResolvedUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    private sealed class TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public string Url => "http://tfs.example.local:8080/tfs/DefaultCollection";
        public string Project => "Demo";
        public string ConnectorType => "TeamFoundationServer";
        public string OrganisationSlug => "defaultcollection";
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }

    private sealed class WhitespaceTargetEndpointInfo : ITargetEndpointInfo
    {
        public string Url => "  http://tfs.example.local:8080/tfs/DefaultCollection  ";
        public string Project => "Demo";
        public string ConnectorType => "TeamFoundationServer";
        public string OrganisationSlug => "defaultcollection";
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }
}

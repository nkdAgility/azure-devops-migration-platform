// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class CurrentRuntimeContextAccessorsTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentPackageConfigAccessor_SetThenClear_ExposesOnlyActiveConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "Export"
            })
            .Build();
        var accessor = new CurrentPackageConfigAccessor();

        accessor.Set(configuration);
        Assert.AreSame(configuration, accessor.Current);

        accessor.Clear();
        Assert.IsNull(accessor.Current);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentPackageConfigAccessor_SetNull_ThrowsArgumentNullException()
    {
        var accessor = new CurrentPackageConfigAccessor();

        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.Set(null!));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentAgentJobContextAccessor_SetThenClear_ExposesOnlyActiveContext()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"C:\packages\job-1",
            ConfigVersion = "2.0"
        };
        var accessor = new CurrentAgentJobContextAccessor();

        accessor.Set(context);
        Assert.AreSame(context, accessor.Current);

        accessor.Clear();
        Assert.IsNull(accessor.Current);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentAgentJobContextAccessor_SetNull_ThrowsArgumentNullException()
    {
        var accessor = new CurrentAgentJobContextAccessor();

        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.Set(null!));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentJobEndpointAccessor_ClearSource_DoesNotClearTarget()
    {
        var source = new TestSourceEndpointInfo("https://source.example", "SourceProject", "AzureDevOpsServices");
        var target = new TestTargetEndpointInfo("https://target.example", "TargetProject", "TeamFoundationServer");
        var accessor = new CurrentJobEndpointAccessor();

        accessor.SetSource(source);
        accessor.SetTarget(target);
        accessor.ClearSource();

        Assert.IsNull(accessor.Source);
        Assert.AreSame(target, accessor.Target);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentJobEndpointAccessor_ClearTarget_DoesNotClearSource()
    {
        var source = new TestSourceEndpointInfo("https://source.example", "SourceProject", "AzureDevOpsServices");
        var target = new TestTargetEndpointInfo("https://target.example", "TargetProject", "TeamFoundationServer");
        var accessor = new CurrentJobEndpointAccessor();

        accessor.SetSource(source);
        accessor.SetTarget(target);
        accessor.ClearTarget();

        Assert.AreSame(source, accessor.Source);
        Assert.IsNull(accessor.Target);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentJobEndpointAccessor_Clear_RemovesSourceAndTarget()
    {
        var accessor = new CurrentJobEndpointAccessor();
        accessor.SetSource(new TestSourceEndpointInfo("https://source.example", "SourceProject", "AzureDevOpsServices"));
        accessor.SetTarget(new TestTargetEndpointInfo("https://target.example", "TargetProject", "TeamFoundationServer"));

        accessor.Clear();

        Assert.IsNull(accessor.Source);
        Assert.IsNull(accessor.Target);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CurrentJobEndpointAccessor_SetNullEndpoint_ThrowsArgumentNullException()
    {
        var accessor = new CurrentJobEndpointAccessor();

        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.SetSource(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.SetTarget(null!));
    }

    private sealed record TestSourceEndpointInfo(string Url, string Project, string ConnectorType) : ISourceEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }

    private sealed record TestTargetEndpointInfo(string Url, string Project, string ConnectorType) : ITargetEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }
}

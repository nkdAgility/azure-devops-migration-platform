// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Identity;

[TestClass]
public sealed class CompositeIdentityAdapterTests
{
    private class StubAdapter : IIdentityAdapter
    {
        private readonly string _descriptor;
        public StubAdapter(string descriptor) => _descriptor = descriptor;

        public Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IdentityCandidate>>(new[] { new IdentityCandidate(_descriptor, upn, "X") });

        public Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IdentityCandidate>>(new[] { new IdentityCandidate(_descriptor, null, displayName) });
    }

    private sealed class SimAdapter : StubAdapter { public SimAdapter() : base("sim") { } }
    private sealed class AdoAdapter : StubAdapter { public AdoAdapter() : base("ado") { } }

    private static CompositeIdentityAdapter Build(string connectorType)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SimAdapter>();
        services.AddSingleton<AdoAdapter>();
        var sp = services.BuildServiceProvider();

        var registrations = new[]
        {
            new KeyedIdentityAdapter("Simulated", typeof(SimAdapter)),
            new KeyedIdentityAdapter("AzureDevOpsServices", typeof(AdoAdapter)),
        };

        var target = new Mock<ITargetEndpointInfo>();
        target.SetupGet(t => t.ConnectorType).Returns(connectorType);

        return new CompositeIdentityAdapter(registrations, sp, target.Object);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FindByUpnAsync_DispatchesToConnectorMatchingTargetType()
    {
        var sim = await Build("Simulated").FindByUpnAsync("u@x", "p", CancellationToken.None);
        Assert.AreEqual("sim", sim[0].Descriptor);

        var ado = await Build("AzureDevOpsServices").FindByUpnAsync("u@x", "p", CancellationToken.None);
        Assert.AreEqual("ado", ado[0].Descriptor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FindByDisplayNameAsync_DispatchesByTargetType_CaseInsensitive()
    {
        var result = await Build("simulated").FindByDisplayNameAsync("Bob", "p", CancellationToken.None);
        Assert.AreEqual("sim", result[0].Descriptor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FindByUpnAsync_UnknownConnectorType_Throws()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Build("UnknownConnector").FindByUpnAsync("u@x", "p", CancellationToken.None));
    }
}

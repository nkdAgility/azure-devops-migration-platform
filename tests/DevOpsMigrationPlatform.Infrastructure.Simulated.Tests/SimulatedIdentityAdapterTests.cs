// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests;

[TestClass]
[TestCategory("UnitTests")]
public sealed class SimulatedIdentityAdapterTests
{
    private static readonly SimulatedIdentityAdapter Adapter = new();

    [TestMethod]
    public async Task FindByUpnAsync_ExactMatch_ReturnsTargetCandidate()
    {
        var result = await Adapter.FindByUpnAsync("alice@simulated.example.com", "proj", CancellationToken.None);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("vstfs:///Target/Identity/alice", result[0].Descriptor);
        Assert.AreEqual("Alice Smith", result[0].DisplayName);
    }

    [TestMethod]
    public async Task FindByUpnAsync_CaseInsensitive()
    {
        var result = await Adapter.FindByUpnAsync("BOB@SIMULATED.EXAMPLE.COM", "proj", CancellationToken.None);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("vstfs:///Target/Identity/bob", result[0].Descriptor);
    }

    [TestMethod]
    public async Task FindByUpnAsync_NoMatch_ReturnsEmpty()
    {
        var result = await Adapter.FindByUpnAsync("nobody@nowhere.com", "proj", CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task FindByDisplayNameAsync_ExactMatch_ReturnsTargetCandidate()
    {
        var result = await Adapter.FindByDisplayNameAsync("Carol Taylor", "proj", CancellationToken.None);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("vstfs:///Target/Identity/carol", result[0].Descriptor);
    }

    [TestMethod]
    public async Task FindByDisplayNameAsync_NoMatch_ReturnsEmpty()
    {
        var result = await Adapter.FindByDisplayNameAsync("Unknown Person", "proj", CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task FindByUpnAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(0, (await Adapter.FindByUpnAsync("", "proj", CancellationToken.None)).Count);
        Assert.AreEqual(0, (await Adapter.FindByDisplayNameAsync("  ", "proj", CancellationToken.None)).Count);
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Architecture;

/// <summary>
/// Typed contract compatibility tests for ADR-0023 port promotions:
/// DI resolution of the canonical ports (CA-C1, VS-H1, VS-H2) and the inventory
/// file round-trip contract (VS-H2 required evidence).
/// </summary>
[TestClass]
public sealed class AbstractionsPortContractTests
{
    private static ServiceProvider BuildCoreAgentProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAgentServices(
            configuration,
            new Uri("http://localhost:59999"));
        return services.BuildServiceProvider();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CoreAgentServices_ResolveWorkerEventWriterPort_ToTheSingletonChannel()
    {
        using var provider = BuildCoreAgentProvider();

        var port = provider.GetRequiredService<IWorkerEventWriter>();
        var concrete = provider.GetRequiredService<UnifiedWorkerEventWriter>();

        Assert.AreSame(concrete, port,
            "IWorkerEventWriter must resolve to the single UnifiedWorkerEventWriter channel (CA-C1 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CoreAgentServices_ResolveInventoryPorts_ToASingleImplementation()
    {
        using var provider = BuildCoreAgentProvider();

        var reader = provider.GetRequiredService<IProjectInventoryReader>();
        var writer = provider.GetRequiredService<IProjectInventoryWriter>();

        Assert.AreSame((object)reader, writer,
            "Reader and writer ports must share the single inventory-file implementation (VS-H2 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CoreAgentServices_ResolveWorkItemRevisionReaderPort()
    {
        using var provider = BuildCoreAgentProvider();

        var reader = provider.GetRequiredService<IWorkItemRevisionReader>();
        Assert.IsNotNull(reader, "IWorkItemRevisionReader must be resolvable (VS-H1 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProjectInventoryPorts_RoundTripInventoryFile_PreservingOtherModulesCounts()
    {
        using var provider = BuildCoreAgentProvider();
        var reader = provider.GetRequiredService<IProjectInventoryReader>();
        var writer = provider.GetRequiredService<IProjectInventoryWriter>();

        using var package = new InMemoryPackageAccess();

        // First module writes its counts.
        await writer.MergeAsync(
            package, "fabrikam", "migration",
            orgUrl: "https://dev.azure.com/fabrikam",
            workItems: 42, revisions: 99, ct: CancellationToken.None);

        // Second module merges only its own field.
        await writer.MergeAsync(
            package, "fabrikam", "migration",
            teams: 7, ct: CancellationToken.None);

        var data = await reader.ReadAsync(package, "fabrikam", "migration", CancellationToken.None);

        Assert.AreEqual("https://dev.azure.com/fabrikam", data.OrgUrl);
        Assert.AreEqual("migration", data.Project);
        Assert.AreEqual(42, data.WorkItems, "A later merge must not clobber another module's counts.");
        Assert.AreEqual(99, data.Revisions);
        Assert.AreEqual(7, data.Teams);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProjectInventoryReader_ReturnsEmptyRecord_WhenFileMissing()
    {
        using var provider = BuildCoreAgentProvider();
        var reader = provider.GetRequiredService<IProjectInventoryReader>();

        using var package = new InMemoryPackageAccess();

        var data = await reader.ReadAsync(package, "fabrikam", "no-such-project", CancellationToken.None);

        Assert.IsNotNull(data);
        Assert.AreEqual(0, data.WorkItems);
        Assert.IsNull(data.Error);
    }
}

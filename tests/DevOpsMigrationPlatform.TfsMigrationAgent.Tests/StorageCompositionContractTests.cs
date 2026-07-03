// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.TfsMigrationAgent.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// Contract compatibility tests for ADR-0022 (MM-C1, MM-H1, CA-C2):
/// the TfsMigrationAgent host composition roots own storage-implementation
/// selection and must still resolve a working package boundary
/// (<see cref="IPackageAccess"/> and friends) after the module stopped
/// referencing Infrastructure.Storage.FileSystem.
/// </summary>
[TestClass]
public sealed class StorageCompositionContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void AddTfsMigrationAgentServices_ResolvesPackageBoundaryStorageContracts()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        // The production host (Program.cs) provides IConfiguration and logging;
        // replicate that context here.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddTfsMigrationAgentServices(configuration, new Uri("http://localhost:5100"));

        using var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<IPackageAccess>(),
            "Agent host must resolve IPackageAccess from the composition root (ADR-0022).");
        Assert.IsNotNull(provider.GetRequiredService<IPackageMigrationConfigLoader>(),
            "Agent host must resolve IPackageMigrationConfigLoader from the composition root (ADR-0022).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void MigrationPlatformHost_CreateDefaultBuilder_ResolvesPackageBoundary()
    {
        // MM-H1: the TFS subprocess host builder now lives in the TfsMigrationAgent
        // host project; it must still compose a working package boundary.
        var outputFolder = Path.Combine(Path.GetTempPath(), "dmp-adr0022-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputFolder);
        try
        {
            var settings = new MigrationPlatformHost.Settings(
                new Uri("http://localhost:8080/tfs"), "TestProject", outputFolder);

            using var host = MigrationPlatformHost
                .CreateDefaultBuilder(Array.Empty<string>(), settings)
                .Build();

            var packageAccess = host.Services.GetRequiredService<IPackageAccess>();
            Assert.IsNotNull(packageAccess,
                "TFS subprocess host must resolve IPackageAccess after host-root extraction (ADR-0022).");

            var state = host.Services.GetRequiredService<ActivePackageState>();
            Assert.AreEqual(outputFolder, state.CurrentPackageUri,
                "TFS subprocess host must pre-initialize the active package state with the output folder.");
        }
        finally
        {
            try { Directory.Delete(outputFolder, recursive: true); } catch (IOException) { }
        }
    }
}

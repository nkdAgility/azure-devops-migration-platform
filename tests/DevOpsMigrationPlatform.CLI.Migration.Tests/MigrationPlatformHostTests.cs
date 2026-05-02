// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

/// <summary>
/// Integration tests validating configuration binding, DI resolution, and
/// host builder architecture of <see cref="MigrationPlatformHost"/>.
/// </summary>
[TestClass]
public class MigrationPlatformHostTests
{
    // ─────────────────────────────────────────────────────────────────────
    // T023: Config values flow from --config through IOptions<T>
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractConfigFileArg_WhenConfigSpecified_ReturnsAbsolutePath()
    {
        var args = new[] { "--config", "my-scenario.json", "queue", "export" };
        var (configFile, remaining) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(Path.IsPathRooted(configFile));
        Assert.IsTrue(configFile.EndsWith("my-scenario.json"));
        CollectionAssert.AreEqual(new[] { "queue", "export" }, remaining);
    }

    [TestMethod]
    public void ExtractConfigFileArg_WhenShortFlag_ReturnsAbsolutePath()
    {
        var args = new[] { "-c", "test.json" };
        var (configFile, _) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(configFile.EndsWith("test.json"));
    }

    [TestMethod]
    public void ExtractConfigFileArg_WhenNoConfig_DefaultsToMigrationJson()
    {
        var args = new[] { "queue", "export" };
        var (configFile, remaining) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(configFile.EndsWith("migration.json"));
        CollectionAssert.AreEqual(new[] { "queue", "export" }, remaining);
    }

    [TestMethod]
    public void ExtractConfigFileArg_WhenAbsolutePath_PreservesIt()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "abs-scenario.json");
        var args = new[] { "--config", absolute };
        var (configFile, _) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.AreEqual(absolute, configFile);
    }

    // ─────────────────────────────────────────────────────────────────────
    // T034: DI container service registration and resolution
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CreateDefaultBuilder_RegistersEnvironmentOptions()
    {
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>())
            .Build();

        var options = host.Services.GetService<IOptions<EnvironmentOptions>>();
        Assert.IsNotNull(options);
        Assert.IsNotNull(options.Value);
    }

    [TestMethod]
    public void CreateDefaultBuilder_RegistersAnsiConsole()
    {
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>())
            .Build();

        var console = host.Services.GetService<IAnsiConsole>();
        Assert.IsNotNull(console);
    }

    [TestMethod]
    public void CreateDefaultBuilder_InvokesConfigureServicesDelegate()
    {
        bool delegateCalled = false;

        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>(), (services, config) =>
            {
                delegateCalled = true;
                services.AddSingleton<string>("test-marker");
            })
            .Build();

        Assert.IsTrue(delegateCalled);
        Assert.AreEqual("test-marker", host.Services.GetService<string>());
    }

    [TestMethod]
    public void CreateDefaultBuilder_ConfigureServicesDelegateReceivesConfiguration()
    {
        IConfiguration? capturedConfig = null;

        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>(), (services, config) =>
            {
                capturedConfig = config;
            })
            .Build();

        Assert.IsNotNull(capturedConfig);
    }

    // ─────────────────────────────────────────────────────────────────────
    // T033: Adding a new command does not require modifying host setup
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CreateDefaultBuilder_SupportsArbitraryServiceRegistration_WithoutHostChanges()
    {
        // Simulate a new command registering its own services.
        // This proves the architecture: host provides shared infra,
        // each command adds its own services via the delegate.
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>(), (services, config) =>
            {
                services.AddSingleton<INewCommandService, NewCommandServiceImpl>();
            })
            .Build();

        var service = host.Services.GetService<INewCommandService>();
        Assert.IsNotNull(service);
    }

    // Test interface to prove extensibility without host modification
    private interface INewCommandService { }
    private class NewCommandServiceImpl : INewCommandService { }
}

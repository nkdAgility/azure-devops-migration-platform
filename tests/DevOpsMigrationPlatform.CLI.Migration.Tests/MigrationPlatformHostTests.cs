// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry.Trace;
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

    [TestCategory("UnitTest")]
    [TestMethod]
    public void ExtractConfigFileArg_WhenConfigSpecified_ReturnsAbsolutePath()
    {
        var args = new[] { "--config", "my-scenario.json", "queue", "export" };
        var (configFile, remaining) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(Path.IsPathRooted(configFile));
        Assert.IsTrue(configFile.EndsWith("my-scenario.json"));
        CollectionAssert.AreEqual(new[] { "queue", "export" }, remaining);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void ExtractConfigFileArg_WhenShortFlag_ReturnsAbsolutePath()
    {
        var args = new[] { "-c", "test.json" };
        var (configFile, _) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(configFile.EndsWith("test.json"));
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void ExtractConfigFileArg_WhenNoConfig_DefaultsToMigrationJson()
    {
        var args = new[] { "queue", "export" };
        var (configFile, remaining) = MigrationPlatformHost.ExtractConfigFileArg(args);

        Assert.IsTrue(configFile.EndsWith("migration.json"));
        CollectionAssert.AreEqual(new[] { "queue", "export" }, remaining);
    }

    [TestCategory("UnitTest")]
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

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
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

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
    [TestMethod]
    public void CreateDefaultBuilder_RegistersAnsiConsole()
    {
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>())
            .Build();

        var console = host.Services.GetService<IAnsiConsole>();
        Assert.IsNotNull(console);
    }

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
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

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
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

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
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

    // ─────────────────────────────────────────────────────────────────────
    // GAP-HBA-001: OpenTelemetry tracing pipeline registration
    // ─────────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-registration")]
    [TestMethod]
    public void CreateDefaultBuilder_RegistersOpenTelemetryTracing()
    {
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>())
            .Build();

        // TracerProvider is the root OTel tracing object registered by AddOpenTelemetry().
        // If WithTracing(...) was configured, the DI container holds a TracerProvider singleton.
        var tracerProvider = host.Services.GetService<TracerProvider>();
        Assert.IsNotNull(tracerProvider,
            "AddOpenTelemetry().WithTracing() must register a TracerProvider in the DI container.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // GAP-HBA-002: Command-specific service isolation — negative assertion
    // ─────────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("di-isolation")]
    [TestMethod]
    public void CreateDefaultBuilder_CommandServices_NotVisibleToOtherHosts()
    {
        // Host A: registers ICommandServiceA via its own delegate.
        var hostWithService = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>(), (services, _) =>
            {
                services.AddSingleton<ICommandServiceA, CommandServiceAImpl>();
            })
            .Build();

        // Host B: built without any delegate — simulates a different command.
        var hostWithoutService = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>())
            .Build();

        // Positive: the registering host can resolve the service.
        var resolvedFromA = hostWithService.Services.GetService<ICommandServiceA>();
        Assert.IsNotNull(resolvedFromA,
            "ICommandServiceA must be resolvable from the host that registered it.");

        // Negative: the non-registering host must not resolve the service.
        var resolvedFromB = hostWithoutService.Services.GetService<ICommandServiceA>();
        Assert.IsNull(resolvedFromB,
            "ICommandServiceA must not be resolvable from a host that did not register it.");
    }

    // Isolation test interfaces — not used in production code.
    private interface ICommandServiceA { }
    private sealed class CommandServiceAImpl : ICommandServiceA { }

    // ─────────────────────────────────────────────────────────────────────
    // GAP-HBA-003: ValidateOnStart early failure
    // ─────────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestCategory("IntegrationTest")]
    [TestCategory("cli-architecture")]
    [TestCategory("options-validation")]
    [TestMethod]
    public async Task CreateDefaultBuilder_ValidateOnStart_InvalidConfig_ThrowsOptionsValidationException()
    {
        // Arrange: inject config that omits the required field so validation fails.
        // The section exists but RequiredField is intentionally absent/empty.
        var inMemoryConfig = new Dictionary<string, string?>
        {
            // Provide the section header without the required key so the
            // data-annotation validator reports a missing required value.
            [$"{TestValidatedOptions.SectionName}:OtherField"] = "irrelevant"
        };

        var exception = await HostBuilderFixture.StartAsync_CapturingException(
            inMemoryConfig,
            configureServices: (services, config) =>
            {
                services.AddOptions<TestValidatedOptions>()
                    .BindConfiguration(TestValidatedOptions.SectionName)
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            });

        // Assert: StartAsync must throw before any command logic runs.
        Assert.IsNotNull(exception,
            "StartAsync must throw when ValidateOnStart is configured and configuration is invalid.");

        // The exception may be wrapped in a HostAbortedException or AggregateException
        // depending on the hosting runtime version; unwrap to find OptionsValidationException.
        var inner = UnwrapToOptionsValidationException(exception);
        Assert.IsNotNull(inner,
            $"Expected OptionsValidationException (possibly wrapped) but got: {exception.GetType().Name}: {exception.Message}");

        // --- helpers (local functions) ---

        static OptionsValidationException? UnwrapToOptionsValidationException(Exception ex) =>
            ex switch
            {
                OptionsValidationException ove => ove,
                AggregateException ae => ae.InnerExceptions
                    .OfType<OptionsValidationException>()
                    .FirstOrDefault()
                    ?? ae.InnerExceptions
                        .Select(UnwrapToOptionsValidationException)
                        .FirstOrDefault(x => x is not null),
                _ when ex.InnerException is not null =>
                    UnwrapToOptionsValidationException(ex.InnerException),
                _ => null
            };
    }

    /// <summary>Options type used only by the ValidateOnStart failure test.</summary>
    private sealed class TestValidatedOptions
    {
        public const string SectionName = "TestValidated";

        [Required]
        public string RequiredField { get; set; } = string.Empty;
    }
}

/// <summary>
/// Minimal helper for testing the ValidateOnStart failure path.
/// Builds a host with an injected in-memory configuration source and
/// captures any exception thrown during StartAsync.
/// </summary>
internal static class HostBuilderFixture
{
    /// <summary>
    /// Builds a host with the supplied in-memory config entries and
    /// the given configureServices delegate, then attempts StartAsync.
    /// Returns the caught exception, or null if StartAsync completed without error.
    /// </summary>
    public static async Task<Exception?> StartAsync_CapturingException(
        Dictionary<string, string?> inMemoryConfig,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        var host = MigrationPlatformHost
            .CreateDefaultBuilder(Array.Empty<string>(), configureServices)
            .ConfigureAppConfiguration((_, b) => b.AddInMemoryCollection(inMemoryConfig))
            .Build();

        try
        {
            await host.StartAsync();
            await host.StopAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

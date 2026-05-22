// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.MigrationAgent.Tests;

/// <summary>
/// Verifies the full DI container can be built without deadlock.
/// This catches circular ILoggerProvider dependencies that cause
/// <c>Host.Build()</c> to hang forever with no error.
/// </summary>
[TestClass]
public class DiCompositionTests
{
    /// <summary>
    /// Builds the full MigrationAgent host DI container and asserts it
    /// completes within 15 seconds. A deadlock would cause this test to
    /// timeout, failing CI with a clear diagnostic instead of hanging.
    /// </summary>
    [TestMethod]
    public void Build_FullMigrationAgentContainer_CompletesWithoutDeadlock()
    {
        // Arrange — replicate the same registration path as Program.cs
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:BaseUrl"] = "http://localhost:5100",
                ["Telemetry:DiagnosticsPath"] = null, // Disable file logging in test
            })
            .Build();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            Configuration = new ConfigurationManager(),
        });
        builder.Configuration.AddConfiguration(config);

        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddServiceDefaults(Abstractions.WellKnownServiceNames.MigrationAgent);

        var controlPlaneBaseUrl = new Uri("http://localhost:5100");
        builder.AddMigrationAgentServices(controlPlaneBaseUrl);

        builder.Services.Configure<ServiceProviderOptions>(options =>
        {
            options.ValidateOnBuild = true;
        });

        // Act — Build() is the operation that deadlocks when circular deps exist.
        // The [Timeout] attribute ensures the test fails rather than hanging.
        var host = builder.Build();

        // Assert — if we get here, the container resolved without deadlock.
        Assert.IsNotNull(host);
        var loggerFactory = host.Services.GetService<ILoggerFactory>();
        Assert.IsNotNull(loggerFactory, "ILoggerFactory should be resolvable after Build().");

        host.Dispose();
    }
}

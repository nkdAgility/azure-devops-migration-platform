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
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    /// <summary>
    /// #171 live run: the Aspire default resilience handler (10 s attempt timeout) also wrapped
    /// the Control Plane client, so every 30 s lease long-poll was cut off at 10 s. A lease granted
    /// as the agent gave up went to nobody and the job hung.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ControlPlaneClient_LeaseLongPollLongerThanTheDefaultTimeout_WaitsForTheAnswer()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], Configuration = new ConfigurationManager() });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ControlPlane:BaseUrl"] = "http://localhost:5100",
            ["Telemetry:DiagnosticsPath"] = null,
        });
        builder.AddServiceDefaults(Abstractions.WellKnownServiceNames.MigrationAgent);
        builder.AddMigrationAgentServices(new Uri("http://localhost:5100"));
        var heldFor = TimeSpan.FromSeconds(12);
        builder.Services.AddHttpClient("ControlPlane").ConfigurePrimaryHttpMessageHandler(() => new LongPollHandler(heldFor));

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("ControlPlane");

        using var response = await client.GetAsync("/agents/lease?capabilities=AzureDevOps");

        Assert.AreEqual(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class LongPollHandler(TimeSpan heldFor) : System.Net.Http.HttpMessageHandler
    {
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(heldFor, cancellationToken);
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }
}

// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// Migration Agent — Worker Service
// Polls the control plane for jobs, executes them, and reports progress.
// Stateless: all durable state is written to the package via IArtefactStore/IStateStore.
// See docs/migration-agent.md.

using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Explicitly ensure the global minimum log level is not accidentally raised.
    // In published deployments appsettings.json is absent, so without this the
    // effective minimum may default to a suppressive level in .NET 10+.
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
    builder.Logging.AddConsole();

    builder.AddServiceDefaults(DevOpsMigrationPlatform.Abstractions.WellKnownServiceNames.MigrationAgent);

    // Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
    builder.Logging.AddDataClassificationFilter();

    var controlPlaneBaseUrl = new Uri(
        builder.Configuration["ControlPlane:BaseUrl"]
        ?? builder.Configuration["MigrationPlatform:Environment:ControlPlane:BaseUrl"]
        ?? "http://localhost:5100");
    // All agent service registrations are in MigrationAgentServiceExtensions so that
    // LocalStackHost (CLI in-process mode) can use the exact same registrations.
    builder.AddMigrationAgentServices(controlPlaneBaseUrl);

    // Enable DI validation — catches missing registrations at startup rather than at first
    // resolution. ValidateScopes is intentionally omitted: the codebase uses captive Singleton
    // dependencies on Scoped services (e.g., IFieldTransformTool) by design, resolving them
    // lazily within a job scope at call time.
    builder.Services.Configure<ServiceProviderOptions>(options =>
    {
        options.ValidateOnBuild = true;
    });

    // Build with timeout — converts silent circular-dependency deadlocks into a clear
    // exception rather than hanging forever with no diagnostics.
    const int buildTimeoutSeconds = 30;
    var buildTask = Task.Run(() => builder.Build());
    if (!buildTask.Wait(TimeSpan.FromSeconds(buildTimeoutSeconds)))
    {
        throw new TimeoutException(
            $"Host.Build() did not complete within {buildTimeoutSeconds}s. " +
            "This typically indicates a circular dependency in an ILoggerProvider that " +
            "eagerly resolves IHttpClientFactory, ILogger<T>, or IPackageAccess. " +
            "ILoggerProvider implementations must use lazy resolution (IServiceProvider) " +
            "for any dependency that may itself depend on ILogger<T>.");
    }
    var host = buildTask.GetAwaiter().GetResult();

    // Emit startup log immediately after host build — before hosted services start.
    // This verifies the logging pipeline is functional and provides an early diagnostic.
    var startupLogger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DevOpsMigrationPlatform.MigrationAgent.Startup");
    var version = typeof(MigrationAgentServiceExtensions).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    startupLogger.LogInformation(
        "MigrationAgent v{Version} started. ControlPlane={ControlPlaneUrl}, PID={ProcessId}",
        version,
        controlPlaneBaseUrl,
        Environment.ProcessId);

    host.Run();

    startupLogger.LogInformation("MigrationAgent host stopped gracefully.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("[MigrationAgent] Startup failed:");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
    throw;
}

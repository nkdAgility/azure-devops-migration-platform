using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Hosts the ControlPlane API and MigrationAgent worker in-process within the CLI when
/// the environment type is <c>Standalone</c> (the default).
///
/// This allows a single <c>devopsmigration export --config ...</c> command to start the
/// full local stack transparently — ControlPlane API on <c>http://localhost:{port}</c>,
/// MigrationAgent polling that API — with no Docker, no Aspire, and no external
/// processes required. The port defaults to 5100 but is configurable via <c>--port</c>.
///
/// The same service classes used by <c>ControlPlaneHost</c> and <c>MigrationAgent</c>
/// are loaded here, so no migration logic is duplicated. When PostgreSQL is added to
/// <c>ControlPlaneServiceExtensions</c>, the CLI will pick it up automatically.
///
/// See docs/cli.md — "Control Plane Endpoint".
/// </summary>
public sealed class LocalStackHost : IAsyncDisposable
{
    private readonly Uri _controlPlaneUrl;

    private WebApplication? _controlPlane;
    private IHost? _agent;

    /// <summary>
    /// Creates a new <see cref="LocalStackHost"/> that binds the control plane to the given port.
    /// </summary>
    /// <param name="port">TCP port for the in-process control plane API. Default: 5100.</param>
    public LocalStackHost(int port = 5100)
    {
        _controlPlaneUrl = new Uri($"http://localhost:{port}");
    }

    /// <summary>
    /// Starts the ControlPlane ASP.NET Core API on the configured port,
    /// waits for it to be healthy, then starts the MigrationAgent worker.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StartControlPlaneAsync(cancellationToken);
        await WaitForHealthyAsync(cancellationToken);
        await StartAgentAsync(cancellationToken);
    }

    private async Task StartControlPlaneAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        // Load the CLI's appsettings.json so Telemetry:AzureMonitorConnectionString
        // is available to ServiceDefaults (UseAzureMonitor) and other shared config.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults(WellKnownServiceNames.ControlPlaneHost);

        // Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
        builder.Logging.AddDataClassificationFilter();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Register polymorphic serializers so MigrationEndpointOptions can be deserialized.
        builder.Services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        builder.Services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
        builder.Services.AddMigrationPlatformPolymorphicSerializers();

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ControlPlaneServiceExtensions).Assembly)
            .AddJsonOptions(opts =>
            {
                // Explicitly wire DefaultJsonTypeInfoResolver so that [JsonPolymorphic] /
                // [JsonDerivedType] attributes on Job are processed during [FromBody]
                // deserialization (required for abstract base-type binding in ASP.NET Core).
                opts.JsonSerializerOptions.TypeInfoResolver =
                    new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
                opts.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });

        builder.Services.AddControlPlaneServices(builder.Configuration);

        // Post-configure ASP.NET JSON options to include the polymorphic endpoint converter.
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(sp =>
        {
            var converter = sp.GetRequiredService<DevOpsMigrationPlatform.Infrastructure.Serialization.PolymorphicEndpointOptionsConverter>();
            return new Microsoft.Extensions.Options.PostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>(
                string.Empty, opts => opts.JsonSerializerOptions.Converters.Add(converter));
        });

        builder.WebHost.UseUrls(_controlPlaneUrl.ToString().TrimEnd('/'));

        _controlPlane = builder.Build();

        // Stamp every request as authenticated so the auth check in
        // ProgressController.GetLogs (403 for unauthenticated callers) passes.
        // LocalStackHost is single-user / local-only — no real auth is needed.
        _controlPlane.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var identity = new System.Security.Claims.ClaimsIdentity("LocalStack");
                identity.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, "local-cli-user"));
                context.User = new System.Security.Claims.ClaimsPrincipal(identity);
            }
            await next();
        });

        _controlPlane.MapControllers();

        await _controlPlane.StartAsync(cancellationToken);
    }

    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = _controlPlaneUrl };
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await http.GetAsync("/jobs", cancellationToken);
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not ready yet — keep polling
            }
            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException(
            $"ControlPlane API at {_controlPlaneUrl} did not become ready within 10 seconds.");
    }

    private async Task StartAgentAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        // Load the CLI's appsettings.json so Telemetry:AzureMonitorConnectionString
        // is available to ServiceDefaults (UseAzureMonitor) and other shared config.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults(WellKnownServiceNames.MigrationAgent);

        // Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
        builder.Logging.AddDataClassificationFilter();

        // The CLI handles all user-facing console output via Spectre.Console and the
        // SSE progress stream. Suppress the default console logger entirely so internal
        // agent logs (Polly, JobAgentWorker, module diagnostics) do not leak to stdout.
        // Diagnostic logs still flow to PackageLoggerProvider (Logs/agent.jsonl) and
        // ControlPlaneLoggerProvider (diagnostics endpoint).
        builder.Logging.AddFilter<ConsoleLoggerProvider>(_ => false);

        builder.AddMigrationAgentServices(_controlPlaneUrl);

        _agent = builder.Build();
        await _agent.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Use a bounded timeout so Ctrl+C / ProcessExit never hangs.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            if (_agent is not null)
            {
                await _agent.StopAsync(timeoutCts.Token);
                _agent.Dispose();
                _agent = null;
            }

            if (_controlPlane is not null)
            {
                await _controlPlane.StopAsync(timeoutCts.Token);
                await _controlPlane.DisposeAsync();
                _controlPlane = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout exceeded — forcibly dispose whatever we can.
            _agent?.Dispose();
            _agent = null;
            if (_controlPlane is not null)
            {
                await _controlPlane.DisposeAsync();
                _controlPlane = null;
            }
        }
    }
}

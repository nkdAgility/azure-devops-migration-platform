using DevOpsMigrationPlatform.ControlPlane.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Hosts the ControlPlane API and MigrationAgent worker in-process within the CLI when
/// the environment type is <c>Standalone</c> (the default).
///
/// This allows a single <c>devopsmigration export --config ...</c> command to start the
/// full local stack transparently — ControlPlane API on <c>http://localhost:5100</c>,
/// MigrationAgent polling that API — with no Docker, no Aspire, and no external
/// processes required.
///
/// The same service classes used by <c>ControlPlaneHost</c> and <c>MigrationAgent</c>
/// are loaded here, so no migration logic is duplicated. When PostgreSQL is added to
/// <c>ControlPlaneServiceExtensions</c>, the CLI will pick it up automatically.
///
/// See docs/cli.md — "Control Plane Endpoint".
/// </summary>
public sealed class LocalStackHost : IAsyncDisposable
{
    private static readonly Uri LocalControlPlaneUrl = new("http://localhost:5100");

    private WebApplication? _controlPlane;
    private IHost? _agent;

    /// <summary>
    /// Starts the ControlPlane ASP.NET Core API on <c>http://localhost:5100</c>,
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

        builder.WebHost.UseUrls(LocalControlPlaneUrl.ToString().TrimEnd('/'));

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
        using var http = new HttpClient { BaseAddress = LocalControlPlaneUrl };
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
            "ControlPlane API at http://localhost:5100 did not become ready within 10 seconds.");
    }

    private async Task StartAgentAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.AddMigrationAgentServices(LocalControlPlaneUrl);

        _agent = builder.Build();
        await _agent.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop agent first so it finishes its current poll cycle cleanly
        if (_agent is not null)
        {
            await _agent.StopAsync(TimeSpan.FromSeconds(5));
            _agent.Dispose();
            _agent = null;
        }

        if (_controlPlane is not null)
        {
            await _controlPlane.StopAsync(TimeSpan.FromSeconds(5));
            await _controlPlane.DisposeAsync();
            _controlPlane = null;
        }
    }
}

// ControlPlaneHost — ASP.NET Core deployable host
//
// Hosts the ControlPlane service library and manages Migration Agent lifecycle.
// This is the SAME binary for ALL connected deployment modes:
//
//   Mode 1 — Local / Self-Host (Aspire-managed)
//     The AppHost starts this process on localhost:5100 alongside a local
//     PostgreSQL binary and the MigrationAgent(s). Aspire handles process
//     spawning; ControlPlaneHost monitors liveness via AgentLifecycleService.
//
//   Mode 2 — Cloud (Azure Container Apps via azd up)
//     This image is deployed to Azure Container Apps. The CLI connects via HTTPS.
//     Azure Container Apps auto-scaling handles MigrationAgent container lifecycle.
//
// See docs/control-plane.md, docs/aspire-integration.md.

using System.Security.Claims;
using DevOpsMigrationPlatform.ControlPlane.Services;
using DevOpsMigrationPlatform.ControlPlaneHost.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

using DevOpsMigrationPlatform.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
builder.Logging.AddDataClassificationFilter();

// Register polymorphic serializers so the endpoint type registry is available.
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

// Post-configure ASP.NET JSON options to include the polymorphic endpoint converter
// (needs the DI-resolved EndpointOptionsTypeRegistry, so cannot be done in AddJsonOptions).
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(sp =>
{
    var converter = sp.GetRequiredService<DevOpsMigrationPlatform.Infrastructure.Serialization.PolymorphicEndpointOptionsConverter>();
    return new Microsoft.Extensions.Options.PostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>(
        string.Empty, opts => opts.JsonSerializerOptions.Converters.Add(converter));
});

// Agent lifecycle management: monitors running agents and will manage
// spawning/restart in future phases. Currently a stub that establishes the pattern.
builder.Services.AddHostedService<AgentLifecycleService>();

var app = builder.Build();

// In Development mode (used by test runners via ControlPlaneHostRunner),
// stamp every request as authenticated so auth-gated endpoints (e.g.
// ProgressController.GetProgress) are accessible without a real bearer token.
// This mirrors the identical bypass in LocalStackHost for in-process mode.
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var identity = new ClaimsIdentity("Development");
            identity.AddClaim(new Claim(ClaimTypes.Name, "dev-user"));
            context.User = new ClaimsPrincipal(identity);
        }
        await next(context);
    });
}

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();

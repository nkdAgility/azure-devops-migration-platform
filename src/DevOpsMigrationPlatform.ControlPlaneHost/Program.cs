// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// ControlPlaneHost — ASP.NET Core deployable host
//
// Hosts the ControlPlane service library and manages Migration Agent lifecycle.
// This is the SAME binary for ALL connected deployment modes:
//
//   Mode 1 — Local / Self-Host (CLI-managed)
//     The CLI uses LocalStackHost to start this process on localhost:5100 alongside
//     the MigrationAgent. LocalStackHost manages process lifecycle directly via
//     ChildProcessHost (plain System.Diagnostics.Process — no Aspire at runtime).
//
//   Mode 2 — Developer standalone (AppHost / Aspire dashboard)
//     Run 'dotnet run --project AppHost' to start the full stack with the Aspire
//     dashboard. AppHost also configures PostgreSQL as a portable binary resource.
//     This mode is for development only; the CLI does NOT drive AppHost at runtime.
//
//   Mode 3 — Cloud (Azure Container Apps via azd up)
//     This image is deployed to Azure Container Apps. The CLI connects via HTTPS.
//     Azure Container Apps auto-scaling handles MigrationAgent container lifecycle.
//
// See docs/control-plane.md, docs/aspire-integration.md.

using System.Security.Claims;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using DevOpsMigrationPlatform.ControlPlaneHost.AgentLifecycle;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.Infrastructure.ControlPlane.Metrics;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(DevOpsMigrationPlatform.Abstractions.WellKnownServiceNames.ControlPlaneHost);

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
        opts.JsonSerializerOptions.UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    });

builder.Services.AddControlPlaneTelemetryServices(builder.Configuration);
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

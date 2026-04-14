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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers()
    .AddApplicationPart(typeof(ControlPlaneServiceExtensions).Assembly);

builder.Services.AddControlPlaneServices(builder.Configuration);

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

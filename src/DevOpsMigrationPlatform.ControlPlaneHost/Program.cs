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

app.MapDefaultEndpoints();
app.MapControllers();

// TODO: Implement job lifecycle endpoints (docs/control-plane.md):
//   POST   /jobs                              — submit MigrationJob
//   GET    /jobs  /jobs/{jobId}               — list / get job
//   GET    /jobs/{jobId}/progress             — per-module progress
//   POST   /jobs/{jobId}/cancel|pause|resume  — lifecycle signals
//   GET    /agents/lease                      — agent polls for work
//   POST   /agents/lease/{id}/heartbeat       — agent keepalive
//   POST   /agents/lease/{id}/progress        — agent reports cursor
//   POST   /agents/lease/{id}/complete|fail   — agent signals terminal state

app.Run();

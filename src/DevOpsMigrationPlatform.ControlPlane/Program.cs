// Control Plane — ASP.NET Core Web API
//
// This is the SAME binary for ALL connected deployment modes.
// Deployment mode is determined entirely by configuration and hosting environment:
//
//   Mode 1 — Local Aspire (Standalone on developer laptop)
//     The Aspire AppHost (DevOpsMigrationPlatform.AppHost) starts this process
//     on localhost:5100 alongside a local PostgreSQL binary and the Migration Agent.
//     The CLI's ControlPlaneClient points at http://localhost:5100.
//     Package storage is file:/// on the local machine.
//
//   Mode 2 — Self-Hosted / Managed (Azure Container Apps)
//     'azd up' deploys this image to Azure Container Apps.
//     The CLI's ControlPlaneClient points at the ACA HTTPS endpoint.
//     Package storage is azureblob://.
//
// There is NO "embedded" mode. When the CLI runs without any control plane
// (pure Standalone, no Aspire), it uses LocalJobRunner which bypasses this
// service entirely and runs the Job Engine in-process.
//
// See docs/control-plane.md, docs/cli.md, docs/aspire-integration.md.

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();

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

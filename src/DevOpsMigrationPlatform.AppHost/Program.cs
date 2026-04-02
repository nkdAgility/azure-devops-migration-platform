// Aspire AppHost — orchestrates the full stack locally.
// Profiles:
//   portable — bundled PostgreSQL binary + filesystem package store (validates Standalone mode)
//   docker   — Docker PostgreSQL + Azurite (validates Self-Hosted / Managed mode)
// See docs/aspire-integration.md.

var builder = DistributedApplication.CreateBuilder(args);

// TODO: Wire up resources once ControlPlane + MigrationAgent are fully implemented:
//
// var postgres = builder.AddPostgres("postgres");
// var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlane>("control-plane")
//     .WithReference(postgres);
// var agent = builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
//     .WithReference(controlPlane);

builder.Build().Run();

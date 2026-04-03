// AppHost — Aspire orchestration entry point
//
// PURPOSE: This project has two uses only:
//   1. 'azd up' — provisions Azure resources (Container Apps, PostgreSQL, Blob Storage)
//   2. Developer standalone — 'dotnet run --project AppHost' starts the full stack
//      with the Aspire dashboard for development without going through the CLI.
//
// The CLI does NOT drive this project at runtime. CLI commands manage their own
// hosting lifecycle directly. See docs/aspire-integration.md.
//
// Two subprofiles, selected via DEVOPS_MIGRATION_INFRA env var:
//
//   portable (default) — no Docker required
//     PostgreSQL: AddPostgres (local volume)
//     Package storage: filesystem
//
//   docker — Docker required
//     PostgreSQL: Azure PostgreSQL Flexible Server wire protocol (RunAsContainer)
//     Package storage: Azurite (same BlobContainerClient as production)
//
// See docs/aspire-integration.md.

var builder = DistributedApplication.CreateBuilder(args);

var infra = builder.Configuration["DEVOPS_MIGRATION_INFRA"] ?? "portable";

if (infra == "docker")
{
    // Docker subprofile — validates cloud topology.
    // PostgreSQL in a container: identical wire protocol to Azure PostgreSQL Flexible Server.
    var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
        .RunAsContainer()
        .AddDatabase("controlplane-db");

    // Azurite: same Azure SDK BlobContainerClient used in production runs unmodified.
    var storage = builder.AddAzureStorage("storage")
        .RunAsEmulator()
        .AddBlobs("packages");

    var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlaneHost>("controlplane")
        .WithReference(postgres)
        .WithReference(storage)
        .WithEnvironment("PackageStore__Type", "azureblob")
        .WithHttpEndpoint(port: 5100, name: "http");

    builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
        .WithReference(controlPlane)
        .WithReference(storage)
        .WithEnvironment("PackageStore__Type", "azureblob");
}
else
{
    // Portable subprofile (default) — no Docker; validates local/self-host topology.
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume()
        .AddDatabase("controlplane-db");

    var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlaneHost>("controlplane")
        .WithReference(postgres)
        .WithEnvironment("PackageStore__Type", "filesystem")
        .WithHttpEndpoint(port: 5100, name: "http");

    builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
        .WithReference(controlPlane)
        .WithEnvironment("PackageStore__Type", "filesystem");
}

builder.Build().Run();

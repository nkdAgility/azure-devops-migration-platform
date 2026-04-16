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
// Installed mode (Start from build.ps1):
//   When MIGRATION_INSTALL_PATH is set, Aspire launches the ControlPlane and
//   MigrationAgent executables from that directory instead of from the project
//   build output. This validates the installed package end-to-end.
//
// See docs/aspire-integration.md.

using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

var infra = builder.Configuration["DEVOPS_MIGRATION_INFRA"] ?? "portable";
var installPath = builder.Configuration["MIGRATION_INSTALL_PATH"];
var useInstalled = !string.IsNullOrEmpty(installPath);

// Helper: resolve the executable path for an installed component.
static string InstalledExe(string installRoot, string subfolder, string assemblyName)
{
    var path = Path.Combine(installRoot, subfolder, assemblyName);
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? path + ".exe" : path;
}

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

    if (useInstalled)
    {
        // Installed mode: launch ControlPlane and MigrationAgent from the extracted package.
        // MIGRATION_INSTALL_PATH points to the versioned install dir (e.g. current/).
        // Structure: root = CLI, ControlPlane/ = control plane host, MigrationAgent/ = agent.
        // ExecutableResource does not implement IResourceWithConnectionString, so we
        // wire up Postgres via WithEnvironment and the agent's control-plane URL explicitly.
        var cpExe = InstalledExe(installPath!, "ControlPlane", "DevOpsMigrationPlatform.ControlPlaneHost");
        var controlPlane = builder.AddExecutable("controlplane", cpExe, Path.Combine(installPath!, "ControlPlane"))
            .WithEnvironment(ctx =>
            {
                ctx.EnvironmentVariables["ConnectionStrings__controlplane-db"] =
                    postgres.Resource.ConnectionStringExpression;
                ctx.EnvironmentVariables["PackageStore__Type"] = "filesystem";
            })
            .WithHttpEndpoint(port: 5100, name: "http");

        var agentExe = InstalledExe(installPath!, "MigrationAgent", "DevOpsMigrationPlatform.MigrationAgent");
        builder.AddExecutable("migration-agent", agentExe, Path.Combine(installPath!, "MigrationAgent"))
            .WithEnvironment("MigrationPlatform__Environment__ControlPlane__BaseUrl", "http://localhost:5100")
            .WithEnvironment("PackageStore__Type", "filesystem");
    }
    else
    {
        var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlaneHost>("controlplane")
            .WithReference(postgres)
            .WithEnvironment("PackageStore__Type", "filesystem")
            .WithHttpEndpoint(port: 5100, name: "http");

        builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
            .WithReference(controlPlane)
            .WithEnvironment("PackageStore__Type", "filesystem");
    }
}

builder.Build().Run();

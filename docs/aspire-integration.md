# Microsoft Aspire Integration

## Purpose

Microsoft Aspire provides cloud-ready orchestration for the Azure DevOps Migration Platform, enabling seamless transitions between local development and cloud deployment. The TUI always runs locally, but the control plane and migration agents can run either locally (orchestrated by Aspire) or in the cloud (Azure Container Apps).

---

## Architecture Fit

The migration platform's execution model aligns perfectly with Aspire's design:

| Component | Always Local | Can Run Local | Can Run Cloud | Aspire Role |
|---|---|---|---|---|
| **TUI** (`CLI.Migration`, net10.0) | ✓ | ✓ | ✗ | Standalone CLI (not orchestrated) |
| **TFS Migration CLI** (`CLI.TfsMigration`, net481) | ✓ | ✓ | ✗ | Standalone CLI or subprocess of TUI (not orchestrated) |
| **Control Plane API** | ✗ | ✓ | ✓ | Aspire container resource |
| **Migration Agent(s)** | ✗ | ✓ | ✓ | Aspire container resource |
| **Package Storage** | ✗ | ✓ (filesystem) | ✓ (Azure Blob) | Aspire-configured connection string |

---

## Aspire Projects Structure

```
src/
  DevOpsMigrationPlatform.AppHost/               ← Aspire AppHost (orchestrator)
    Program.cs                                    ← defines resources and service discovery
    appsettings.json                              ← local configuration overrides
    DevOpsMigrationPlatform.AppHost.csproj

  DevOpsMigrationPlatform.ServiceDefaults/       ← Shared observability/resilience
    Extensions.cs                                 ← AddServiceDefaults() extension
    DevOpsMigrationPlatform.ServiceDefaults.csproj

  DevOpsMigrationPlatform.ControlPlane/          ← ASP.NET Core Web API
    Program.cs                                    ← control plane endpoints
    Controllers/                                  ← job, lease, progress APIs
    DevOpsMigrationPlatform.ControlPlane.csproj

  DevOpsMigrationPlatform.MigrationAgent/        ← Worker Service
    Program.cs                                    ← agent worker host
    Worker.cs                                     ← lease polling and execution
    DevOpsMigrationPlatform.MigrationAgent.csproj

  DevOpsMigrationPlatform.CLI.Migration/         ← Main TUI (net10.0, not orchestrated by Aspire)
    Program.cs                                    ← local CLI entry point; spawns CLI.TfsMigration as subprocess
    ExternalToolRunner.cs                         ← spawns net481 subprocess, streams stdout/stderr
    DevOpsMigrationPlatform.CLI.Migration.csproj  ← TargetFramework: net10.0

  DevOpsMigrationPlatform.CLI.TfsMigration/      ← TFS exporter CLI (net481, not orchestrated by Aspire)
    Program.cs                                    ← standalone CLI entry point; also callable as subprocess
    DevOpsMigrationPlatform.CLI.TfsMigration.csproj  ← TargetFramework: net481
```

---

## AppHost Configuration

The Aspire AppHost (`DevOpsMigrationPlatform.AppHost`) has two configurations depending on mode.

### Standalone AppHost (no external dependencies)

Standalone mode is fully self-contained. PostgreSQL ships as a bundled portable binary launched as a child process — no Docker, no installer. Package storage uses the local filesystem.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Portable PostgreSQL — bundled binary, spawned as a child process.
// No Docker or external install required.
var postgres = builder.AddPortablePostgres("postgres")
    .AddDatabase("controlplane-db");

// Control Plane API
var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlane>("controlplane")
    .WithReference(postgres)
    .WithEnvironment("PackageStore__Type", "filesystem")
    .WithHttpEndpoint(port: 5100, name: "http");

// Migration Agent
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReference(controlPlane)
    .WithEnvironment("PackageStore__Type", "filesystem");

builder.Build().Run();
```

`AddPortablePostgres` is a custom Aspire resource that extracts and starts the bundled PostgreSQL binary from the application's own directory. It does not call Docker.

### Development / CI AppHost

This profile is used by engineers building the platform and by all CI/CD pipeline stages (build, test, preview, production gate). The same profile runs identically on a developer's machine and on the CI agent — this is the CD guarantee.

The AppHost supports two launch subprofiles, controlled by the `DEVOPS_MIGRATION_INFRA` environment variable (or `launchSettings.json`). Both use the same application code. The switch validates both production architectures in the same pipeline:

| Subprofile | PostgreSQL | Package storage | Docker required |
|---|---|---|---|
| `dev-portable` | Portable binary (`AddPortablePostgres`) | `file:///` | No |
| `dev-docker` | Docker container (`RunAsContainer`) | Azurite (`RunAsEmulator`) | Yes |

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var infra = builder.Configuration["DEVOPS_MIGRATION_INFRA"] ?? "portable";

IResourceBuilder<IResourceWithConnectionString> postgres;
IResourceBuilder<BlobsResource>? storage = null;

if (infra == "docker")
{
    // Docker subprofile — full Azure API parity.
    // PostgreSQL in Docker: identical API to Azure PostgreSQL Flexible Server.
    postgres = builder.AddAzurePostgresFlexibleServer("postgres")
        .RunAsContainer(c => c.WithEphemeralVolume())
        .AddDatabase("controlplane-db");

    // Azurite: same Azure SDK BlobContainerClient used in production.
    storage = builder.AddAzureStorage("storage")
        .RunAsEmulator()
        .AddBlobs("packages");
}
else
{
    // Portable subprofile (default) — no Docker, validates Standalone architecture.
    postgres = builder.AddPortablePostgres("postgres")
        .AddDatabase("controlplane-db");
}

var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlane>("controlplane")
    .WithReference(postgres)
    .WithEnvironment("PackageStore__Type", infra == "docker" ? "azureblob" : "filesystem")
    .WithHttpEndpoint(port: 5100, name: "http");

if (storage != null)
    controlPlane.WithReference(storage);

var agent = builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReference(controlPlane)
    .WithEnvironment("PackageStore__Type", infra == "docker" ? "azureblob" : "filesystem");

if (storage != null)
    agent.WithReference(storage);

builder.Build().Run();
```

**launchSettings.json** defines both subprofiles so engineers can switch with one click:

```json
{
  "profiles": {
    "dev-portable": {
      "commandName": "Project",
      "environmentVariables": {
        "DOTNET_ENVIRONMENT": "Development",
        "DEVOPS_MIGRATION_INFRA": "portable"
      }
    },
    "dev-docker": {
      "commandName": "Project",
      "environmentVariables": {
        "DOTNET_ENVIRONMENT": "Development",
        "DEVOPS_MIGRATION_INFRA": "docker"
      }
    }
  }
}
```

**CD contract:** every pipeline stage — local, preview, production gate — runs **both** subprofiles. The pipeline fails if either subprofile fails. There is no "CI-only" configuration.

**What each subprofile validates:**
- `dev-portable` — validates the Standalone operator architecture: portable PostgreSQL binary, filesystem `IArtefactStore`, zero external dependencies
- `dev-docker` — validates the Self-Hosted/Managed architecture: real PostgreSQL via Docker (same wire protocol as Azure PostgreSQL Flexible Server), Azure Blob SDK via Azurite (same `BlobContainerClient` code runs in production unmodified)

### Self-Hosted / Managed AppHost (Azure)

Self-Hosted and Managed both use the Azure AppHost. `azd up` provisions the real Azure resources; locally, Aspire substitutes the Azure resources with their Azure-hosted equivalents.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Azure PostgreSQL Flexible Server.
// Local dev: connect to Azure (or developer-supplied instance).
// azd up: Azure PostgreSQL Flexible Server provisioned automatically.
var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
    .AddDatabase("controlplane-db");

// Azure Blob Storage for migration package storage.
// azd up: Azure Blob Storage provisioned automatically.
var storage = builder.AddAzureStorage("storage")
    .AddBlobs("packages");

// Control Plane API
var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlane>("controlplane")
    .WithReference(postgres)
    .WithReference(storage)
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithExternalHttpEndpoints();

// Migration Agent (can scale to multiple instances)
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReference(controlPlane)
    .WithReference(storage)
    .WithReplicas(2);

builder.Build().Run();
```

---

## ServiceDefaults Pattern

The `ServiceDefaults` project provides shared configuration for observability and resilience:

```csharp
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }
}
```

Both the Control Plane and Migration Agent call `builder.AddServiceDefaults()` in their `Program.cs`.

---

## TUI Integration

Neither `CLI.Migration` nor `CLI.TfsMigration` is orchestrated by Aspire. Both are always standalone CLIs.

### CLI.Migration (net10.0) — Main TUI

`CLI.Migration` is the primary user-facing CLI. It discovers the Control Plane via configuration and submits `MigrationJob` definitions to it:

```json
{
  "ControlPlane": {
    "BaseUrl": "http://localhost:5100",  // Aspire local endpoint
    "Mode": "Local"                       // or "Cloud" for Azure deployment
  }
}
```

When a TFS source is configured, `CLI.Migration` also invokes `CLI.TfsMigration` as a subprocess via `ExternalToolRunner`, streaming its stdout in real time:

```csharp
// In TfsExportCommand inside CLI.Migration
var exitCode = await ExternalToolRunner.RunWithStreamingAsync(
    exeFullPath,                          // path to CLI.TfsMigration exe
    $"export --tfsserver {settings.TfsServer} --project {settings.Project} --output {settings.OutputFolder}",
    onOutput: line => AnsiConsole.MarkupLineInterpolated($"[grey]{line}[/]"),
    onError:  line => AnsiConsole.MarkupLineInterpolated($"[red]{line}[/]")
);
```

### CLI.TfsMigration (net481) — TFS Exporter CLI

`CLI.TfsMigration` wraps the TFS Object Model and can be used in two ways:

**1. As a subprocess of CLI.Migration** (the normal path)

```powershell
# Terminal 1: Start Aspire orchestration
cd src/DevOpsMigrationPlatform.AppHost
dotnet run

# Terminal 2: Run the main TUI — it automatically spawns CLI.TfsMigration for TFS exports
cd src/DevOpsMigrationPlatform.CLI.Migration
dotnet run -- tfsexport --tfsserver http://tfs:8080/tfs --project MyProject
```

**2. As a standalone CLI** (direct invocation, no main TUI required)

A user or script can call `CLI.TfsMigration` directly for simple one-off TFS exports without the full migration stack:

```powershell
# Run the net481 CLI directly
.\TfsMigration.exe export --tfsserver http://tfs:8080/tfs --project MyProject --output D:\exports\run-001
```

This is useful for:
- Running in an isolated network zone with access to TFS but not the Control Plane
- Scripted or pipeline-driven exports without the interactive TUI
- Debugging TFS connectivity issues independently of the migration stack

The TUI submits jobs to `http://localhost:5100/jobs` (the Aspire-managed Control Plane) for the import and orchestration phases; the TFS export phase runs via `CLI.TfsMigration` (subprocess or standalone) and writes directly to the package on disk.

---

## Operational Modes

All three modes run the same stack. The difference is topology — where that stack runs.

### Mode 1 — Standalone (full stack on a single local machine)

```
Single Machine
├─ TUI (CLI)                    ← always local
├─ Aspire AppHost               ← orchestrates all services on this machine
│   ├─ Control Plane API        ← localhost
│   ├─ Migration Agent(s)       ← localhost
│   └─ PostgreSQL               ← portable binary, localhost (no Docker)
└─ Package Storage              ← file:/// (no blob emulator needed)
```

The full stack runs on one machine with zero external dependencies. PostgreSQL ships as a bundled portable binary (`AddPortablePostgres`) and is launched as a child process by the AppHost — no Docker, no installer, no network required. Package storage uses the local filesystem. This is the closest experience to the original migration tool: run one command, migration executes locally.

**Use when:** Single-operator migrations, air-gapped environments, or development and testing.

**Benefits:**

- Zero external dependencies — no Docker, no installer, no network
- Identical stack and code paths to Self-Hosted and Managed
- Full observability dashboard at `http://localhost:15888`

### Mode 2 — Self-Hosted (same Azure stack as Managed, customer-operated)

```
Developer Machine
└─ TUI (CLI)                    ← always local
      ↓ HTTPS
Customer Azure Subscription
├─ Control Plane (Container App)
├─ Migration Agent(s) (Container Apps)
├─ PostgreSQL Flexible Server
└─ Azure Blob Storage
```

The customer runs `azd up` in their own Azure subscription and operates the resulting infrastructure. The stack — Container Apps, PostgreSQL Flexible Server, and Azure Blob Storage — is identical to Managed mode. The only difference is who provisions it and who pays the Azure bill. The TUI is always run locally by the operator. Multiple teams and migration runs are coordinated from a single control plane.

**Use when:** Organisations want control over their own Azure infrastructure, require data residency within their own Azure tenant, or prefer to operate the platform themselves.

**Benefits:**

- Full control — data stays within the organisation's Azure tenant
- Multiple migration runs from one control plane
- Identical code paths and infrastructure to Managed mode
- No NKD Agility dependency after initial setup

### Mode 3 — Managed (Azure Container Apps, hosted service)

```
Developer Machine
└─ TUI (CLI)                    ← always local
      ↓ HTTPS
Azure Subscription
├─ Control Plane (Container App)
├─ Migration Agent(s) (Container Apps)
├─ PostgreSQL Flexible Server
└─ Azure Blob Storage
```

NKD Agility provisions and operates the Azure stack on behalf of the customer. The Aspire AppHost declaration is identical to Self-Hosted — `azd up` provisions the same Azure resources. Organisations use this without operating any infrastructure themselves.

**Use when:** A managed service is preferred, infrastructure operation is not desired, or elastic scaling across many concurrent migrations is required.

**Benefits:**

- No infrastructure to operate
- Elastic scaling (Container Apps scale-out)
- Network isolation (export/import agents in separate zones)
- Multi-tenant capable with persistent job history

### Summary: Where is PostgreSQL?

| Mode | PostgreSQL present? | Who runs it? |
|---|---|---|
| Standalone | **Yes** | Portable binary (bundled, no Docker) |
| Self-Hosted | **Yes** | Azure PostgreSQL Flexible Server (customer subscription) |
| Managed | **Yes** | Azure PostgreSQL Flexible Server (NKD Agility subscription) |

The `Checkpoints/idmap.db` SQLite file (work item ID mapping, inside the package) is present in all three modes. It is not the control plane's database.

---

## Service Discovery

Aspire's service discovery eliminates hardcoded URLs:

### In Migration Agent

```csharp
// NO hardcoded URL - Aspire injects the endpoint via service discovery
builder.Services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri("http://controlplane");  // Aspire resolves this
});
```

### In TUI (Cloud Mode)

```csharp
// TUI explicitly configures cloud endpoint
var controlPlaneUrl = configuration["ControlPlane:BaseUrl"];  // https://controlplane.azurecontainerapps.io
services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(controlPlaneUrl);
});
```

---

## Configuration and Secrets

### Local Development

Aspire injects all connection strings automatically. No manual configuration is required for local development. The values it injects are:

```json
{
  "ConnectionStrings": {
    "controlplane-db": "Host=localhost;Port=<aspire-assigned>;Database=controlplane-db;Username=postgres;Password=<aspire-generated>",
    "packages": "UseDevelopmentStorage=true"
  }
}
```

These values are written to the `.NET User Secrets` store of each dependent project by Aspire at startup. Developers must not hardcode or commit them.

### Cloud Deployment

Azure Container Apps environment variables and Key Vault references:

```bicep
resource controlPlaneApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'controlplane'
  properties: {
    configuration: {
      secrets: [
        {
          name: 'db-connection-string'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/db-connection-string'
          identity: managedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'controlplane'
          image: '${acr.properties.loginServer}/controlplane:latest'
          env: [
            {
              name: 'ConnectionStrings__controlplane-db'
              secretRef: 'db-connection-string'
            }
          ]
        }
      ]
    }
  }
}
```

---

## Observability

Aspire provides a unified dashboard at **`http://localhost:15888`** showing:

- **Logs**: Structured logs from Control Plane and Migration Agents
- **Traces**: Distributed traces across job submission → lease → execution
- **Metrics**: Request rates, CPU, memory, cursor progress
- **Resources**: Container status, health checks

### Key Telemetry Points

| Component | Emits |
|---|---|
| Control Plane | Job submission, lease events, progress updates |
| Migration Agent | Lease polls, heartbeats, cursor writes, module execution |
| Job Engine | Module start/complete, cursor advancement, errors |
| IArtefactStore | Read/write operations, blob access latency |

All telemetry uses OpenTelemetry and flows to the Aspire dashboard locally or Azure Monitor in production.

---

## Deployment

### Local Development

```powershell
# Start Aspire orchestration (Control Plane + Agents + PostgreSQL + Azurite)
cd src\DevOpsMigrationPlatform.AppHost
dotnet run

# In another terminal, run the main TUI (automatically spawns CLI.TfsMigration for TFS sources)
cd src\DevOpsMigrationPlatform.CLI.Migration
dotnet run -- export --config config.json

# Or invoke the TFS CLI directly without the main TUI
.\src\DevOpsMigrationPlatform.CLI.TfsMigration\bin\Debug\net481\TfsMigration.exe export --tfsserver http://tfs:8080/tfs --project MyProject --output D:\exports\run-001
```

> **Binary location**: `CLI.Migration` resolves the `CLI.TfsMigration` executable path from configuration (`tfsExporter.executablePath`). In development this points to the `bin/Debug/net481` output folder. In production it points to the pre-built binary shipped alongside the main TUI.

### Cloud Deployment (Azure Container Apps)

Aspire includes built-in Azure deployment via `azd`:

```powershell
# Provision Azure resources and deploy
azd init
azd up
```

This creates:
- Azure Container Apps environment
- Control Plane container app
- Migration Agent container app(s)
- PostgreSQL Flexible Server
- Azure Blob Storage
- Managed Identity and Key Vault

The TUI then connects to the cloud endpoint:

```json
{
  "ControlPlane": {
    "BaseUrl": "https://controlplane-abc123.azurecontainerapps.io",
    "Mode": "Cloud"
  }
}
```

---

## Package Storage Modes

### Local (Filesystem)

```json
"artefacts": {
  "packageUri": "file:///D:/exports/run-001"
}
```

The Migration Agent uses `FileSystemArtefactStore`.

### Local (Azurite)

```json
"artefacts": {
  "packageUri": "azureblob://localhost:10000/packages/run-001"
}
```

Aspire runs Azurite automatically. The Migration Agent uses `AzureBlobArtefactStore` with local emulator credentials.

### Cloud (Azure Blob Storage)

```json
"artefacts": {
  "packageUri": "azureblob://mystorageaccount.blob.core.windows.net/packages/run-001"
}
```

The Migration Agent uses Managed Identity to access Azure Blob Storage.

---

## Testing Scenarios

Aspire makes it trivial to test cloud scenarios locally:

### Scenario 1: Single Agent

```csharp
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReplicas(1);
```

### Scenario 2: Multiple Agents (Lease Competition)

```csharp
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReplicas(3);  // Test lease assignment and heartbeat timeout
```

### Scenario 3: Network Isolation (Export + Import Agents)

```csharp
var exportAgent = builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("export-agent")
    .WithEnvironment("AGENT_MODE", "ExportOnly");

var importAgent = builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("import-agent")
    .WithEnvironment("AGENT_MODE", "ImportOnly");
```

---

## Migration from Legacy Architecture

If the system was previously built without Aspire:

1. **Control Plane**: Already ASP.NET Core → add `builder.AddServiceDefaults()`
2. **Migration Agent**: Already a worker → add `builder.AddServiceDefaults()`
3. **Create AppHost**: New project referencing Control Plane + Agent
4. **Create ServiceDefaults**: New shared project for observability
5. **TUI**: No changes required (already standalone)

---

## Benefits Summary

| Benefit | Local Dev | Cloud Deploy |
|---|---|---|
| Unified orchestration | ✓ | ✓ (via `azd`) |
| Service discovery | ✓ | ✓ |
| Observability (OTel) | ✓ | ✓ |
| Configuration management | ✓ | ✓ |
| Secrets management | User Secrets | Key Vault |
| Multi-instance testing | ✓ | ✓ |
| Zero infrastructure setup | ✓ | Automated via `azd` |

---

## Non-Negotiable Rules

1. Neither `CLI.Migration` nor `CLI.TfsMigration` must ever be orchestrated by Aspire — both are always standalone CLIs.
2. `CLI.TfsMigration` must be invocable independently, without`CLI.Migration` present. It writes directly to the package output path and exits with a standard exit code.
3. `CLI.Migration` must invoke `CLI.TfsMigration` via `ExternalToolRunner` (subprocess) — never via a direct assembly or project reference.
4. The `CLI.TfsMigration` executable path must come from configuration, not hardcoded development paths in production builds.
5. The Control Plane and Migration Agent must call `builder.AddServiceDefaults()` for consistent observability.
6. Service discovery must be used for agent-to-control-plane communication (no hardcoded URLs in agent code).
7. The AppHost must support both filesystem and blob storage configurations.
8. Aspire's dashboard is the primary local observability tool — do not build custom dashboards for local dev.
9. Cloud deployment must use `azd` and Azure Container Apps — do not deploy Aspire-managed components to VMs or App Service.

---

## See Also

- [Control Plane](control-plane.md) — API contract and responsibilities
- [Migration Agent](migration-agent.md) — Worker execution model
- [TUI](tui.md) — Terminal UI progress display
- [CLI](cli.md) — Command-line interface and job submission
- [Configuration](configuration.md) — Config schema and validation
- [Artefact Store](artefact-store.md) — Storage abstraction and URI schemes

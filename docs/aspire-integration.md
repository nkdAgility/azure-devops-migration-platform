# Microsoft Aspire Integration

## Purpose

Microsoft Aspire provides cloud-ready orchestration for the Azure DevOps Migration Platform, enabling seamless transitions between local development and cloud deployment. The TUI always runs locally, but the control plane and migration agents can run either locally (orchestrated by Aspire) or in the cloud (Azure Container Apps).

---

## Architecture Fit

The migration platform's execution model aligns perfectly with Aspire's design:

| Component | Always Local | Can Run Local | Can Run Cloud | Aspire Role |
|---|---|---|---|---|
| **TUI** | ✓ | ✓ | ✗ | CLI project (not orchestrated) |
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

  DevOpsMigrationPlatform.CLI.Migration/         ← TUI (not orchestrated by Aspire)
    Program.cs                                    ← local CLI entry point
    DevOpsMigrationPlatform.CLI.Migration.csproj

  DevOpsMigrationPlatform.CLI.TfsMigration/         ← TUI (not orchestrated by Aspire)
    Program.cs                                    ← local CLI entry point
    DevOpsMigrationPlatform.CLI.TfsMigration.csproj
```

---

## AppHost Configuration

The Aspire AppHost (`DevOpsMigrationPlatform.AppHost`) orchestrates local development:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL for control plane job storage
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("controlplane-db");

// Azure Blob Storage emulator (Azurite) for package storage
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator()
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
    .WithReplicas(2);  // Run 2 agents locally for testing

builder.Build().Run();
```

### Key Features

- **Service Discovery**: Migration Agents discover the Control Plane via Aspire's service discovery (`http://controlplane`)
- **Configuration**: Connection strings automatically injected via `IConfiguration`
- **Observability**: Built-in OpenTelemetry, Prometheus, and dashboard at `http://localhost:15888`
- **Local Storage**: Azurite provides local blob storage for testing cloud scenarios
- **Scaling**: Easily test multiple Migration Agent instances locally

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

The TUI runs as a standalone CLI and is **not** orchestrated by Aspire. It discovers the Control Plane via configuration:

```json
{
  "ControlPlane": {
    "BaseUrl": "http://localhost:5100",  // Aspire local endpoint
    "Mode": "Local"                       // or "Cloud" for Azure deployment
  }
}
```

When running locally with Aspire:

```powershell
# Terminal 1: Start Aspire orchestration (Control Plane + Agents)
cd src/DevOpsMigrationPlatform.AppHost
dotnet run

# Terminal 2: Run TUI and submit jobs
cd src/DevOpsMigrationPlatform.CLI.Migration
dotnet run -- export --config .\config.json
```

The TUI submits jobs to `http://localhost:5100/jobs` (the Aspire-managed Control Plane).

---

## Local vs Cloud Execution Modes

### Local Mode (Aspire)

```
Developer Machine
├─ TUI (CLI)                    ← always local
├─ Aspire AppHost               ← orchestrates local services
│   ├─ Control Plane API        ← runs in local container/process
│   ├─ Migration Agent(s)       ← runs in local container/process
│   ├─ PostgreSQL               ← local container
│   └─ Azurite (blob emulator)  ← local container
└─ Package Storage              ← file:/// or azureblob://localhost:10000
```

**Benefits:**
- Full stack runs locally
- Fast iteration
- No cloud resources required
- Identical code paths to production

### Cloud Mode (Azure Container Apps)

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

**Benefits:**
- Scalable execution
- Network isolation (export/import agents in separate zones)
- Persistent job history
- Multi-tenant capable

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

Aspire manages local configuration via User Secrets and `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "controlplane-db": "Host=localhost;Database=controlplane;Username=postgres;Password=***",
    "packages": "UseDevelopmentStorage=true"  // Azurite
  }
}
```

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
# Start Aspire orchestration
cd src\DevOpsMigrationPlatform.AppHost
dotnet run

# In another terminal, run TUI
cd src\DevOpsMigrationPlatform.CLI.Migration
dotnet run -- export --config config.json
```

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

1. The TUI must never be orchestrated by Aspire — it is always a standalone CLI.
2. The Control Plane and Migration Agent must call `builder.AddServiceDefaults()` for consistent observability.
3. Service discovery must be used for agent-to-control-plane communication (no hardcoded URLs in agent code).
4. The AppHost must support both filesystem and blob storage configurations.
5. Aspire's dashboard is the primary local observability tool — do not build custom dashboards for local dev.
6. Cloud deployment must use `azd` and Azure Container Apps — do not deploy Aspire-managed components to VMs or App Service.

---

## See Also

- [Control Plane](control-plane.md) — API contract and responsibilities
- [Migration Agent](migration-agent.md) — Worker execution model
- [TUI](tui.md) — Terminal UI and job submission
- [Configuration](configuration.md) — Config schema and validation
- [Artefact Store](artefact-store.md) — Storage abstraction and URI schemes

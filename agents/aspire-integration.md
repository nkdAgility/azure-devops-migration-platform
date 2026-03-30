# Aspire Integration Guardrails

> These are non-negotiable rules for Microsoft Aspire integration. If any code conflicts with these rules, reject the change.

---

## Project Structure Rules

### MUST Have

- `DevOpsMigrationPlatform.AppHost` — Aspire orchestrator project
- `DevOpsMigrationPlatform.ServiceDefaults` — Shared observability/resilience extensions
- `DevOpsMigrationPlatform.ControlPlane` — ASP.NET Core Web API
- `DevOpsMigrationPlatform.MigrationAgent` — Worker Service

### MUST NOT Have

- TUI (CLI) added to AppHost resources — the TUI is always standalone
- Multiple AppHost projects — only one orchestrator per solution
- Custom health check or metrics endpoints bypassing ServiceDefaults
- Hardcoded URLs in Migration Agent or Control Plane code (use service discovery)

---

## ServiceDefaults Contract

### Every service (Control Plane, Migration Agent) MUST call:

```csharp
builder.AddServiceDefaults();
```

### ServiceDefaults MUST provide:

- OpenTelemetry logging with structured output
- OpenTelemetry metrics (ASP.NET Core, HTTP client, runtime)
- OpenTelemetry tracing (ASP.NET Core, HTTP client, EF Core)
- Service discovery registration and client configuration
- Standard resilience policies for HTTP clients
- Health check endpoints (`/health` and `/alive`)

### ServiceDefaults MUST NOT:

- Include business logic
- Reference domain or infrastructure projects
- Configure database migrations (belongs in Control Plane startup)
- Configure module-specific behaviors

---

## Service Discovery Rules

### Migration Agent → Control Plane

**MUST:**
```csharp
builder.Services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri("http://controlplane");  // Aspire resolves this
});
```

**MUST NOT:**
```csharp
// ❌ FORBIDDEN: Hardcoded URLs in agent code
client.BaseAddress = new Uri("http://localhost:5100");
client.BaseAddress = new Uri(configuration["ControlPlaneUrl"]);  // ❌ agents use discovery only
```

### TUI → Control Plane (Cloud Mode)

**MUST:**
```csharp
// TUI explicitly configures endpoint from config file
var controlPlaneUrl = configuration["ControlPlane:BaseUrl"];
services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(controlPlaneUrl);
});
```

The TUI does not use Aspire service discovery because it is not orchestrated by Aspire.

---

## AppHost Configuration Rules

### MUST include:

```csharp
// Database for control plane
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("controlplane-db");

// Blob storage (Azurite locally, Azure Blob in cloud)
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator()
    .AddBlobs("packages");

// Control Plane API
var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlane>("controlplane")
    .WithReference(postgres)
    .WithReference(storage)
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithExternalHttpEndpoints();

// Migration Agent(s)
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReference(controlPlane)
    .WithReference(storage)
    .WithReplicas(1);  // configurable for testing
```

### MUST NOT include:

- TUI project reference (it runs standalone)
- Direct references to domain or business logic projects (only Control Plane and Agent)
- Custom container configurations bypassing Aspire's built-in support
- Environment-specific secrets (use User Secrets locally, Key Vault in cloud)

---

## Storage Configuration Rules

### Local Development

**MUST support both:**

1. **Filesystem** (file:///)
   ```json
   "artefacts": {
     "packageUri": "file:///D:/exports/run-001"
   }
   ```

2. **Azurite** (azureblob://localhost:10000)
   ```json
   "artefacts": {
     "packageUri": "azureblob://localhost:10000/packages/run-001"
   }
   ```

### Cloud Deployment

**MUST use:**
```json
"artefacts": {
  "packageUri": "azureblob://<account>.blob.core.windows.net/packages/run-001"
}
```

**Authentication:**
- Local: Azurite default credentials or Development Storage
- Cloud: Managed Identity (no connection strings in code)

---

## Observability Rules

### Telemetry MUST be emitted for:

| Event | Component | Trace | Metric | Log |
|---|---|---|---|---|
| Job submitted | Control Plane | ✓ | ✓ | ✓ |
| Lease acquired | Migration Agent | ✓ | ✓ | ✓ |
| Heartbeat sent | Migration Agent | ✗ | ✓ | ✓ |
| Module started | Job Engine | ✓ | ✓ | ✓ |
| Cursor advanced | Job Engine | ✗ | ✓ | ✓ |
| Module completed | Job Engine | ✓ | ✓ | ✓ |
| Job completed | Migration Agent | ✓ | ✓ | ✓ |
| Error occurred | All | ✓ | ✓ | ✓ |

### MUST NOT:

- Write directly to `Console.WriteLine` (use `ILogger<T>`)
- Bypass OpenTelemetry for custom metrics or traces
- Log sensitive data (PATs, connection strings, Key Vault secrets)
- Emit traces with unbounded cardinality (e.g., work item IDs in span names)

---

## Deployment Rules

### Local Development

**MUST:**
```powershell
cd src\DevOpsMigrationPlatform.AppHost
dotnet run
```

**MUST NOT:**
- Run Control Plane or Migration Agent standalone without Aspire orchestration during development
- Manually start PostgreSQL, Azurite, or other dependencies (Aspire manages these)

### Cloud Deployment

**MUST use `azd`:**
```powershell
azd init
azd up
```

**MUST provision:**
- Azure Container Apps environment
- Control Plane container app
- Migration Agent container app(s)
- PostgreSQL Flexible Server
- Azure Blob Storage account
- Managed Identity for agent → blob authentication
- Key Vault for secrets

**MUST NOT:**
- Deploy to Azure App Service (use Container Apps)
- Deploy to VMs (use Container Apps)
- Deploy to AKS unless explicitly required for advanced scenarios
- Use SQL Database instead of PostgreSQL (Aspire defaults to PostgreSQL)

---

## Configuration Management Rules

### Local (User Secrets)

**MUST:**
```powershell
dotnet user-secrets set "ConnectionStrings:controlplane-db" "Host=localhost;Database=controlplane;Username=postgres;Password=***"
```

**MUST NOT:**
- Commit secrets to `appsettings.json`
- Store secrets in environment variables long-term

### Cloud (Key Vault)

**MUST:**
- Reference Key Vault secrets via Managed Identity
- Use Azure Container Apps secret references in Bicep/ARM templates

**MUST NOT:**
- Retrieve secrets in Control Plane code (Container Apps injects them)
- Unwrap Key Vault secrets in the TUI (TUI submits secret references only)

---

## Scaling Rules

### Local Testing

**MUST allow:**
```csharp
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReplicas(3);  // Test lease competition locally
```

### Cloud Auto-Scaling

**MUST configure:**
```bicep
resource migrationAgent 'Microsoft.App/containerApps@2023-05-01' = {
  properties: {
    configuration: {
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [
          {
            name: 'queue-depth'
            custom: {
              type: 'azure-queue'
              metadata: {
                queueName: 'pending-jobs'
                queueLength: '5'
              }
            }
          }
        ]
      }
    }
  }
}
```

**MUST NOT:**
- Manually scale agent instances via Azure Portal (use KEDA rules)
- Run multiple Control Plane instances without session affinity or distributed locking

---

## TUI Rules

### The TUI MUST:

- Run as a standalone CLI (not orchestrated by Aspire)
- Read Control Plane endpoint from configuration file
- Support both local (`http://localhost:5100`) and cloud (`https://controlplane.azurecontainerapps.io`) endpoints
- Validate job definitions before submission
- Display job status via Control Plane API

### The TUI MUST NOT:

- Be added to the Aspire AppHost
- Use Aspire service discovery
- Execute Job Engine logic directly (always submit to Control Plane)

---

## Prohibited Patterns

Reject any code that:

- Adds TUI to AppHost resources.
- Hardcodes Control Plane URLs in Migration Agent code.
- Bypasses ServiceDefaults observability configuration.
- Uses custom health checks without calling `AddDefaultHealthChecks()`.
- Deploys Aspire-managed components to Azure App Service or VMs.
- Stores secrets in `appsettings.json` or environment variables long-term.
- Logs sensitive data (connection strings, PATs, Key Vault secrets).
- Manually manages PostgreSQL, Azurite, or other Aspire-managed resources during local dev.

---

## Validation Checklist

Before accepting a change, verify:

- [ ] ServiceDefaults is referenced by all services (Control Plane, Agent).
- [ ] AppHost does not include TUI project reference.
- [ ] Migration Agent uses service discovery for Control Plane communication.
- [ ] TUI explicitly configures Control Plane endpoint from configuration.
- [ ] OpenTelemetry is configured via ServiceDefaults only.
- [ ] No hardcoded URLs in agent or Control Plane code.
- [ ] Local storage supports both file:/// and azureblob://localhost:10000.
- [ ] Cloud deployment uses `azd` and Azure Container Apps.
- [ ] Secrets are managed via User Secrets locally and Key Vault in cloud.
- [ ] No sensitive data is logged.

---

## Final Rule

Aspire orchestrates the Control Plane and Migration Agent locally and in the cloud.

The TUI is always standalone.

No exceptions.

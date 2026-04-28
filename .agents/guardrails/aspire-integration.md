# Aspire Integration Guardrails

> These are non-negotiable rules for Microsoft Aspire integration. If any code conflicts with these rules, reject the change.

---

## Project Structure Rules

### MUST Have

- `DevOpsMigrationPlatform.AppHost` — Aspire orchestrator project
- `DevOpsMigrationPlatform.ServiceDefaults` — Shared observability/resilience extensions
- `DevOpsMigrationPlatform.ControlPlane` — Service library: HTTP API controllers, job state machine, lease protocol, EF Core data model; no entry point
- `DevOpsMigrationPlatform.ControlPlaneHost` — Deployable ASP.NET Core host: references `ControlPlane` library, manages Migration Agent lifecycle in all topologies, is the Aspire resource target
- `DevOpsMigrationPlatform.MigrationAgent` — Worker Service: executes jobs under lease, polls `ControlPlaneHost`
- `DevOpsMigrationPlatform.CLI.Migration` (net10.0) — Main CLI; drives Aspire for local and server execution; submits all jobs (including TFS) to the control plane; connects to remote endpoint for cloud
- `DevOpsMigrationPlatform.TfsMigrationAgent` (net481) — TFS migration agent; spawned by `AgentLifecycleService` on Windows or run independently; not added to AppHost resources

### MUST NOT Have

- `CLI.Migration` or `TfsMigrationAgent` added to AppHost resources — CLI is always standalone; TFS agent is managed by `AgentLifecycleService`
- Multiple AppHost projects — only one orchestrator per solution
- Custom health check or metrics endpoints bypassing ServiceDefaults
- Hardcoded URLs in `Agent` or `ControlPlaneHost` code (use service discovery)
- A direct assembly or project reference from any .NET 10 project to `TfsMigrationAgent` (net481) — the TFS agent is a separate process
- Custom process management for starting the control plane or agents — use Aspire for orchestration, `AgentLifecycleService` for agent spawning

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

### CLI → Control Plane (Standalone)

When `Environment.Type` is `Standalone` (the default), the CLI starts `LocalStackHost` which launches ControlPlane and MigrationAgent — preferring **process-per-component** mode (separate child processes via `ChildProcessHost`) when published binaries are found, with automatic fallback to **in-process** hosting when they are not. The control plane starts on `http://localhost:5100`.

### CLI → Control Plane (Hosted)

**MUST:**
```csharp
// CLI connects to remote endpoint from EnvironmentOptions
services.AddHttpClient<ControlPlaneClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
    client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
});
```

The CLI does not use Aspire service discovery directly because the CLI itself is not an Aspire resource. The CLI drives Aspire to start services, and those services use Aspire service discovery among themselves.

### Agent → ControlPlaneHost (AppHost only)

When running under Aspire (all local/server topologies), Aspire service discovery resolves the endpoint:

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

---

## AppHost Configuration Rules

The AppHost defines the service topology for **cloud provisioning and developer-standalone use only**. It is the `azd up` target for cloud deployment and a convenience tool for developer-standalone runs. The CLI does **not** use or invoke the AppHost project at runtime — when `Environment.Type` is `Standalone`, the CLI starts `LocalStackHost` to launch the same services (preferring process-per-component mode, with in-process fallback).

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

// ControlPlaneHost
var controlPlane = builder.AddProject<Projects.DevOpsMigrationPlatform_ControlPlaneHost>("controlplane")
    .WithReference(postgres)
    .WithReference(storage)
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithExternalHttpEndpoints();

// Agent(s)
builder.AddProject<Projects.DevOpsMigrationPlatform_MigrationAgent>("migration-agent")
    .WithReference(controlPlane)
    .WithReference(storage)
    .WithReplicas(1);  // configurable for testing
```

### MUST NOT include:

- CLI project reference (it runs standalone)
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

2. **Azurite** (https://127.0.0.1:10000/devstoreaccount1/...)
   ```json
   "artefacts": {
     "packageUri": "https://127.0.0.1:10000/devstoreaccount1/packages/myorg/myproject"
   }
   ```

### Cloud Deployment

**MUST use:**
```json
"artefacts": {
  "packageUri": "https://<account>.blob.core.windows.net/packages/myorg/myproject"
}
```

**Authentication:**
- Local: Azurite default credentials or Development Storage
- Cloud: Managed Identity (no connection strings in code); or SAS token appended as query string to the URL

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

Operators run the CLI directly — Aspire is driven internally:
```powershell
cd src\DevOpsMigrationPlatform.CLI.Migration
dotnet run -- export --config migration.json
```

Developers and CI pipelines can also use the AppHost directly:
```powershell
cd src\DevOpsMigrationPlatform.AppHost
dotnet run
```

**MUST NOT:**
- Require Docker, an installer, or manual AppHost startup for local operator usage
- Direct operators to start the AppHost separately before running the CLI

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
- Log sensitive data (PATs, connection strings)

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

## CLI Rules

### CLI.Migration (net10.0) — Main CLI MUST:

- Run as a standalone CLI (not orchestrated by Aspire)
- Drive Aspire programmatically to start the control plane, agents, and PostgreSQL when `Environment.Type` is `Standalone`
- Connect to a remote control plane endpoint when `Environment.Type` is `Hosted`
- Support both local (`http://localhost:5100`) and cloud (`https://controlplane.azurecontainerapps.io`) endpoints
- Validate job definitions before submission
- Display job status via Control Plane API

### CLI.Migration MUST NOT:

- Be added to the Aspire AppHost
- Use Aspire service discovery
- Execute Job Engine logic directly (always submit to Control Plane)
- Hold a direct project or assembly reference to `TfsMigrationAgent`

### TfsMigrationAgent (net481) — TFS Migration Agent MUST:

- Poll `GET /agents/lease?capabilities=tfs` from the control plane (same protocol as `MigrationAgent`)
- Use `IModule` dispatch (`TfsJobAgentWorker` accepts `IEnumerable<IModule>`)
- Write to the package exclusively via `IArtefactStore` (`FileSystemArtefactStore`) and `IStateStore`
- Report progress via `POST /agents/lease/{leaseId}/progress` to the control plane
- Be spawned by `AgentLifecycleService` on Windows; skipped silently on Linux/macOS

### TfsMigrationAgent MUST NOT:

- Be added to the Aspire AppHost
- Be referenced by any net10.0 project via `<ProjectReference>`
- Accept credentials via command-line arguments (job contract only)

---

## Prohibited Patterns

Reject any code that:

- Adds `CLI.Migration` or `TfsMigrationAgent` to AppHost resources.
- Adds a direct project or assembly reference from any net10.0 project to `TfsMigrationAgent`.
- Spawns a TFS subprocess (`tfsmigration.exe`) from the CLI or any .NET 10 component.
- Hardcodes Control Plane URLs in Migration Agent code.
- Bypasses ServiceDefaults observability configuration.
- Uses custom health checks without calling `AddDefaultHealthChecks()`.
- Deploys Aspire-managed components to Azure App Service or VMs.
- Stores secrets in `appsettings.json` or environment variables long-term.
- Logs sensitive data (connection strings, PATs, Key Vault secrets).
- Requires operators to start the AppHost to run a migration.
- Moves migration execution logic into the in-process control plane host within the CLI.

---

## Validation Checklist

Before accepting a change, verify:

- [ ] ServiceDefaults is referenced by all services (Control Plane, Agent).
- [ ] AppHost does not include `CLI.Migration` or `TfsMigrationAgent` project references.
- [ ] No net10.0 project holds a direct reference to `TfsMigrationAgent`.
- [ ] No code spawns a TFS subprocess from a .NET 10 component.
- [ ] Agent uses service discovery for `ControlPlaneHost` communication (when running under AppHost).
- [ ] CLI reads `Environment.Type` from config to determine whether to start LocalStackHost (Standalone) or connect remotely (Hosted).
- [ ] OpenTelemetry is configured via ServiceDefaults only.
- [ ] No hardcoded URLs in agent or Control Plane code.
- [ ] Local storage supports both file:/// and Azurite (https://127.0.0.1:10000/devstoreaccount1/...).
- [ ] Cloud deployment uses `azd` and Azure Container Apps.
- [ ] Secrets are managed via User Secrets locally and Key Vault in cloud.
- [ ] No sensitive data is logged.

---

## Final Rule

Aspire is the orchestration layer across all topologies. The CLI is always the operator's entry point. No exceptions.

The CLI drives Aspire for local and server execution. `azd` drives Aspire for cloud deployment.

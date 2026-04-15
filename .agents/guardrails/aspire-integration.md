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
- `DevOpsMigrationPlatform.CLI.Migration` (net10.0) — Main CLI; drives Aspire for local and server execution; spawns `CLI.TfsMigration` as subprocess for TFS sources; connects to remote endpoint for cloud
- `DevOpsMigrationPlatform.CLI.TfsMigration` (net481) — TFS exporter CLI; callable as subprocess OR independently

### MUST NOT Have

- `CLI.Migration` or `CLI.TfsMigration` added to AppHost resources — both are always standalone
- Multiple AppHost projects — only one orchestrator per solution
- Custom health check or metrics endpoints bypassing ServiceDefaults
- Hardcoded URLs in `Agent` or `ControlPlaneHost` code (use service discovery)
- A direct assembly or project reference from `CLI.Migration` (net10.0) to `CLI.TfsMigration` (net481) — subprocess via `ExternalToolRunner` only
- Custom process management for starting the control plane or agents — use Aspire exclusively

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

When `Environment.Type` is `Standalone` (the default), the CLI starts `LocalStackHost` in-process. The control plane starts on `http://localhost:5100`.

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

The AppHost defines the service topology for **cloud provisioning and developer-standalone use only**. It is the `azd up` target for cloud deployment and a convenience tool for developer-standalone runs. The CLI does **not** use or invoke the AppHost project at runtime — when `Environment.Type` is `Standalone`, the CLI starts `LocalStackHost` in-process to run the same services.

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
- Unwrap Key Vault secrets in the CLI or TUI (the CLI submits only Key Vault URI references; the TUI never submits jobs at all)

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
- Invoke `CLI.TfsMigration` via `ExternalToolRunner` (subprocess) for TFS source exports
- Resolve the `CLI.TfsMigration` executable path from configuration (`tfsExporter:executablePath`) — never a hardcoded relative path in production

### CLI.Migration MUST NOT:

- Be added to the Aspire AppHost
- Use Aspire service discovery
- Execute Job Engine logic directly (always submit to Control Plane)
- Hold a direct project or assembly reference to `CLI.TfsMigration`

### CLI.TfsMigration (net481) — TFS Exporter CLI MUST:

- Be invocable as a standalone CLI without `CLI.Migration` present
- Accept all required parameters via CLI arguments (`--tfsserver`, `--project`, `--output`, etc.)
- Read credentials from stdin JSON when PAT authentication is required
- Write output to the `--output` path following canonical package layouts
- Write NDJSON progress lines to stdout (consumed by `CLI.Migration` or a calling script)
- Exit with the standard exit codes defined in [docs/tfs-exporter.md](../../docs/tfs-exporter.md)

### CLI.TfsMigration MUST NOT:

- Be added to the Aspire AppHost
- Be referenced by any net10.0 project via `<ProjectReference>`
- Accept credentials via CLI arguments (stdin JSON only)

---

## Prohibited Patterns

Reject any code that:

- Adds `CLI.Migration` or `CLI.TfsMigration` to AppHost resources.
- Adds a direct project or assembly reference from `CLI.Migration` to `CLI.TfsMigration`.
- Hardcodes the `CLI.TfsMigration` exe path to a development-relative path in production configuration.
- Makes `CLI.TfsMigration` non-invocable as a standalone CLI (e.g. requires the main CLI to be present).
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
- [ ] AppHost does not include `CLI.Migration` or `CLI.TfsMigration` project references.
- [ ] `CLI.Migration` has no direct project reference to `CLI.TfsMigration` — subprocess via `ExternalToolRunner` only.
- [ ] `CLI.TfsMigration` can be invoked standalone (no dependency on `CLI.Migration` being present).
- [ ] `CLI.TfsMigration` exe path is read from configuration, not hardcoded in production.
- [ ] Agent uses service discovery for `ControlPlaneHost` communication (when running under AppHost).
- [ ] CLI reads `Environment.Type` from config to determine whether to start LocalStackHost (Standalone) or connect remotely (Hosted).
- [ ] OpenTelemetry is configured via ServiceDefaults only.
- [ ] No hardcoded URLs in agent or Control Plane code.
- [ ] Local storage supports both file:/// and azureblob://localhost:10000.
- [ ] Cloud deployment uses `azd` and Azure Container Apps.
- [ ] Secrets are managed via User Secrets locally and Key Vault in cloud.
- [ ] No sensitive data is logged.

---

## Final Rule

Aspire is the orchestration layer across all topologies. The CLI is always the operator's entry point. No exceptions.

The CLI drives Aspire for local and server execution. `azd` drives Aspire for cloud deployment.

# Aspire Integration Guardrails

Non-negotiable rules for Microsoft Aspire integration. Reject any code that conflicts.

---

## Project Structure

**MUST have:**
- `DevOpsMigrationPlatform.AppHost` — Aspire orchestrator (cloud provisioning + dev-standalone)
- `DevOpsMigrationPlatform.ServiceDefaults` — Shared observability/resilience
- `DevOpsMigrationPlatform.ControlPlane` — Service library (HTTP API, job state machine, lease protocol, EF Core)
- `DevOpsMigrationPlatform.ControlPlaneHost` — Deployable ASP.NET Core host (references ControlPlane, manages agent lifecycle)
- `DevOpsMigrationPlatform.MigrationAgent` — Worker Service (executes jobs under lease)
- `DevOpsMigrationPlatform.CLI.Migration` (net10.0) — Main CLI; drives Aspire for local; submits jobs to control plane
- `DevOpsMigrationPlatform.TfsMigrationAgent` (net481) — TFS agent; spawned by `AgentLifecycleService`; not in AppHost

**MUST NOT have:**
- CLI or TfsMigrationAgent added to AppHost resources
- Multiple AppHost projects
- Custom health/metrics endpoints bypassing ServiceDefaults
- Hardcoded URLs in Agent or ControlPlaneHost code (use service discovery)
- Direct project reference from any .NET 10 project to TfsMigrationAgent
- Custom process management for starting control plane/agents (use Aspire + `AgentLifecycleService`)

---

## ServiceDefaults

Every service MUST call `builder.AddServiceDefaults()`. Provides: OTel logging/metrics/tracing, service discovery, resilience policies, health checks (`/health`, `/alive`). MUST NOT contain business logic or domain references.

---

## Service Discovery

- **CLI → ControlPlane (Standalone):** CLI starts `LocalStackHost` (process-per-component preferred, in-process fallback). ControlPlane on `http://localhost:5100`.
- **CLI → ControlPlane (Hosted):** CLI reads `EnvironmentOptions.ControlPlane.BaseUrl`.
- **Agent → ControlPlaneHost:** Aspire service discovery: `client.BaseAddress = new Uri("http://controlplane")`.
- **FORBIDDEN:** Hardcoded URLs in agent code, `configuration["ControlPlaneUrl"]` in agents.

---

## AppHost

The AppHost is the `azd up` target and developer-standalone tool. CLI does NOT invoke AppHost at runtime — uses `LocalStackHost` for Standalone mode.

MUST include: PostgreSQL + database, Azure Storage (Azurite locally), ControlPlaneHost with references and `WithHttpEndpoint(5100)`, MigrationAgent with references and configurable replicas.

MUST NOT include: CLI project reference, direct domain/business logic refs, custom containers bypassing Aspire, environment-specific secrets.

---

## Storage

- **Local:** filesystem (`file:///`) AND Azurite (`https://127.0.0.1:10000/devstoreaccount1/...`)
- **Cloud:** Azure Blob (`https://<account>.blob.core.windows.net/...`)
- **Auth:** Local = Azurite defaults; Cloud = Managed Identity (no connection strings in code) or SAS token as query string.

---

## Observability

Telemetry events: Job submitted, Lease acquired, Heartbeat, Module started/completed, Cursor advanced, Job completed, Errors. All via OTel (traces + metrics + logs). MUST NOT use `Console.WriteLine`, bypass OTel, log sensitive data, or use unbounded-cardinality span names.

---

## Deployment

- **Local:** `dotnet run -- queue --config migration.json` from CLI project. No Docker/installer/manual AppHost startup required.
- **Cloud:** `azd up` → Container Apps + PostgreSQL Flexible Server + Blob Storage + Managed Identity + Key Vault. Not App Service or VMs.

---

## Configuration

- **Local:** User Secrets. Never commit secrets to `appsettings.json`.
- **Cloud:** Key Vault via Managed Identity. Container Apps secret references in Bicep/ARM.

---

## Scaling

- Local: `.WithReplicas(N)` for testing lease competition.
- Cloud: KEDA rules (queue-depth-based), `minReplicas: 1`, `maxReplicas: 10`. No manual Portal scaling. No multiple ControlPlane instances without distributed locking.

---

## CLI Rules

CLI MUST: run standalone, drive Aspire for Standalone mode, connect to remote for Hosted mode, validate jobs before submission, display status via Control Plane API.

CLI MUST NOT: be in AppHost, use Aspire service discovery, execute Job Engine logic, reference TfsMigrationAgent.

TfsMigrationAgent MUST: poll `GET /agents/lease?capabilities=tfs`, use `IModule` dispatch, write via `IArtefactStore`/`IStateStore`, report via `POST /progress`.

TfsMigrationAgent MUST NOT: be in AppHost, be referenced by .NET 10 projects, accept CLI arg credentials.

---

## Prohibited

- CLI or TfsMigrationAgent in AppHost resources.
- .NET 10 project reference to TfsMigrationAgent.
- Spawning TFS subprocess from CLI or .NET 10 component.
- Hardcoded URLs in agent/control plane code.
- Bypassing ServiceDefaults observability.
- Custom health checks without `AddDefaultHealthChecks()`.
- Aspire components deployed to App Service or VMs.
- Secrets in `appsettings.json` or long-term env vars.
- Logging sensitive data.
- Requiring operators to start AppHost to run a migration.
- Migration execution logic in the in-process control plane host within CLI.

---

## Final Rule

Aspire is the orchestration layer across all topologies. CLI is always the operator's entry point. CLI drives Aspire locally; `azd` drives Aspire for cloud. No exceptions.

# Advanced Operator Guide

Audience: Advanced Operators.

This guide covers multi-job deployments, larger organisations, hosted vs self-hosted operation, scaling, and operational diagnostics.

## Deployment Topologies

### Standalone (default)

The CLI starts a local Control Plane and agent on the operator machine. No external services required. Suitable for single-operator, single-machine migrations.

```json
"Environment": { "Type": "Standalone" }
```

### Self-Hosted

Operator deploys the Control Plane (`ControlPlaneHost`) on their own infrastructure. Agents connect to it remotely. Suitable for team environments and concurrent migrations.

```json
"Environment": {
  "Type": "Hosted",
  "ControlPlane": { "BaseUrl": "https://migration-cp.internal:5000" }
}
```

### Hosted (nkdAgility-managed)

nkdAgility operates the Control Plane. The operator runs agents in their environment. The package stays on operator infrastructure.

## Running Many Jobs Concurrently

- Each job requires its own `Package.WorkingDirectory`.
- Jobs are queued in the Control Plane and leased to available agents.
- Scale by running additional agent processes on machines with access to the package path.
- The Control Plane enforces entitlement checks at job admission — concurrent job limits depend on your licence.

## Larger Organisations

- For large organisations with many projects, run separate jobs per project.
- Monitor aggregate progress via `GET /jobs/{id}/telemetry` or the TUI.
- Use cursor-based checkpointing to safely interrupt and resume long-running exports.

## Operational Boundaries

- The Migration Agent handles .NET 10 compatible sources (Azure DevOps Services, Azure DevOps Server).
- The TFS Export Agent handles legacy Team Foundation Server sources (net481, Windows only).
- Both agent types communicate with the same Control Plane.

## Job Admission

Jobs are admitted by the Control Plane after:
1. Config schema validation
2. Entitlement check (licence and usage rights)
3. Agent availability check

A `JobAdmissionException` with a clear message is returned if any check fails.

## Scaling Agents

- Agents are stateless workers. Start additional agent processes to increase throughput.
- Each agent can run one job at a time.
- Agents discover jobs by polling `GET /jobs/available`.

## Operational Diagnostics

| Resource | How to access |
|---|---|
| Job list | `devopsmigration manage list` or TUI job list |
| Job details | `devopsmigration manage status --job <id>` |
| Structured logs | `devopsmigration manage diagnostics --job <id>` |
| Real-time progress | `devopsmigration queue ... --follow` or TUI |
| Agent health | `GET /health` on the Control Plane or agent |

## Performance Considerations

- Concurrency is controlled by `Policies.Throttle.MaxConcurrency` in the config.
- The platform streams artefacts — memory usage is bounded even for large migrations.
- Attachments are streamed via `IArtefactStore.WriteBinaryAsync`; large binaries do not require full in-memory loading.

## Further Reading

- [`observability.md`](observability.md) — traces, metrics, logs
- [`agent-hosting.md`](agent-hosting.md) — how to host agents
- [`security-and-data-sovereignty.md`](security-and-data-sovereignty.md) — data residency
- [`control-plane.md`](control-plane.md) — Control Plane responsibilities
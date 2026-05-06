# ADR 0007 — Compiler-Enforced Project Boundary Topology

## Status

Accepted

## Context

The platform has three process-level components (CLI, Control Plane, Agent) that must not share internal contracts. Before this decision, the CLI project referenced the Control Plane, Migration Agent, and all infrastructure assemblies via `<ProjectReference>`. This was caused by a `LocalStackHost` in-process fallback that hosted both the Control Plane and Migration Agent inside the CLI process when external binaries were not found.

Consequences of the blurred boundary:
- A CLI command could accidentally inject `IArtefactStore` and the compiler would not object.
- Adding any interface to `Abstractions` made it visible to all three components regardless of intent.
- Build times increased and dependency graphs became circular.

## Decision

Project reference topology is the enforcement mechanism. Boundary violations are **build errors**, not code-review findings.

**Permitted references:**

| Component | May reference |
|---|---|
| `CLI.Migration` | `Abstractions`, `Infrastructure` (config/serialization) |
| `TUI` | `Abstractions`, `Infrastructure` (config/serialization) |
| `ControlPlane` | `Abstractions`, `Abstractions.ControlPlane` |
| `MigrationAgent` | `Abstractions`, `Abstractions.Agent`, `Infrastructure.Agent` |
| `TfsMigrationAgent` | `Abstractions`, `Abstractions.Agent`, `Infrastructure.Tfs` |

**Prohibited references:**

- CLI must not reference `ControlPlane`, `MigrationAgent`, `Infrastructure.AzureDevOps`, `Infrastructure.Simulated`, `Infrastructure.Agent`, or `Infrastructure.ControlPlane`.
- Control Plane must not reference `MigrationAgent` or any Agent-internal assembly.
- No circular references between any two components.

The in-process fallback (`LocalStackHost`) is replaced by `ChildProcessHost`, which launches the Control Plane and Migration Agent as separate OS processes — identical to how `ExternalToolRunner` launches `CLI.TfsMigration`.

**Abstractions are split by consumer:**

| Assembly | Contents | Used by |
|---|---|---|
| `Abstractions` | Cross-cutting contracts, `Job`, `IProgressSink` | All components |
| `Abstractions.ControlPlane` | Control Plane HTTP API contracts | CLI (via HTTP client), ControlPlane |
| `Abstractions.Agent` | `IModule`, `IArtefactStore`, `IStateStore`, `IAnalyser` | Agent only |

## Alternatives Considered

**Convention-only boundary (comments, code review)**: Proved insufficient — the in-process fallback accumulated references for convenience. Compiler enforcement makes violations impossible to ship.

**Single flat `Abstractions` assembly**: Convenient, but allows every component to see every contract. The compiler cannot distinguish what is safe to inject in the CLI from what is safe to inject in the Agent.

**Runtime DI scope guards**: Can detect misuse at runtime but permits it to compile and ship.

## Consequences

- `LocalStackHost` is deleted; the in-process fallback no longer exists.
- Any PR that adds a prohibited project reference fails to compile.
- `IArtefactStore` and `IControlPlaneClient` cannot coexist in the same assembly.
- Adding a new interface to `Abstractions.Agent` makes it invisible to CLI code — by design.
- Developers who add a new module or connector must place interfaces in the correct `Abstractions.*` assembly.

## Related

- [docs/architecture.md](../architecture.md) — component boundaries
- [.agents/guardrails/architecture-boundaries.md](../../.agents/guardrails/architecture-boundaries.md) — enforced rules
- Driving spec: `specs/021.2-separation-of-concerns/spec.md`

# Architecture Update: agent_runtime_context

## 1. Current Architecture Narrative

`agent_runtime_context` is the runtime bridge between queued job payloads and infrastructure services that need to know the active package configuration, job mode, package path, and source/target endpoints. `PackageConfigStore` persists and reads `migration-config.json` through `IArtefactStore`. `AgentJobContext` carries the immutable per-job mode, package path, and config version. Singleton current-context accessors publish the active job values while a job is running and clear those values when the job completes.

## 2. Required Documentation Changes

- Update `.agents/context/architecture/agent-runtime-context.md` to state that package-path absoluteness is validated independently of the host OS.
- Document that current-context accessors are singleton holders with explicit `Set` and `Clear` lifecycle semantics.
- Document that source and target endpoint clearing are independent operations.

## 3. Boundary Clarifications

- `AgentJobContext` is a domain/runtime context object, not a filesystem adapter; its package-path validation must accept the supported package URI/path shapes regardless of the OS running the agent tests.
- `CurrentPackageConfigAccessor` and `CurrentAgentJobContextAccessor` hold one nullable active value and reject null in `Set`.
- `CurrentJobEndpointAccessor` holds source and target independently, allowing phase-specific cleanup without removing the other endpoint.
- Active wrapper classes should continue to expose empty values when no current value exists; this avoids stale singleton state between jobs.

## 4. Testability Seams

No new production testability seam is required. The existing public accessor methods are sufficient for direct unit tests. `AgentJobContext` path validation remains private implementation detail, tested through the constructor/init contract.

## 5. Risks and Follow-Up

- `JobAgentWorkerDispatchTests.cs` remains excluded from active test compilation and should be reviewed in a separate worker orchestration safety-net pass.
- If future package URI schemes are added beyond filesystem paths, `AgentJobContext` should either explicitly support those schemes or move URI validation into a dedicated value object.

# Orchestration

## Job Engine

The **Job Engine** is the shared execution core used by Migration Agents in all hosting topologies. It receives a `MigrationJob`, resolves the execution plan, and runs modules in dependency order. It has no knowledge of the TUI, the console, or any progress renderer.

See [docs/cli.md](cli.md) for how the CLI routes a job to the Job Engine. See [.agents/context/job-contract.md](../.agents/context/job-contract.md) for the `MigrationJob` schema.

### Steps

1. **Validate job** — Check `MigrationJob` schema, `configVersion` compatibility, and `guardrails` values.
2. **Validate package** — Run each module's `ValidateAsync` (pre-execution pass). Fail fast on errors.
3. **Build module dependency graph** — Topological sort of all enabled modules using `DependsOn` declarations. Fail fast on circular dependencies.
4. **Execute modules in order** — Run each module's `ExportAsync`, `ImportAsync`, or both, depending on `mode`.
5. **Maintain state via cursors** — Each module writes its cursor after each unit of work via `IStateStore`.
6. **Emit progress events** — After each cursor write, emit a `ProgressEvent` to `IProgressSink`.
7. **Fail fast on module failure** — A non-recoverable error in any module halts the run. Cursor state allows resume.

### Mode Behaviour

#### Export Mode

```
Validate job → Build graph → ExportAsync (each module) → Done
```

- Only `source` connection is required.
- `target` is ignored.
- Package is written to the URI in `artefacts.packageUri`.

#### Import Mode

```
Validate job → Build graph → ImportAsync (each module) → Done
```

- Only `target` connection is required.
- `source` is ignored.
- Package is read from the URI in `artefacts.packageUri`.

#### Both Mode

```
Validate job → Build graph → ExportAsync → Validate package → ImportAsync → Done
```

- Both `source` and `target` connections are required.
- A validation step runs between export and import to catch any package integrity issues before the import begins.

### Validate Step Placement

- Config validation runs **before** any module executes (fail fast on bad config).
- Package validation in `Both` mode runs **after** all exports complete and **before** any imports begin.
- Each module's `ValidateAsync` is called as part of the pre-execution validation pass.

### Fail-Fast Rules

- Any module that throws an unhandled exception fails the run immediately.
- Partial progress is preserved via cursors; the run is resumable.
- Retryable errors (network timeouts, rate limits) are handled within modules using the `policies.retries` configuration; they do not trigger fail-fast.
- Non-retryable errors (schema mismatch, missing required fields, auth failure) trigger fail-fast immediately.

---

## Execution Contexts

The orchestrator runs in the same way regardless of execution context. The context determines where the package lives and how progress is reported, not how modules execute.

### Local / Server Hosted

- The CLI uses embedded Aspire `DistributedApplication` APIs to start `ControlPlaneHost`, `MigrationAgent`(s), and PostgreSQL on the local machine. All components communicate over HTTP (`http://localhost:5100`).
- `IArtefactStore` is backed by the local filesystem (`FileSystemArtefactStore`).
- Progress is consumed by all three sinks simultaneously: `ConsoleProgressSink`, `PackageProgressSink`, and `ControlPlaneProgressSink` (enables live TUI streaming via the control plane).
- Any machine with network access to the host can attach a TUI via the control plane HTTP endpoint.

See [docs/cli.md](cli.md) for local and server command details.

### Agent (Cloud)

- A Migration Agent calls the Job Engine after receiving a leased `MigrationJob` from the remote control plane.
- `IArtefactStore` is backed by the shared artefact store (`AzureBlobArtefactStore` or equivalent).
- Progress is consumed by `ControlPlaneProgressSink`, which pushes events to the control plane.
- The control plane's progress view mirrors the cursor; the cursor in the package remains authoritative for resume.

### What Does Not Change Between Contexts

- The orchestrator runner logic (steps 1–6 above).
- Module code.
- Cursor schema and resume behaviour.
- Package layout.
- Fail-fast rules.

# Orchestration

## Job Engine

The **Job Engine** is the shared execution core used by Migration Agents in all hosting topologies. It receives a `Job`, resolves the execution plan, and runs modules in dependency order. It has no knowledge of the TUI, the console, or any progress renderer.

See [docs/cli.md](cli.md) for how the CLI routes a job to the Job Engine. See [.agents/context/job-contract.md](../.agents/context/job-contract.md) for the `Job` wire format.

### Steps

1. **Validate job** — Check `Job` schema, `configVersion` compatibility, and `kind` value.
2. **Validate package** — Run each module's `ValidateAsync` (pre-execution pass). Fail fast on errors.
3. **Build module dependency graph** — Topological sort of all enabled modules using `DependsOn` declarations. Fail fast on circular dependencies.
4. **Execute modules in order** — Run each module's `ExportAsync`, `PrepareAsync`, `ImportAsync`, or a combination, depending on `mode`.
5. **Maintain state via cursors** — Each module writes its cursor after each unit of work via `IStateStore`.
6. **Emit progress events** — After each cursor write, emit a `ProgressEvent` to `IProgressSink`.
6a. **Record metrics** — After each work item processing step, record OTel metrics via `IMigrationMetrics` (execution counters, payload histograms, duration).
7. **Fail fast on module failure** — A non-recoverable error in any module halts the run. Cursor state allows resume.

### Mode Behaviour

#### Inventory Mode

```
Validate job → Build graph → ExportAsync (inventory-capable modules only) → Done
```

- Only `source` connection is required.
- `target` is ignored.
- Enumerates and catalogues in-scope items (work items, revisions, artefacts) per project.
- Results written to the package as inventory artefacts.
- Writes `.migration/Checkpoints/inventory.complete.json` on completion.

#### Export Mode

```
Validate job → Check Inventory gate → Build graph → ExportAsync (each module) → Done
```

- Only `source` connection is required.
- `target` is ignored.
- Package is written to the URI in `artefacts.packageUri`.
- **Inventory gate**: Before building the module graph, the orchestrator checks for `.migration/Checkpoints/inventory.complete.json`. If the marker is absent, the orchestrator **auto-runs Inventory** (runs inventory-capable modules). This ensures export always has inventory data available.

#### Prepare Mode

```
Validate job → Validate package → Build graph → PrepareAsync (each module) → Write prepare.complete.json → Done
```

- Requires a completed Export (package must exist with `manifest.json`).
- Only `target` connection is required (reads the package, queries the target).
- `source` is ignored.
- Each module's `PrepareAsync` reads exported artefacts from the package, queries the target system, and writes validation/mapping artefacts into the module's own folder (e.g. `Identities/prepare-report.json`, `Nodes/prepare-report.json`).
- On successful completion, writes `.migration/Checkpoints/prepare.complete.json` as the completion marker.
- Prepare is **idempotent** — re-running overwrites Prepare output artefacts but never modifies operator-edited mapping files (e.g. `Identities/mapping.json`).
- Any unresolved issue (unmapped identity, missing node, unmapped field) is a **blocking error** unless the operator has added an explicit skip annotation to the relevant mapping file.

#### Import Mode

```
Validate job → Check Prepare gate → Build graph → ImportAsync (each module) → Done
```

- Only `target` connection is required.
- `source` is ignored.
- Package is read from the URI in `artefacts.packageUri`.
- **Prepare gate**: Before building the module graph, the orchestrator checks for `.migration/Checkpoints/prepare.complete.json`. If the marker is absent, the orchestrator **auto-runs Prepare** (runs `PrepareAsync` for each module). If Prepare produces any blocking issues, the orchestrator **aborts** with a diagnostic report and does not proceed to Import. The operator must resolve the issues and re-run Import (or run `prepare` explicitly).

#### Validate Mode

```
Validate job → Validate package → Build graph → ValidateAsync (each module) → Write validation-report.json → Done
```

- Only `target` connection and the package are required.
- `source` is ignored.
- Compares the import results against the exported package data.
- Runs Tier 3 post-flight checks: work item count parity, link integrity, attachment integrity, identity resolution completeness.
- Writes `.migration/Logs/validation-report.json` with comprehensive results.
- Writes `.migration/Checkpoints/validate.complete.json` on completion.
- Can be re-run independently at any time after import.

#### Migrate Mode

```
Validate job → Build graph → Inventory → ExportAsync → Validate package → PrepareAsync (each module) → Check for blocking issues → ImportAsync (each module) → ValidateAsync (each module) → Done
```

- Both `source` and `target` connections are required.
- Runs all five phases in a single orchestrated run: Inventory → Export → Prepare → Import → Validate.
- If Prepare produces any blocking issues, the orchestrator **aborts** after Prepare and does not proceed to Import. The operator must resolve the issues and re-run (with `mode: Import` to skip re-export, or `mode: Migrate` to repeat the full cycle).

### Phase Gates

Each phase can be run independently, but downstream phases auto-run their prerequisites if the prerequisite's completion marker is absent:

| Phase | Gate | Marker checked | Auto-runs |
|---|---|---|---|
| **Export** | Inventory gate | `.migration/Checkpoints/inventory.complete.json` | Inventory |
| **Import** | Prepare gate | `.migration/Checkpoints/prepare.complete.json` | Prepare (aborts on blocking issues) |

These gates ensure the pipeline is self-healing: running Export alone will also produce inventory data; running Import alone will also run Prepare first.

### Validate Step Placement

- Config validation runs **before** any module executes (fail fast on bad config).
- Inventory gate runs **before** Export in `Export` mode.
- Package validation in `Migrate` mode runs **after** all exports complete and **before** Prepare begins.
- Each module's `ValidateAsync` is called as part of the Validate phase or the pre-execution validation pass.
- Prepare gate runs **before** any import in `Import` mode and **after** all Prepare modules complete in `Migrate` mode.

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

- A Migration Agent calls the Job Engine after receiving a leased `Job` from the remote control plane.
- `IArtefactStore` is backed by the shared artefact store (`AzureBlobArtefactStore` or equivalent).
- Progress is consumed by `ControlPlaneProgressSink`, which pushes events to the control plane.
- The control plane's progress view mirrors the cursor; the cursor in the package remains authoritative for resume.

### What Does Not Change Between Contexts

- The orchestrator runner logic (steps 1–6 above).
- Module code.
- Cursor schema and resume behaviour.
- Package layout.
- Fail-fast rules.

# Orchestration

## 11. Orchestration

The top-level runner coordinates all modules according to the configured mode.

### Runner Steps

1. **Load config** — Read and deserialise the configuration file.
2. **Validate schema** — Run the config upgrader if needed; validate `configVersion` compatibility.
3. **Build module dependency graph** — Topological sort of all enabled modules using `DependsOn` declarations. Fail fast on circular dependencies.
4. **Execute modules in order** — Run each module's `ExportAsync`, `ImportAsync`, or both, depending on mode.
5. **Maintain state via cursors** — Each module writes its cursor after each unit of work.
6. **Fail fast on module failure** — A non-recoverable error in any module halts the run. The cursor state allows resume.

### Mode Behaviour

#### Export Mode

```
Load config → Validate → Build graph → ExportAsync (each module) → Done
```

- Only `source` connection is required.
- `target` is ignored.
- Package is written to `artefacts.path`.

#### Import Mode

```
Load config → Validate → Build graph → ImportAsync (each module) → Done
```

- Only `target` connection is required.
- `source` is ignored.
- Package is read from `artefacts.path`.

#### Both Mode

```
Load config → Validate → Build graph → ExportAsync → Validate package → ImportAsync → Done
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

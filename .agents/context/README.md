# Agent Context

This folder contains compressed system context for Agents/AI.

Context files explain the system, terminology, concepts, and settled decisions in a low-token form. They help agents reason correctly before making changes.

Context does not override guardrails. If context conflicts with guardrails, the guardrail wins. If context conflicts with canonical documentation or ADRs, the context should be updated.

## Files

| File | Purpose |
| --- | --- |
| `product-vision.md` | What the platform is, what it is not, supported sources and targets |
| `domain-model.md` | Core domain concepts: Package, Job, Phase, Module, Connector, etc. |
| `terminology.md` | Canonical terms and terms to avoid |
| `architecture/readme.md` | Compressed component map, data flow, telemetry channels, and Migration Agent subsystem index |
| `architecture/agent-package-boundary.md` | Typed package boundary above raw persistence: authoritative metadata, run-audit mirroring, and run-log routing |
| `pipeline-phases.md` | Five phases and Migrate mode — what each does, outputs, rules |
| `migration-package-concept.md` | Package layout specification — what the package is, structure, paths |
| `package-format-summary.md` | Short package format reference for agents |
| `workitems-format-summary.md` | WorkItems folder layout — chronological revision structure |
| `import-streaming.md` | Streaming import requirements — memory-safe enumeration |
| `checkpointing-summary.md` | Cursor-based checkpointing — resumability, idempotency |
| `artefact-store.md` | IArtefactStore abstraction — the only permitted persistence interface |
| `job-lifecycle.md` | Job contract specification — what a Job is, fields, lifecycle |
| `telemetry-model.md` | Telemetry layer model — spans, metrics, logs, progress events |
| `identity-and-mapping.md` | Identity mapping service — how source identities map to target |
| `control-plane-concept.md` | Control Plane — what it does and does NOT do |
| `module-model.md` | Module model — boundaries, execution shape, test expectations |
| `connector-model.md` | Connector model — three variants, boundaries, rules |
| `configuration-model.md` | Configuration model — structure, auth, key sections |
| `entitlements-model.md` | Entitlements — admission checks, lease model, module ignorance |
| `decision-records-summary.md` | ADR summaries and current implications |
| `data-classification-summary.md` | Data classification — what is Customer vs System vs Derived data |
| `cli-commands.md` | Canonical CLI commands reference — all commands, options, contract |
| `ui-mode-summary.md` | Compressed CLI/TUI mode-to-view contract summary |

# Architecture Decision Records

This folder contains Architecture Decision Records (ADRs) for the Azure DevOps Migration Platform.

Each ADR records an important architectural decision: what was decided, why, what alternatives were considered, and what the consequences are.

## Status Values

- **Accepted** — the decision is in effect
- **Superseded by XXXX** — replaced by a later ADR
- **Deprecated** — no longer applies

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-source-files-target.md) | Source → Files → Target architecture | Accepted |
| [0002](0002-filesystem-package-as-source-of-truth.md) | Filesystem package as source of truth | Accepted |
| [0003](0003-cursor-based-checkpointing.md) | Cursor-based checkpointing | Accepted — amended by ADR-0010 |
| [0004](0004-control-plane-does-not-execute-migrations.md) | Control Plane does not execute migrations | Accepted |
| [0005](0005-agent-only-package-write-access.md) | Agent-only package write access | Accepted — amended by ADR-0008 |
| [0006](0006-three-channel-observability.md) | Three-channel observability | Accepted — amended by iron-comms (Phases A–E, 2026-06-30); transport superseded by ADR-0020 |
| [0007](0007-compiler-enforced-project-boundaries.md) | Compiler-enforced project boundary topology | Accepted |
| [0008](0008-configuration-travels-in-package.md) | Configuration travels in the package | Accepted |
| [0009](0009-single-job-kind-discriminator.md) | Single Job class with Kind discriminator | Accepted |
| [0010](0010-plan-driven-dag-execution.md) | Plan-driven DAG execution | Accepted — amended by iron-comms (2026-07-01) |
| [0011](0011-unified-platform-metric-namespace.md) | Unified `platform.*` metric namespace | Accepted |
| [0012](0012-imodule-five-phase-contract.md) | IModule five-phase contract | Accepted |
| [0013](0013-simulated-connector-as-ci-infrastructure.md) | Simulated connector as first-class CI infrastructure | Accepted |
| [0014](0014-icapture-unified-capture-contract.md) | ICapture: Unified capture contract | Accepted — amends ADR-0012 |
| [0015](0015-mode-driven-cli-and-tui-ui-contract.md) | Mode-driven CLI and TUI UI contract | Accepted |
| [0016](0016-unified-package-access.md) | Unified package access | Accepted |
| [0017](0017-capability-seam-ethos-and-tdd-architecture-governance.md) | Capability seam ethos and TDD architecture governance | Accepted |
| [0018](0018-compatibility-only-guard-clauses.md) | Compatibility-only guard clauses | Accepted |
| [0019](0019-workitems-extension-seam-and-staged-cursor-pipeline.md) | WorkItems extension seam and staged cursor pipeline | Accepted |
| [0020](0020-unified-worker-event-channel.md) | Unified worker-event channel | Accepted — amends ADR-0006 and ADR-0010 |
| [0021](0021-four-tier-validation-model.md) | Four-tier validation model | Accepted |
| [0022](0022-host-composition-roots-own-storage-selection.md) | Host composition roots own storage selection; modules depend only on storage contracts | Accepted — executes MM-C1, MM-H1, CA-C2 |
| [0023](0023-promote-hidden-seams-to-abstractions-ports.md) | Promote hidden cross-slice seams to canonical Abstractions ports | Accepted — executes CA-C1, CA-H1/HX-M1, VS-H1, VS-H2, VS-H3, VS-M3 |

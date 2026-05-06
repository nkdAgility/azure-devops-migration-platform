# Architecture Decision Records

This folder contains Architecture Decision Records (ADRs) for the Azure DevOps Migration Platform.

Each ADR records an important architectural decision: what was decided, why, what alternatives were considered, and what the consequences are.

## Status Values

- **Accepted** — the decision is in effect
- **Superseded by XXXX** — replaced by a later ADR
- **Deprecated** — no longer applies

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-source-files-target.md) | Source → Files → Target architecture | Accepted |
| [0002](0002-filesystem-package-as-source-of-truth.md) | Filesystem package as source of truth | Accepted |
| [0003](0003-cursor-based-checkpointing.md) | Cursor-based checkpointing | Accepted — amended by ADR-0010 |
| [0004](0004-control-plane-does-not-execute-migrations.md) | Control Plane does not execute migrations | Accepted |
| [0005](0005-agent-only-package-write-access.md) | Agent-only package write access | Accepted — amended by ADR-0008 |
| [0006](0006-three-channel-observability.md) | Three-channel observability | Accepted |
| [0007](0007-compiler-enforced-project-boundaries.md) | Compiler-enforced project boundary topology | Accepted |
| [0008](0008-configuration-travels-in-package.md) | Configuration travels in the package | Accepted |
| [0009](0009-single-job-kind-discriminator.md) | Single Job class with Kind discriminator | Accepted |
| [0010](0010-plan-driven-dag-execution.md) | Plan-driven DAG execution | Accepted |
| [0011](0011-unified-platform-metric-namespace.md) | Unified `platform.*` metric namespace | Accepted |
| [0012](0012-imodule-five-phase-contract.md) | IModule five-phase contract | Accepted |
| [0013](0013-simulated-connector-as-ci-infrastructure.md) | Simulated connector as first-class CI infrastructure | Accepted |
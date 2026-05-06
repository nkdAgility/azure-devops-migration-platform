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
| [0003](0003-cursor-based-checkpointing.md) | Cursor-based checkpointing | Accepted |
| [0004](0004-control-plane-does-not-execute-migrations.md) | Control Plane does not execute migrations | Accepted |
| [0005](0005-agent-only-package-write-access.md) | Agent-only package write access | Accepted |
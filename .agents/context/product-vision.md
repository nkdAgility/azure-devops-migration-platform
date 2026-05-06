# Product Vision

The Azure DevOps Migration Platform is a deterministic, resumable, auditable migration package platform.

## Core Model

```
Source → Files (package) → Target
```

All migration data moves through an intermediate filesystem package. Source and Target never communicate directly.

## What It Is

- A migration package platform — the package is the source of truth.
- Deterministic — the same inputs produce the same package every time.
- Resumable — interrupted migrations resume from the cursor in the package, not from scratch.
- Auditable — every exported item is inspectable before import.
- Portable — the package can be moved between machines and environments.
- Phased — Inventory, Export, Prepare, Import, Validate run independently.

## What It Is Not

- Not a direct migration utility (no Source → Target).
- Not a synchronisation tool (one-way, one-time migration).
- Not an online transformation service (package is offline, file-based).

## Deployment Modes

| Mode | Description |
|---|---|
| Standalone | CLI starts Control Plane and agent locally on the operator machine |
| Self-hosted | Operator deploys Control Plane and agents on their own infrastructure |
| Hosted | nkdAgility-managed Control Plane; operator runs agents in their environment |

## Supported Sources

- Azure DevOps Services (cloud)
- Azure DevOps Server (on-premises, formerly TFS)
- Team Foundation Server (legacy TFS, via TFS Export Agent, net481)

## Supported Targets

- Azure DevOps Services (cloud)
- Azure DevOps Server (on-premises)
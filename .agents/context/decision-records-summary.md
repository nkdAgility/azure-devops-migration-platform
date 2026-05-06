# Decision Records Summary

Compressed summary of Architecture Decision Records (ADRs). Full records in `docs/adr/`.

## ADR 0001 — Source → Files → Target

**Status:** Accepted

All migration data flows through the filesystem package. Source and Target never communicate directly. Export writes to the package. Import reads from the package.

**Current implication:** Never route data directly from source to target. Every module must write to `IArtefactStore` on export and read from `IArtefactStore` on import.

## ADR 0002 — Filesystem Package as Source of Truth

**Status:** Accepted

The package is the single source of truth. No external databases or memory structures are authoritative for migration state.

**Current implication:** All persistent state goes through `IArtefactStore` (artefacts) or `IStateStore` (transient state). Both are backed by the package working directory.

## ADR 0003 — Cursor-Based Checkpointing

**Status:** Accepted

Checkpoints are cursor strings (the last successfully processed artefact store path). No count-based progress tracking.

**Current implication:** No watermark tables, no in-memory counts as resume state. Resume = seek to cursor in `EnumerateAsync`.

## ADR 0004 — Control Plane Does Not Execute Migrations

**Status:** Accepted

The Control Plane coordinates but never executes migration phases. Migration logic runs exclusively in agents.

**Current implication:** No migration method calls in Control Plane code. No package writes from Control Plane.

## ADR 0005 — Agent-Only Package Write Access

**Status:** Accepted

Only Migration Agent and TFS Export Agent may write to the package. CLI, TUI, Control Plane, and ControlPlaneHost are read-only.

**Current implication:** Reject any code that calls `IArtefactStore` write methods from CLI, TUI, or Control Plane code.
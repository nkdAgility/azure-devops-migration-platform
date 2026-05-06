# Domain Model

Core domain concepts used throughout the system.

## Migration Package

An intermediary directory tree that holds all exported data. It is the source of truth for the migration. Located at `Package.WorkingDirectory`.

## Job

A unit of work submitted to the Control Plane. A Job has a `Mode`, a `Config`, and a lifecycle (Queued → Running → Completed | Failed).

## Phase

A discrete step in the migration process: Inventory, Export, Prepare, Import, Validate. Migrate is a convenience mode that chains all five.

## Module

A self-contained unit of migration logic for a specific data type (e.g. WorkItems, Teams, Nodes). Modules implement `IModule` and are the only extension point.

## Connector

An adapter between the platform and an external system. Source connectors enumerate data. Target connectors accept data. All connectors have Simulated, AzureDevOps, and TFS variants.

## Source / Target

The system being migrated from (Source) and the system being migrated to (Target). Source and Target never communicate directly — all data flows through the package.

## Artefact Store

The persistence abstraction for package data. Implemented as a local filesystem store or Azure Blob Storage store. All modules must use `IArtefactStore` — no direct filesystem access.

## State Store

The persistence abstraction for transient module state. Backed by SQLite in the package. All modules must use `IStateStore` — no direct database access.

## Checkpoint

A cursor-based durable progress marker. Stored in `.migration/Checkpoints/`. Each module has its own checkpoint file per phase.

## Cursor

A string key representing the last successfully processed item. Equal to the artefact store path of that item (e.g. `WorkItems/2026-02-25/638760123456789012-12345-17`).

## Telemetry

The observability layer: activity spans (O-1), metrics (O-2), structured logs (O-3), progress events (O-4).

## Entitlement

A snapshot of the licence and usage rights associated with a job. Enforced at admission, lease renewal, and unit-of-work boundaries by the Control Plane.

## Control Plane

A coordination service that manages job admission, leasing, telemetry, and progress. It does not execute migration logic.

## Agent

A worker process that leases jobs from the Control Plane and executes phases. The Migration Agent targets .NET 10. The TFS Export Agent targets net481 (Windows only).
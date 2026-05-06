# ADR 0009 — Single Job Class with Kind Discriminator

## Status

Accepted

## Context

The platform originally had two separate job classes: `MigrationJob` (for export/import/migrate) and `DiscoveryJob` (for inventory/dependency operations). Both types travelled the same CLI → Control Plane → Agent pipeline but were deserialized differently, stored differently, and dispatched differently. Adding a new job kind required changes in at least five places: the job class, the Control Plane store, the Control Plane lease dispatcher, the Agent bootstrap, and the CLI submission path.

Additionally, `DiscoveryJob` contained a `Connectors` concept implemented as a different shape to `MigrationJob`, making cross-kind logic inconsistent.

## Decision

A single `Job` record replaces `MigrationJob` and `DiscoveryJob`. All job kinds travel the same wire format.

**`Job` shape:**

```
Job
  ├── JobId          : string          (UUID v4, CLI-assigned)
  ├── ConfigVersion  : string          ("2.0")
  ├── Kind           : JobKind         (Export | Import | Migrate | Prepare | Inventory | Dependencies)
  ├── Connectors     : ConnectorType[] (e.g. [AzureDevOps], [TeamFoundationServer], [] = Simulated)
  ├── Package        : JobPackage      (packageUri, createPackage)
  ├── Diagnostics    : JobDiagnostics? (minimumLevel)
  ├── Resume         : JobResume?      (Auto | ForceFresh)
  └── ConfigPayload  : string?         (raw JSON of migration-config.json — see ADR-0008)
```

`Job.Kind` is the dispatch discriminator. The Control Plane stores `Job` directly. Agents switch on `job.Kind` in a single dispatch method. `Job.Connectors` replaces per-type source-type strings and drives capability matching between the Control Plane and available agents.

## Alternatives Considered

**Keep separate job classes with a common base**: Less change initially, but the class hierarchy grows with every new job kind and the serialization discriminator problem recurs. The root cause (five-place changes for a new kind) is not fixed.

**Use a generic `Dictionary<string, object>` payload**: Avoids a class hierarchy but loses compile-time type safety on the common fields and makes contract evolution harder to version.

**Polymorphic serialization with `[JsonDerivedType]`**: Works in .NET 7+, but keeps separate classes alive and requires the Control Plane to know every derived type — the problem reappears as a registration problem.

## Consequences

- `MigrationJob` and `DiscoveryJob` are deleted from the codebase.
- Every component that previously switched on `MigrationJob` vs `DiscoveryJob` switches on `Job.Kind` instead.
- Adding a new job kind (`JobKind.Validate`, for example) requires only: adding an enum value, adding an Agent dispatch case, and optionally adding a CLI command — no structural change to the job model.
- `Job.ConfigPayload` carries the raw JSON of `migration-config.json` as a transport mechanism (see ADR-0008). The Agent materialises it to disk before reading it.
- `Job` is defined in `DevOpsMigrationPlatform.Abstractions.Jobs` — visible to all components.

## Related

- [ADR-0008](0008-configuration-travels-in-package.md) — config payload in Job
- [.agents/context/job-lifecycle.md](../../.agents/context/job-lifecycle.md) — job lifecycle model
- Driving spec: `specs/025.1-fold-to-job/spec.md`

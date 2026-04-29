# Interface Contract: IPackageConfigStore

**Phase 1 output** for [plan.md](../plan.md)

---

## Overview

`IPackageConfigStore` is the abstraction for reading and writing the `migration-config.json` file that carries the full `MigrationOptions` from the CLI to the agent via the package.

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Storage`
**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`
**Implementation**: `DevOpsMigrationPlatform.Infrastructure.Agent.Storage.PackageConfigStore`

---

## Interface Definition

```csharp
/// <summary>
/// Reads and writes the per-job migration configuration file (<c>migration-config.json</c>)
/// at the package root.
/// </summary>
/// <remarks>
/// The CLI calls <see cref="WriteAsync"/> before submitting the job.
/// The agent calls <see cref="ReadAsync"/> after opening the package store.
/// See <see cref="PackagePaths.MigrationConfigFileName"/> for the well-known path.
/// </remarks>
public interface IPackageConfigStore
{
    /// <summary>
    /// Serialises <paramref name="options"/> as JSON and writes it to
    /// <c>migration-config.json</c> at the root of <paramref name="artefactStore"/>.
    /// </summary>
    /// <param name="artefactStore">Package store to write into. Must be writable.</param>
    /// <param name="options">
    /// The fully resolved <see cref="MigrationOptions"/> including source, target,
    /// credentials, modules, policies, and tools.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <c>migration-config.json</c> already exists in the package root
    /// (FR-012 — must not silently overwrite an existing config).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task WriteAsync(
        IArtefactStore artefactStore,
        MigrationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>migration-config.json</c> from <paramref name="artefactStore"/> and returns
    /// an <see cref="IConfiguration"/> built from its contents.
    /// </summary>
    /// <param name="artefactStore">Package store to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IConfiguration"/> whose root contains the full JSON from
    /// <c>migration-config.json</c>. Callers bind <c>IOptions&lt;T&gt;</c> from
    /// <c>configuration.GetSection(MigrationOptions.SectionName)</c> and nested sections.
    /// </returns>
    /// <exception cref="PackageConfigNotFoundException">
    /// Thrown if <c>migration-config.json</c> is absent from the package root.
    /// The agent must fail the job and instruct the operator to re-submit.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown if the file exists but cannot be parsed as valid JSON.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task<IConfiguration> ReadAsync(
        IArtefactStore artefactStore,
        CancellationToken cancellationToken = default);
}
```

---

## Caller Contracts

### WriteAsync — CLI (`QueueCommand`)

Called once per job submission, after resolving `outputPath` and before building `MigrationJob`:

```csharp
// Preconditions:
//   - artefactStore is writable
//   - options is fully resolved (no unexpanded environment variables)
//   - migration-config.json does NOT already exist (FR-012)
// Postconditions:
//   - migration-config.json exists at package root
//   - File contains valid JSON matching MigrationOptions POCO
//   - Credential values are not logged
await _packageConfigStore.WriteAsync(artefactStore, config, cancellationToken);
```

### ReadAsync — Agent (`ModulePipelineWorkerBase`, `TfsJobAgentWorker`)

Called once per job start, after opening the package store and before executing modules:

```csharp
// Preconditions:
//   - artefactStore is opened for the package URI in the job
//   - migration-config.json MUST exist (fail-fast if absent)
// Postconditions:
//   - Returns IConfiguration with root key "MigrationPlatform"
//   - IConfiguration can be used with .Bind<T>() or .GetSection()
var config = await _packageConfigStore.ReadAsync(artefactStore, cancellationToken);
var migrOpts = config.GetSection(MigrationOptions.SectionName).Get<MigrationOptions>();
```

---

## Observability

| Operation | Span Name | On Enter | On Success | On Error |
|---|---|---|---|---|
| `WriteAsync` | `config.write` | `Information`: "Writing config to package {PackageUri}" | `Information`: "Config written to {PackageUri}" | `Error`: "Failed to write config: {ErrorMessage}" |
| `ReadAsync` | `config.read` | `Information`: "Reading config from package {PackageUri}" | `Information`: "Config loaded from {PackageUri}" | `Warning`: "migration-config.json not found in {PackageUri}"; `Error`: "Failed to parse config: {ErrorMessage}" |

**Credential redaction**: `AccessToken`, `Password`, and any value with key containing `Secret` or `Token` MUST NOT be logged. Log only the URI.

---

## Error Handling

| Scenario | Exception | Agent behaviour |
|---|---|---|
| File absent | `PackageConfigNotFoundException` | Fail job immediately; log structured `Error`; no retry |
| File unparseable | `JsonException` (or Newtonsoft equivalent) | Fail job immediately; log structured `Error`; no retry |
| Overwrite attempt | `InvalidOperationException` | CLI returns exit code 1; prompt operator to use `--force` (future feature) |
| Write failure | `IArtefactStore` exception | CLI returns exit code 1; job never submitted |

---

## DI Registration

```csharp
// In PackageConfigServiceCollectionExtensions.cs
public static IServiceCollection AddPackageConfigStore(this IServiceCollection services)
{
    services.AddSingleton<IPackageConfigStore, PackageConfigStore>();
    return services;
}
```

Called from:
- `MigrationAgent`: `MigrationAgentServiceExtensions.AddMigrationAgentServices()`
- `TfsMigrationAgent`: `TfsMigrationAgentServiceExtensions.AddTfsMigrationAgentServices()`
- `CLI.Migration`: `QueueCommand` host builder

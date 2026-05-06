# Configuration Rules

These rules are mandatory for all configuration-related code.

## Configuration Shape

1. All configuration must be bound through `IOptions<T>` interfaces. No `Configuration.GetValue<T>()` or `Configuration["key"]` calls in module or connector code.
2. Configuration classes must use `init`-only setters (immutable after construction).
3. All configuration classes must be registered with the schema generator so that `migration.schema.json` stays current.

## Environment and Modes

4. No environment-name branching in code (e.g. `if (env.IsProduction())`). All environment differences must be expressed through configuration.
5. `Environment.Type` values (`Standalone`, `Hosted`) control deployment topology. New topology values require a guardrail amendment.

## Source and Target Blocks

6. `Source.Type` must be a known registered source type. Adding a new source type requires updating the schema and `docs/capabilities-guide.md`.
7. `Target.Type` must be a known registered target type.
8. Authentication blocks must support at minimum: `Pat`, `Windows` (TFS), `ManagedIdentity`.

## Defaults

9. All configuration properties must have sensible defaults. Required fields must fail fast at startup with a clear validation message, not at runtime.
10. `ConfigVersion` must be checked at startup. An unknown version must fail with a clear upgrade message, not silently use defaults.

## Schema Updates

11. Every configuration schema change must produce a corresponding update to `migration.schema.json`.
12. Breaking configuration changes (renaming or removing properties) require a version bump and an upgrader that migrates old configs forward.
13. No undocumented configuration properties. Every property must appear in `docs/configuration-reference.md`.

## No Undocumented Options

14. CLI commands must not expose configuration options that are not documented in `docs/cli-guide.md` and `.agents/context/cli-commands.md`.

## Related

- [coding-standards.md](./coding-standards.md) — IOptions<T> pattern
- [docs/configuration-reference.md](../docs/configuration-reference.md) — full schema reference
- [docs/configuration-guide.md](../docs/configuration-guide.md) — operator configuration guide
# Research: Schema Generation from IOptions DI Registrations

**Feature**: 028-ioptions-schema-gen | **Phase**: 0 — Research

---

## Decision: NJsonSchema version and API

- **Decision**: Use `NJsonSchema` (latest stable ≥ 11.x) for both build-time schema generation and Tier 0 CLI validation.
- **Rationale**: NJsonSchema supports `net481` and `net10.0` in the same package. It generates `JsonSchema` objects from `Type` reflections via `JsonSchema.FromType<T>()`, preserves `[JsonPropertyName]` attributes, honours `[Required]`, and emits `additionalProperties: false` when `AllowAdditionalItems = false`. NJsonSchema's `IJsonSchemaValidator.Validate(string json, JsonSchema schema)` is the exact API needed for Tier 0 config validation.
- **Alternatives considered**: System.Text.Json schema generation (only available in .NET 9+ and does not support `net481`); Json.NET Schema (commercial licence); manual schema authoring (drifts from code).

## Decision: JSON Schema version ($schema)

- **Decision**: Emit `draft-07` (`"$schema": "http://json-schema.org/draft-07/schema#"`) in `migration.schema.json`.
- **Rationale**: VS Code's built-in JSON Language Server supports draft-07 fully. It recognises `$schema` in `json.schemas` workspace settings and provides IntelliSense, hover documentation, and validation for draft-07. Draft 2019-09 and 2020-12 have partial support in VS Code and require an additional language server extension. Draft-07 is also the default emitted by NJsonSchema, so no custom serialisation is needed.
- **Alternatives considered**: Draft 2020-12 — rejected (incomplete VS Code support); draft-04 — rejected (lacks `if/then/else`, `const`).

## Decision: MSBuild target for schema generation

- **Decision**: Use `AfterTargets="Build"` with `Condition="'$(TargetFramework)' == 'net10.0'"` and invoke `dotnet run` against the `SchemaGenerator` project. The schema is written to `$(OutDir)migration.schema.json`. A `<Content>` item with `CopyToOutputDirectory="PreserveNewest"` ensures it travels with the CLI binary in publish profiles.
- **Rationale**: `AfterTargets="Build"` runs after all project references are compiled, ensuring all connector assemblies are available. `dotnet run` is simple and does not require a pre-published binary. The `net10.0` condition prevents a double-run in multi-targeted builds (the generator itself is net10.0 only).
- **Alternatives considered**: `BeforeTargets="Publish"` only — rejected because it skips schema generation during development builds, which breaks the IntelliSense scenario; a separate build script — rejected because it requires manual coordination and breaks incremental builds.

## Decision: SchemaOptionsEntry multi-target compatibility

- **Decision**: `SchemaOptionsEntry` is placed in `DevOpsMigrationPlatform.Abstractions` which is `net481;net10.0`. The type uses only `System.Type` and `string` — no `net10.0`-only APIs. `Microsoft.Extensions.Options` is already referenced from `Abstractions` (used by `IValidateOptions<T>`). No additional package references are required.
- **Rationale**: TFS agent (net481) must compile against `Abstractions`. `SchemaOptionsEntry` is a pure data carrier — it is never resolved at runtime in the TFS agent (the agent does not build the DI container), but it must compile without `#if` guards so that connector assemblies multi-targeting `net481;net10.0` can register their entries unconditionally.
- **Alternatives considered**: `#if NET10_0_OR_GREATER` guards — rejected because they force every connector to mirror the guards, increasing maintenance cost and risk of omission.

## Decision: TfsMigrationAgent and ActiveJobConfigState

- **Decision**: `TfsMigrationAgent` does NOT use `ActiveJobConfigState` — confirmed by grep. The TFS agent hosts its own tool execution pipeline and does not run `WorkItemsModule`, `TeamsModule`, `NodesModule`, or `IdentitiesModule` directly. Therefore, the TFS agent is unaffected by the `ActiveJobConfigState` deletion.
- **Rationale**: Grep of `ActiveJobConfigState` across `src/DevOpsMigrationPlatform.TfsMigrationAgent/` returned zero matches.
- **Alternatives considered**: N/A.

## Decision: `additionalProperties: false` in generated schema

- **Decision**: The generated root schema emits `"additionalProperties": false`. Each nested object section also emits `"additionalProperties": false`. This enables VS Code to underline unknown keys and enables the Tier 0 NJsonSchema validator to reject them.
- **Rationale**: `NJsonSchema.JsonSchemaGeneratorSettings.AlwaysAllowAdditionalObjectProperties = false` is the default. Explicitly setting this to `false` at the root prevents modules from silently accepting typo'd keys. This is the enforcement mechanism behind FR-006b (unknown-key rejection at Tier 0) and SC-010.
- **Alternatives considered**: Emit without `additionalProperties` — rejected because VS Code would allow any unknown key silently; use `unevaluatedProperties: false` (draft 2020-12 only) — rejected per JSON Schema version decision above.

## Decision: CI semantic JSON diff for schema drift

- **Decision**: Use `git diff --exit-code migration.schema.json` after running the schema generator in CI. The schema file is committed to source control in `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` (source-controlled canonical copy). The MSBuild target regenerates it on build and overwrites the output directory copy. CI runs `dotnet build` then checks git diff.
- **Rationale**: Keeps the CI check simple — no `jq` dependency required. The schema is deterministic (same DI registrations → same output), so any change in options types will produce a diff. The committed file serves as the IntelliSense source for VS Code (via `.vscode/settings.json` `json.schemas`).
- **Alternatives considered**: Semantic JSON diff with `jq` — adds a CI tool dependency with no benefit since the schema is deterministic; NJsonSchema compare API — not publicly exposed for file-to-file comparison.

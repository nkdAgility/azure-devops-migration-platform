# Research: OrganisationEndpoint

**Feature**: 016-organisation-endpoint  
**Date**: 2026-04-17

## Research Task 1: Where do `Projects` go when `OrganisationEndpoint` drops them?

### Context

`DiscoveryJobOrganisation` currently carries `List<string> Projects`. The spec (FR-001) explicitly states `OrganisationEndpoint` MUST NOT carry project lists. But `Projects` is actively used:

- `InventoryService` and `DependencyDiscoveryService` read `Projects` to decide whether to enumerate all projects in the org or use an explicit list.
- The CLI copies `OrganisationEntry.Projects` into `DiscoveryJobOrganisation.Projects`.
- The factory copies them back into `OrganisationEntry.Projects`.

### Decision: Keep Projects on `DiscoveryJob` via a companion structure

`DiscoveryJob` will carry `OrganisationEndpoint` for connection context and a parallel `Projects` list. Two approaches:

**Option A — Flat parallel lists**: `DiscoveryJob.Organisations` is `List<OrganisationEndpoint>` and a separate `DiscoveryJob.OrganisationProjects` maps each org to its project list. Fragile — index-coupling.

**Option B — Wrapper record on `DiscoveryJob` only**: A `ScopedOrganisationEndpoint` record that pairs `OrganisationEndpoint` with `List<string> Projects`. `ApiVersion` moves into `OrganisationEndpoint` itself since it is a connection property, not a job-scope concern. Lives on `DiscoveryJob` only — service interfaces still accept `OrganisationEndpoint`.

**Selected: Option B.**

Rationale:
- `OrganisationEndpoint` stays clean (connection context + API version, no project lists).
- The `Projects` list is a job-scope concern, not a connection concern.
- `ApiVersion` is a connection property — it describes how you talk to the org, so it belongs on `OrganisationEndpoint`.
- The wrapper is local to the discovery job contract — it does not leak into service interfaces.
- The factory implementations that currently map `DiscoveryJobOrganisation` → `OrganisationEntry` will instead map `ScopedOrganisationEndpoint` → `OrganisationEntry`.

### Alternatives Rejected

- **Put `Projects` on `OrganisationEndpoint`**: Violates FR-001 and conflates connection with query scope.
- **Move `Projects` to a top-level `DiscoveryJob.Projects` list**: Only works for single-org jobs. Multi-org jobs (like `inventory-multi-org.json`) need per-org project lists.

---

## Research Task 2: JSON serialisation compatibility for scenario files

### Context

FR-009 requires scenario JSON files to continue deserialising. The scenario files bind to `OrganisationEntry` (config layer), not to `DiscoveryJobOrganisation` directly. The C# class rename does not affect JSON property names.

### Decision: No JSON changes required for config-layer scenarios

The scenario JSON files use property names (`Type`, `Url`, `Projects`, `Authentication.Type`, `Authentication.AccessToken`) that are stable regardless of the C# class name. Renaming `DiscoveryJobOrganisation` → `OrganisationEndpoint` and `DiscoveryJobAuthentication` → `OrganisationEndpointAuthentication` changes only the C# type name, not the serialised property names.

For `DiscoveryJob` JSON (the job contract layer), the `Organisations` array property name stays the same. The internal type is `DiscoveryJobOrganisationScope` which has the same property shape. No scenario file changes needed.

### Alternatives Rejected

- **Add `[JsonPropertyName]` attributes**: Unnecessary — property names already match.

---

## Research Task 3: `IAzureDevOpsClientFactory` — in scope or out of scope?

### Context

`IAzureDevOpsClientFactory` lives in `Infrastructure.AzureDevOps`, not in `Abstractions`. Its methods accept `(string url, string pat)`. Should it be updated?

### Decision: Update in a follow-on pass within the same feature

`IAzureDevOpsClientFactory` is an infrastructure-internal interface. FR-006 targets Abstractions-level interfaces. However, since the factory is the lowest-level consumer that actually builds `VssConnection` objects, updating it to accept `OrganisationEndpoint` would be consistent.

Update it as part of FR-007 (implementation updates). It's not an Abstractions-level interface, so it doesn't need to be listed in FR-006, but the implementation task will include it.

### Alternatives Rejected

- **Leave `IAzureDevOpsClientFactory` unchanged**: Would mean implementations still destructure `OrganisationEndpoint` into `(url, pat)` at the boundary. Better to push the type all the way down.

---

## Research Task 4: `DiscoveryJobAuthentication.Type` — currently hardcoded to "Pat"

### Context

`DiscoveryJobAuthentication.Type` is a `string` with default `"Pat"`. The config layer uses `AuthenticationType` (an enum: `None`, `Pat`, `Windows`). The new `OrganisationEndpointAuthentication` uses `AuthenticationType Type` (the enum).

### Decision: Use the `AuthenticationType` enum on `OrganisationEndpointAuthentication`

The string-based `Type` on `DiscoveryJobAuthentication` was a loose serialisation artefact. The enum is the correct domain type. `OrganisationEndpointAuthentication` uses `AuthenticationType Type` directly.

The CLI command mapping (currently `Type = "Pat"`) will change to `Type = AuthenticationType.Pat`.

For `DiscoveryJobOrganisationScope`, the `Authentication` property carries the raw serialisable form for the job contract. The conversion from scope to `OrganisationEndpoint` resolves the token and maps the auth type.

> **Note**: `DiscoveryJobOrganisationScope` was subsequently renamed to `ScopedOrganisationEndpoint`.

### Alternatives Rejected

- **Keep string-based auth type**: Loses type safety that the codebase already has via the enum.

---

## Research Task 5: Multi-targeting impact (net481)

### Context

`DevOpsMigrationPlatform.Abstractions` targets `net481;net10.0`. The new `OrganisationEndpoint` and `OrganisationEndpointAuthentication` records will live there.

### Decision: Use `class` with `init`-only properties instead of `record` syntax

C# records require at least C# 9 / .NET 5+. For net481 compatibility in the multi-targeted Abstractions project, the type should be a `sealed class` with `init`-only properties (same pattern as existing types like `DiscoveryJobOrganisation` and `EndpointAuthenticationOptions`). The spec says "immutable record" — the intent (immutability, value semantics) is preserved; the mechanism adapts to the multi-target constraint.

### Alternatives Rejected

- **Use `record` keyword**: Would break net481 compilation.
- **Move to a net10.0-only project**: Violates the architectural requirement that abstractions are shared across runtimes.

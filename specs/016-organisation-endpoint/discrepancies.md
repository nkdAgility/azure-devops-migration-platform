# Architecture Discrepancies

**Feature**: OrganisationEndpoint (016-organisation-endpoint)  
**Flagged by**: speckit.plan  
**Status**: Resolved

## Discrepancies

### `OrganisationEndpoint` not referenced in `docs/architecture.md`

- **Source doc**: `docs/architecture.md`
- **Issue**: The architecture doc describes the system components and data flow but does not mention `OrganisationEndpoint` as a canonical type. After this feature lands, it should be mentioned as the standard connection context type used across service interfaces.
- **Suggested update**: Add a brief note in the Abstractions section mentioning `OrganisationEndpoint` as the canonical endpoint type for org/collection connections.
- **Status**: ✓ Resolved in speckit.implement

### `DiscoveryJobOrganisation` referenced in `.agents/context/job-contract.md`

- **Source doc**: `.agents/context/job-contract.md`
- **Issue**: The job contract documentation may reference `DiscoveryJobOrganisation` by name. After the rename, the doc should reference `ScopedOrganisationEndpoint` and `OrganisationEndpoint`.
- **Suggested update**: Update any mentions of `DiscoveryJobOrganisation` to `ScopedOrganisationEndpoint` in the discovery job section.
- **Status**: N/A — file does not reference `DiscoveryJobOrganisation`

### Service interface signatures not documented

- **Source doc**: `docs/modules.md`, `docs/source-types.md`
- **Issue**: The service interface signature changes (from `(string url, string pat)` to `OrganisationEndpoint`) are not reflected in any doc. After this feature, docs should reflect that service interfaces accept `OrganisationEndpoint`.
- **Suggested update**: Add a note in the relevant doc sections about the `OrganisationEndpoint` parameter convention.
- **Status**: ✓ Resolved in speckit.implement — documented in `docs/architecture.md` under "OrganisationEndpoint — Canonical Connection Context"

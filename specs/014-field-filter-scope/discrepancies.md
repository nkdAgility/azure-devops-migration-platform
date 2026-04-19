# Architecture Discrepancies

**Feature**: Field Filter Scope for Work Items
**Flagged by**: speckit.specify / speckit.clarify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### `filter` scope type not documented for WorkItems module

- **Source doc**: `docs/configuration.md`
- **Section**: WorkItems Module — Scopes and Extensions table
- **Issue**: The spec introduces a `filter` scope type for the `WorkItems` module's `scopes` array. The doc currently lists only `scopes[wiql]` as the available scope type. The `filter` scope (`mode`, `field`, `pattern` parameters) is not documented.
- **Suggested update**: Add a row block to the WorkItems Module table documenting `scopes[filter].parameters.mode`, `scopes[filter].parameters.field`, and `scopes[filter].parameters.pattern`. Add performance guidance: prefer short reference-data fields; minimise filter count.
- **Task**: T027

---

### `scopes` array not present on `organisations[]` entries

- **Source doc**: `docs/configuration.md`
- **Section**: Full Schema and Top-Level Fields table
- **Issue**: The spec adds a `scopes` array to `OrganisationEntry` (supporting `wiql` and `filter` scope types). The doc's Full Schema and Top-Level Fields table describe `organisations[]` entries without a `scopes` property.
- **Suggested update**: Add `scopes` to the organisation entry in the Full Schema example and add a row to the Top-Level Fields table documenting the `wiql` and `filter` scope types at the organisation level.
- **Task**: T027

---

### `docs/modules.md` module responsibility table incomplete for filter scopes

- **Source doc**: `docs/modules.md`
- **Section**: Module Responsibilities table — `WorkItemsModule` row
- **Issue**: The `WorkItemsModule` responsibility description mentions "Accepts a `wiql` scope" but does not mention the `filter` scope type added by this feature.
- **Suggested update**: Extend the `WorkItemsModule` responsibility description to add: "Also accepts one or more `filter` scopes (with `mode`, `field`, and `pattern` parameters) to include or exclude work items by field value using a regex."
- **Task**: T028

---

### `WorkItemFieldFilterOptions` placeholder comment is stale

- **Source**: `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFieldFilterOptions.cs`
- **Issue**: Doc-comment says "Placeholder for feature 014; will be replaced when the full filter system lands." After this feature lands, the placeholder comment must be removed and replaced with accurate documentation of the `Regex` operator and the `include`/`exclude` mode pattern.
- **Suggested update**: Remove placeholder comment; add accurate doc describing how filter scopes are parsed into this type.
- **Task**: T005

---

### `FilterOperator` placeholder comment is stale

- **Source**: `src/DevOpsMigrationPlatform.Abstractions/Models/FilterOperator.cs`
- **Issue**: Doc-comment says "Placeholder for feature 014; will be replaced when the full filter system lands." The `Regex` operator value needs to be added and the comment updated.
- **Suggested update**: Add `Regex` value; remove placeholder comment.
- **Task**: T004


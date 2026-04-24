# Architecture Discrepancies

**Feature**: Work Item Field Transformation
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### 1. Configuration schema missing `tools[]` top-level section
- **Source doc**: `docs/configuration.md`
- **Section**: Full Schema (line ~10)
- **Issue**: The spec requires a `tools[]` array at the `MigrationPlatform` config root for declaring field transform sets. The current config schema does not include this section.
- **Suggested update**: Add a `tools` array to the Full Schema JSON example and a corresponding row to the Top-Level Fields table describing `tools` as "Optional. Ordered list of shared tool declarations. Each tool has `id`, `type`, and type-specific parameters. Extensions reference tools by `id` and may override selected values."

### 2. Module docs missing tool injection and override model
- **Source doc**: `docs/modules.md`
- **Section**: WorkItemsModule — ADO Export (line ~54)
- **Issue**: The spec defines a tool resolution model (tools declared at platform level, loaded by extensions via `ref`, with per-extension overrides). This model is described in `analysis/proposed-features.md` but not yet in `docs/modules.md`.
- **Suggested update**: Add a "Tool Resolution" subsection under Module Architecture explaining: tools are declared in `MigrationPlatform.tools[]`, extensions load tools by `ref` ID, and effective settings = platform defaults + extension overrides. Cross-reference `docs/configuration.md` for schema.

### 3. Architecture overview missing Tool concept
- **Source doc**: `docs/architecture.md`
- **Section**: Execution Model / Components and Responsibilities
- **Issue**: The architecture overview describes Modules and Extensions but does not mention Tools as a cross-cutting concept. The spec introduces `FieldTransformTool` as a shared service loaded by extensions.
- **Suggested update**: Add a brief paragraph in the Components section (or a new "Tools" subsection under Module Architecture) defining: "A Tool is a shared, cross-cutting service declared once at the MigrationPlatform config root. Extensions load tools by reference and may override selected values. Tools are pure transformations or lookup services — they perform no I/O."

### 4. Naming convention shift from "FieldMapping" to "FieldTransform"
- **Source doc**: `analysis/proposed-features.md`
- **Section**: M1 and T1
- **Issue**: The proposed-features document uses `FieldMappingTool` and `FieldMapping` as the type discriminator. The spec recommends renaming to `FieldTransformTool` with `Transform` suffix for Screaming Architecture clarity. If accepted, the proposed-features document should be updated for consistency.
- **Suggested update**: Replace `FieldMappingTool` with `FieldTransformTool`, `IFieldMappingTool` with `IFieldTransformTool`, and update all 14 map type names to use the recommended `*Transform` naming throughout M1 and T1 sections.

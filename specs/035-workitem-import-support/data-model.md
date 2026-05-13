# Data Model: Work Item Import Support

**Date**: 2026-05-13 | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

## Overview

This document defines the core entities, state transitions, and validation models required for the Work Item Import Support feature. All entities are immutable records with `init`-only properties per SOLID principles.

---

## Core Entities

### 1. WorkItemImportContext

**Purpose**: Mutable context for a single work item during import processing. Holds current state, resolved identities, translated paths, and field values.

```csharp
/// <summary>
/// Mutable context for a single work item during import processing.
/// Updated as each stage of revision replay completes.
/// </summary>
public class WorkItemImportContext
{
    /// <summary>
    /// Source work item ID (from exported package).
    /// </summary>
    public required int SourceWorkItemId { get; init; }

    /// <summary>
    /// Target work item ID (created during first revision, reused for later revisions).
    /// Null until CreatedOrUpdated stage completes for first revision.
    /// </summary>
    public int? TargetWorkItemId { get; set; }

    /// <summary>
    /// Current revision folder path relative to package root (e.g., "WorkItems/2026-05-13/123456789-42-1").
    /// Updated as each revision folder is processed.
    /// </summary>
    public required string CurrentRevisionFolder { get; set; }

    /// <summary>
    /// Current processing stage: CreatedOrUpdated, AppliedFields, AppliedLinks, UploadedAttachments, Completed.
    /// </summary>
    public required ImportStage CurrentStage { get; set; }

    /// <summary>
    /// Resolved identity mappings for this revision (keyed by source identity ID).
    /// Populated during identity resolution step; used during field application.
    /// </summary>
    public required IDictionary<string, TargetIdentity> ResolvedIdentities { get; init; }

    /// <summary>
    /// Translated area and iteration paths for this revision.
    /// Populated by NodeTranslationTool; used during node readiness and field application.
    /// </summary>
    public required TranslatedNodePaths TranslatedPaths { get; init; }

    /// <summary>
    /// Field values after translation and before transform.
    /// Dict[fieldName] → value as string or object depending on field type.
    /// </summary>
    public required IDictionary<string, object?> TranslatedFieldValues { get; init; }

    /// <summary>
    /// Field values after transform rules applied.
    /// Dict[fieldName] → transformed value (or same as TranslatedFieldValues if no transform).
    /// </summary>
    public IDictionary<string, object?> TransformedFieldValues { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    /// Attachment replay records for this revision.
    /// Maps source attachment ID → target attachment ID and metadata.
    /// Populated during UploadedAttachments stage.
    /// </summary>
    public required IList<AttachmentReplayRecord> AttachmentRecords { get; init; }

    /// <summary>
    /// Embedded image replay records for this revision.
    /// Maps source image ID → target image URL.
    /// Populated during UploadedAttachments stage (images and attachments in same stage).
    /// </summary>
    public required IList<EmbeddedImageReplayRecord> EmbeddedImageRecords { get; init; }

    /// <summary>
    /// Timestamp when this context was created (for telemetry).
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}

public enum ImportStage
{
    CreatedOrUpdated,
    AppliedFields,
    AppliedLinks,
    UploadedAttachments,
    Completed
}
```

**State Transitions**:
- Initial: `CreatedOrUpdated` (none complete).
- After work item create/update: `AppliedFields`.
- After field application: `AppliedLinks`.
- After link application: `UploadedAttachments`.
- After attachment/image upload: `Completed`.

---

### 2. ImportReadinessReport

**Purpose**: Immutable record of prepare phase findings. Written to package for operator review. Consumed by import to decide whether to proceed.

```csharp
/// <summary>
/// Immutable report of import readiness validation (produced by prepare phase).
/// Written to package as JSON; operator reviews and can add skip directives.
/// Import consults this report to decide whether to proceed.
/// </summary>
public sealed record ImportReadinessReport
{
    /// <summary>
    /// Timestamp when prepare phase completed.
    /// </summary>
    public required DateTime PreparedAt { get; init; }

    /// <summary>
    /// Tooling version that produced this report.
    /// </summary>
    public required string ToolVersion { get; init; }

    /// <summary>
    /// True if all blocking findings have been resolved (none remain) or explicitly skipped by operator.
    /// </summary>
    public required bool IsReadyForImport { get; init; }

    /// <summary>
    /// Summary: count of work items, revisions, attachments, embedded images in scope.
    /// </summary>
    public required ScopeSummary Scope { get; init; }

    /// <summary>
    /// Node readiness findings: required paths, created paths, missing paths, errors.
    /// </summary>
    public required NodeReadinessFinding[] NodeFindings { get; init; }

    /// <summary>
    /// Work item type compatibility findings: found types, missing types, errors.
    /// </summary>
    public required WorkItemTypeFinding[] TypeFindings { get; init; }

    /// <summary>
    /// Identity mapping findings: mapped identities, unresolved identities, operator decisions.
    /// </summary>
    public required IdentityMappingFinding[] IdentityFindings { get; init; }

    /// <summary>
    /// Package artefact validation findings: missing revision folders, missing attachments, missing images.
    /// </summary>
    public required ArtefactFinding[] ArtefactFindings { get; init; }

    /// <summary>
    /// Field transformation compatibility findings: field mismatches, type errors, etc.
    /// </summary>
    public required FieldTransformFinding[] FieldTransformFindings { get; init; }

    /// <summary>
    /// Blocking issues that must be resolved before import can proceed.
    /// Each item is a (category, description, recommendation) tuple.
    /// </summary>
    public required BlockingIssue[] BlockingIssues { get; init; }

    /// <summary>
    /// Warnings: issues that may affect import but do not block (e.g., unresolved identities if not marked as critical).
    /// </summary>
    public required WarningItem[] Warnings { get; init; }

    /// <summary>
    /// Operator-added notes or decisions (e.g., "Use fallback identity for unknown users").
    /// </summary>
    public required string OperatorNotes { get; init; }
}

public sealed record ScopeSummary
{
    public required int TotalWorkItems { get; init; }
    public required int TotalRevisions { get; init; }
    public required int TotalAttachments { get; init; }
    public required int TotalEmbeddedImages { get; init; }
}

public sealed record NodeReadinessFinding
{
    /// <summary>
    /// Node path (area or iteration).
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// "Area" or "Iteration".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Status: "ReferencedInPackage", "ExistsOnTarget", "WillBeCreated", "Missing", "TranslationError".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Translated target path (if translation applied).
    /// </summary>
    public string? TranslatedPath { get; init; }

    /// <summary>
    /// Error message if status is "TranslationError" or "Missing".
    /// </summary>
    public string? ErrorMessage { get; init; }
}

public sealed record WorkItemTypeFinding
{
    /// <summary>
    /// Work item type name (from package).
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Count of work items of this type in package.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// Status: "SupportedOnTarget", "UnsupportedOnTarget", "Error".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Error message if status is "Error".
    /// </summary>
    public string? ErrorMessage { get; init; }
}

public sealed record IdentityMappingFinding
{
    /// <summary>
    /// Source identity ID (from package).
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Source display name or email.
    /// </summary>
    public required string SourceDisplay { get; init; }

    /// <summary>
    /// Status: "Mapped", "Unmapped", "Error".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Target identity ID (if mapped).
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Operator decision if unmapped: "Block", "UseDefault", "Skip".
    /// </summary>
    public string? OperatorDecision { get; init; }
}

public sealed record ArtefactFinding
{
    /// <summary>
    /// Artefact type: "RevisionFolder", "Attachment", "EmbeddedImage".
    /// </summary>
    public required string ArtefactType { get; init; }

    /// <summary>
    /// Relative path to the artefact (or folder) in package.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Status: "Present", "Missing", "Invalid".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Error message if status is "Missing" or "Invalid".
    /// </summary>
    public string? ErrorMessage { get; init; }
}

public sealed record FieldTransformFinding
{
    /// <summary>
    /// Work item type this transform applies to.
    /// </summary>
    public required string WorkItemType { get; init; }

    /// <summary>
    /// Field name referenced by the transform rule.
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// Status: "FieldExists", "FieldMissing", "TypeError".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Error message if status is not "FieldExists".
    /// </summary>
    public string? ErrorMessage { get; init; }
}

public sealed record BlockingIssue
{
    public required string Category { get; init; }  // e.g., "MissingNodePath", "UnsupportedType", "MissingArtefact"
    public required string Description { get; init; }
    public required string Recommendation { get; init; }
}

public sealed record WarningItem
{
    public required string Category { get; init; }
    public required string Message { get; init; }
}
```

---

### 3. ImportCheckpoint

**Purpose**: Immutable checkpoint record written to `.mission/Checkpoints/workitems-import.cursor.json`. Enables resumable import from any stage.

```csharp
/// <summary>
/// Immutable checkpoint record for work item import resumability.
/// Serialized to JSON and stored in package cursors folder.
/// </summary>
public sealed record ImportCheckpoint
{
    /// <summary>
    /// Version of checkpoint format (for forward compatibility).
    /// </summary>
    public required int Version { get; init; } = 1;

    /// <summary>
    /// Relative path to the last successfully processed revision folder.
    /// E.g., "WorkItems/2026-05-13/123456789-42-1".
    /// </summary>
    public required string LastProcessedRevisionFolder { get; init; }

    /// <summary>
    /// The stage that completed for LastProcessedRevisionFolder.
    /// Resume begins from the stage after this one.
    /// </summary>
    public required ImportStage LastCompletedStage { get; init; }

    /// <summary>
    /// ISO 8601 timestamp when this checkpoint was last updated.
    /// </summary>
    public required DateTime LastUpdated { get; init; }

    /// <summary>
    /// Total work items processed (cumulative, not reset on resume).
    /// </summary>
    public required int TotalWorkItemsProcessed { get; init; }

    /// <summary>
    /// Total revisions applied (cumulative).
    /// </summary>
    public required int TotalRevisionsApplied { get; init; }

    /// <summary>
    /// Total links applied (cumulative).
    /// </summary>
    public required int TotalLinksApplied { get; init; }

    /// <summary>
    /// Total attachments uploaded (cumulative).
    /// </summary>
    public required int TotalAttachmentsUploaded { get; init; }

    /// <summary>
    /// Total embedded images replayed (cumulative).
    /// </summary>
    public required int TotalEmbeddedImagesReplayed { get; init; }

    /// <summary>
    /// Count of revisions skipped (e.g., due to LeadReplay=false).
    /// </summary>
    public required int TotalRevisionsSkipped { get; init; }

    /// <summary>
    /// Hash of the job configuration used to create this checkpoint.
    /// Used to detect configuration changes that invalidate the checkpoint.
    /// </summary>
    public required string JobConfigurationHash { get; init; }

    /// <summary>
    /// Reason checkpoint was created (for diagnostics): "StageComplete", "Error", "Interrupted", "UserRequest".
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Optional error message if Reason is "Error" or "Interrupted".
    /// </summary>
    public string? ErrorMessage { get; init; }
}
```

**Validation Rules**:
- `LastProcessedRevisionFolder` must match the canonical WorkItems folder structure.
- `LastCompletedStage` must be within the valid ImportStage enum range.
- `LastUpdated` must be recent (within expected execution timeframe).
- `JobConfigurationHash` mismatch signals configuration change; requires operator confirmation to resume.

---

### 4. AttachmentReplayRecord

**Purpose**: Immutable record of a single attachment uploaded during import. Stored in idmap.db for idempotency.

```csharp
/// <summary>
/// Immutable record of an attachment replayed during import.
/// Stored in idmap.db to prevent duplicate uploads on resume.
/// </summary>
public sealed record AttachmentReplayRecord
{
    /// <summary>
    /// Source attachment ID (from exported work item).
    /// </summary>
    public required string SourceAttachmentId { get; init; }

    /// <summary>
    /// Target attachment ID (assigned by target system after upload).
    /// </summary>
    public required string TargetAttachmentId { get; init; }

    /// <summary>
    /// Source work item ID.
    /// </summary>
    public required int SourceWorkItemId { get; init; }

    /// <summary>
    /// Target work item ID (after mapping).
    /// </summary>
    public required int TargetWorkItemId { get; init; }

    /// <summary>
    /// File name from package.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// SHA256 hash of file content (for integrity validation).
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Timestamp when attachment was uploaded.
    /// </summary>
    public required DateTime UploadedAt { get; init; }

    /// <summary>
    /// Target-specific metadata (e.g., WITURI for Azure DevOps).
    /// </summary>
    public string? TargetMetadata { get; init; }
}
```

---

### 5. EmbeddedImageReplayRecord

**Purpose**: Immutable record of an embedded image replayed during import. Stored in idmap.db.

```csharp
/// <summary>
/// Immutable record of an embedded image replayed during import.
/// Stored in idmap.db to prevent duplicate uploads on resume.
/// </summary>
public sealed record EmbeddedImageReplayRecord
{
    /// <summary>
    /// Source image ID (unique identifier within package, typically generated).
    /// </summary>
    public required string SourceImageId { get; init; }

    /// <summary>
    /// Target image URL (after upload to target system).
    /// Used to rewrite field values (e.g., HTML img src).
    /// </summary>
    public required string TargetImageUrl { get; init; }

    /// <summary>
    /// Source work item ID that referenced this image.
    /// </summary>
    public required int SourceWorkItemId { get; init; }

    /// <summary>
    /// Target work item ID (after mapping).
    /// </summary>
    public required int TargetWorkItemId { get; init; }

    /// <summary>
    /// File name from package.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// SHA256 hash of file content.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Timestamp when image was uploaded.
    /// </summary>
    public required DateTime UploadedAt { get; init; }

    /// <summary>
    /// Field names that reference this image (for rewrite during import).
    /// E.g., ["Description", "RegressionDetail"].
    /// </summary>
    public required IReadOnlyList<string> ReferencingFields { get; init; }

    /// <summary>
    /// Original image reference(s) in exported field values (for matching).
    /// E.g., "build://123/image.png" or URL.
    /// </summary>
    public required IReadOnlyList<string> OriginalReferences { get; init; }
}
```

---

### 6. TargetIdentity

**Purpose**: Immutable record of a resolved identity on the target system.

```csharp
/// <summary>
/// Immutable record of a resolved target identity.
/// Result of IIdentityMappingService.ResolveIdentity().
/// </summary>
public sealed record TargetIdentity
{
    /// <summary>
    /// Target system identity ID (e.g., AAD OID for Azure DevOps, TFS user ID for TFS).
    /// </summary>
    public required string TargetId { get; init; }

    /// <summary>
    /// Display name on target system.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Email address on target system (if available).
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// True if this identity was explicitly mapped by operator; false if it's a default fallback.
    /// </summary>
    public required bool IsExplicitlyMapped { get; init; }

    /// <summary>
    /// Timestamp when mapping was resolved (for audit).
    /// </summary>
    public required DateTime ResolvedAt { get; init; }
}
```

---

### 7. TranslatedNodePaths

**Purpose**: Immutable collection of translated area and iteration paths for a work item.

```csharp
/// <summary>
/// Immutable collection of translated node paths (area and iteration) for a single work item.
/// Result of applying INodeTranslationTool to source paths.
/// </summary>
public sealed record TranslatedNodePaths
{
    /// <summary>
    /// Source area path (as exported from source system).
    /// </summary>
    public string? SourceAreaPath { get; init; }

    /// <summary>
    /// Translated area path (after applying NodeTranslation rules).
    /// Null if source path was null or translation failed.
    /// </summary>
    public string? TargetAreaPath { get; init; }

    /// <summary>
    /// Translation error for area path (if translation failed).
    /// </summary>
    public string? AreaPathTranslationError { get; init; }

    /// <summary>
    /// Source iteration path.
    /// </summary>
    public string? SourceIterationPath { get; init; }

    /// <summary>
    /// Translated iteration path (after applying NodeTranslation rules).
    /// </summary>
    public string? TargetIterationPath { get; init; }

    /// <summary>
    /// Translation error for iteration path (if translation failed).
    /// </summary>
    public string? IterationPathTranslationError { get; init; }
}
```

---

## State Transitions

### Work Item Import Lifecycle

```
┌─────────────────────────────────────────────────────────────────────┐
│ Import Started                                                        │
│ (Checkpoint loaded or fresh start)                                   │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                   ┌─────────▼────────┐
                   │ Enumerate        │
                   │ revision folders │
                   │ in order         │
                   └────────┬─────────┘
                            │
              ┌─────────────▼──────────────────┐
              │ For each revision folder:       │
              │ 1. Check if skip (LeadReplay)  │
              │ 2. Resolve identities         │
              │ 3. Translate paths            │
              │ 4. Load revision.json         │
              └──────────────┬────────────────┘
                             │
        ┌────────────────────▼─────────────────────┐
        │ Stage 1: CreatedOrUpdated                │
        │ - If first revision: create work item   │
        │ - Else: map to existing work item       │
        │ Checkpoint: (folder, CreatedOrUpdated)  │
        └──────────────┬───────────────────────────┘
                       │
        ┌──────────────▼────────────────────────┐
        │ Stage 2: AppliedFields                │
        │ - Resolve identities in fields       │
        │ - Apply NodeTranslation              │
        │ - Apply FieldTransform (if enabled)  │
        │ - Write fields to work item          │
        │ Checkpoint: (folder, AppliedFields)  │
        └──────────────┬───────────────────────┘
                       │
        ┌──────────────▼──────────────────────┐
        │ Stage 3: AppliedLinks                │
        │ - If LinkReplay enabled:            │
        │   For each link in revision:        │
        │   - Resolve target work item        │
        │   - Create/update link              │
        │ Checkpoint: (folder, AppliedLinks)  │
        └──────────────┬────────────────────┘
                       │
        ┌──────────────▼───────────────────────────┐
        │ Stage 4: UploadedAttachments            │
        │ - If AttachmentReplay enabled:         │
        │   For each attachment in revision:    │
        │   - Upload binary to target          │
        │   - Store mapping in idmap.db        │
        │ - If EmbeddedImageReplay enabled:    │
        │   For each embedded image:          │
        │   - Upload binary to target         │
        │   - Rewrite field values            │
        │   - Store mapping in idmap.db       │
        │ Checkpoint: (folder, UploadedAttach) │
        └──────────────┬──────────────────────┘
                       │
        ┌──────────────▼──────────────────────┐
        │ Stage 5: Completed                   │
        │ - Mark revision complete            │
        │ Checkpoint: (folder, Completed)     │
        └──────────────┬─────────────────────┘
                       │
                    ┌──▼───────────┐
                    │ Next revision │─────────┐
                    └───────────────┘         │
                                             │
                                   ┌─────────▼──────┐
                                   │ All revisions? │
                                   └─────────┬──────┘
                                             │
                        ┌────────────────────┘
                        │ Yes
                        ▼
                   ┌──────────────┐
                   │ Import Done  │
                   └──────────────┘
```

---

## Validation Rules

### ImportCheckpoint Validation

- `LastProcessedRevisionFolder` must match pattern: `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>`
- `LastCompletedStage` must be within enum range.
- `LastUpdated` timestamp must be within reasonable range (e.g., < 1 hour ago for resume).
- `JobConfigurationHash` must match current job configuration; if not, require operator confirmation.

### ImportReadinessReport Validation

- `IsReadyForImport` must be false if any `BlockingIssues` exist.
- `IsReadyForImport` must be true if no `BlockingIssues` exist and operator has not added blocking skip directives.
- Total count of findings must match scope (e.g., sum of type findings ≤ total work items).

### WorkItemImportContext Validation

- `SourceWorkItemId` must be > 0.
- `TargetWorkItemId` must be > 0 after CreatedOrUpdated stage.
- `CurrentStage` must be >= stage at creation and <= Completed.
- `ResolvedIdentities` keys must match identities referenced in `TranslatedFieldValues`.

---

## File Locations (Package Paths)

### Package-Resident Artefacts

```
.mission/
├── Checkpoints/
│   ├── workitems-import.cursor.json          # Import checkpoint (this module)
│   └── idmap.db                              # SQLite: identity/attachment/image mappings
├── Readiness/
│   └── workitems-import-readiness.json       # ImportReadinessReport (prepare output)
└── Runs/<runId>/
    └── diagnostics/
        └── workitems-import-diagnostics.json # Detailed import diagnostic log
```

### Source Artefacts

```
WorkItems/
└── yyyy-MM-dd/
    └── <ticks>-<workItemId>-<revisionIndex>/
        ├── revision.json                     # Revision metadata + fields
        ├── attachment1.bin                   # Attachment binary (inline)
        ├── attachment2.bin
        └── embeddedimage1.bin                # Embedded image binary (inline)
```

---

## Next Steps

This data model is frozen pending Phase 1 design review and Phase 2 task generation. All entities are immutable records consistent with SOLID principles. State transitions are deterministic and resumable from any checkpoint stage.


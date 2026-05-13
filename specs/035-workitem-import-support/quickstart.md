# Quick Start: Work Item Import Support

**Date**: 2026-05-13 | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

## Overview

This document provides quick reference for **operators** running work item import migrations and **developers** implementing or testing the feature locally.

---

## For Operators

### Typical Migration Workflow

```bash
# Step 1: Queue an export (produces package with work items)
devopsmigration queue \
  --job=Export \
  --source=https://dev.azure.com/myorg/myproject \
  --source-pat=$MY_PAT \
  --package=./my-package.zip

# Step 2: Wait for export to complete
# (or run in same job: --job=Migrate --source=... --target=...)

# Step 3: Run prepare to validate package and target readiness
devopsmigration queue \
  --job=Prepare \
  --package=./my-package.zip \
  --target=https://dev.azure.com/targetorg/targetproject \
  --target-pat=$TARGET_PAT

# Step 4: Review prepare findings
# (Look for ImportReadinessReport in package:
#  .mission/Readiness/workitems-import-readiness.json)

# Step 5: Resolve blocking issues if any
# - Create missing node paths on target
# - Add missing work item types
# - Review identity mapping decisions

# Step 6: Run import with desired extension levers
devopsmigration queue \
  --job=Import \
  --package=./my-package.zip \
  --target=https://dev.azure.com/targetorg/targetproject \
  --target-pat=$TARGET_PAT \
  --extensions=WorkItemImport.RevisionReplay=true,WorkItemImport.LinkReplay=true,WorkItemImport.AttachmentReplay=true,WorkItemImport.EmbeddedImageReplay=true,WorkItemImport.FieldTransform=true

# Step 7: Monitor progress in CLI or TUI
# (Displays revision count, links applied, attachments uploaded, etc.)

# Step 8: Validate import completeness (post-import verification)
devopsmigration queue \
  --job=Validate \
  --package=./my-package.zip \
  --target=https://dev.azure.com/targetorg/targetproject \
  --target-pat=$TARGET_PAT
```

### Configuration

#### Extension Levers (Job Configuration)

```yaml
# In job configuration YAML or CLI args:
Extensions:
  WorkItemImport:
    RevisionReplay: true           # Enable/disable work item revision replay
    LinkReplay: true               # Enable/disable work item link replay
    AttachmentReplay: true         # Enable/disable attachment binary upload
    EmbeddedImageReplay: true      # Enable/disable embedded image replay
    FieldTransform: true           # Enable/disable declarative field transforms
```

**Operator Guidance**:
- **RevisionReplay=true**: Import work items and their field history. **Set to false** if you want to create work items without history replay (faster, but less audit trail).
- **LinkReplay=true**: Create work item links as exported. Set to false if links cause conflicts.
- **AttachmentReplay=true**: Upload attachment binaries to target. Set to false if target lacks storage capacity.
- **EmbeddedImageReplay=true**: Replay embedded images in work item fields. Set to false if images cause field write errors.
- **FieldTransform=true**: Apply configured field cleanup/remapping rules. Set to false to import raw field values.

#### Node Translation Rules (Configuration)

```yaml
# In job configuration:
Tools:
  NodeTranslation:
    - SourceType: "Area"
      SourcePrefix: "Release"
      TargetPrefix: "Program"
      # This rule translates area paths starting with "Release" → "Program"
    - SourceType: "Iteration"
      SourcePattern: "^Sprint-([0-9]+)$"
      TargetTemplate: "Q1-Sprint-{1}"
      # This rule uses regex capture groups to remap sprint naming
```

#### Field Transform Rules (Configuration)

```yaml
# In job configuration:
Tools:
  FieldTransform:
    "User Story":
      - Field: "Priority"
        Transform: "MapValue"
        Mappings:
          "1 - High": "High"
          "2 - Medium": "Medium"
          "3 - Low": "Low"
    "Bug":
      - Field: "Severity"
        Transform: "PrependText"
        Prefix: "[MIGRATED] "
```

### Monitoring Progress

#### CLI Output

```
WorkItem Import Progress:
├── Nodes Created: 12/15 paths
├── Work Items: 234/500 (47%)
│   ├── Revisions Applied: 1,234/2,000 (62%)
│   ├── Links Created: 456/600 (76%)
│   ├── Attachments Uploaded: 89/120 (74%)
│   └── Embedded Images: 23/30 (77%)
├── Errors: 2 (minor field transforms failed)
└── ETA: 2m 30s
```

#### TUI Display

- Live progress bar for work items.
- Expandable details for current revision, stage, and error messages.
- Pause/resume controls (via Control Plane).

### Handling Errors

#### Import Fails During Prepare

**Error**: `ImportReadinessReport records blocking issues.`

**Resolution**:
1. Open `.mission/Readiness/workitems-import-readiness.json` in package.
2. Review `BlockingIssues` array.
3. Common issues:
   - **MissingNodePath**: Create the area/iteration path on target, or enable automatic node creation.
   - **UnsupportedWorkItemType**: Create the work item type on target or add mapping rule.
   - **MissingArtefact**: Verify export completed successfully; re-run export if needed.
4. Rerun prepare after correcting issues.

#### Import Halts Mid-Stream

**Error**: `Import interrupted; checkpoint saved.`

**Resolution**:
1. Check `.mission/Checkpoints/workitems-import.cursor.json` to see last completed stage.
2. Fix the underlying issue (e.g., target API rate limit, attachment upload failure).
3. Rerun import command with same package and target.
4. Import resumes from next incomplete stage (no duplicate work items created).

#### Field Transform Fails

**Error**: `FieldTransform failed for work item 123: field 'Status' type mismatch.`

**Resolution**:
1. Review the transform rule in job configuration.
2. Verify the target field exists and has compatible type.
3. Disable `FieldTransform` temporarily to import without transforms (raw values).
4. Apply transforms post-import manually or via a corrected transform rule.

---

## For Developers

### Local Testing Setup

#### Prerequisites

```bash
# Ensure .NET 9/10 SDK is installed
dotnet --version  # Should be 9.0+

# Build the solution
dotnet build DevOpsMigrationPlatform.slnx --no-incremental

# Run tests to verify setup
dotnet test DevOpsMigrationPlatform.slnx --filter "Category=Unit"
```

#### Running Feature Tests (Simulated Connector)

```bash
# Run work item import feature tests
dotnet test DevOpsMigrationPlatform.sln \
  --filter "Category=Feature&Path=*import*" \
  --verbosity normal

# Or specific test file
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/WorkItemImportModuleTests.cs
```

#### Running System Tests (Local Stack)

```bash
# Start local stack (ControlPlaneHost + MigrationAgent + simulated connectors)
# From CLI project root:
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration -- \
  queue \
  --job=Migrate \
  --mode=Standalone \
  --source=simulated \
  --target=simulated \
  --package=./test-output.zip \
  --diagnostics

# Output appears in ./test-output.zip (package) and console (progress)
```

### Architecture: Import Module Structure

```
src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/
├── WorkItemImportModule.cs
│   └── IModule interface implementation
│       ├── PrepareAsync(context) → ImportReadinessReport
│       └── ImportAsync(context, progressSink) → void
│
├── ImportCheckpointService.cs
│   └── Manages .mission/Checkpoints/workitems-import.cursor.json
│       └── SaveCheckpoint(folder, stage) → void
│
├── NodeReadinessOrchestrator.cs
│   └── Create required area/iteration paths
│       └── EnsureNodesAsync(requiredPaths, target) → CreatedPaths
│
├── WorkItemRevisionImporter.cs
│   └── Process one revision folder (all stages)
│       ├── CreateOrUpdateWorkItem(revision) → targetWorkItemId
│       ├── ApplyFields(revision, identities, transforms) → void
│       ├── ApplyLinks(revision) → void
│       └── UploadAttachments(revision) → AttachmentRecords
│
├── LinkReplayService.cs
│   └── Apply revision-owned links
│       └── ApplyLinksAsync(revision, targetWorkItem) → void
│
├── AttachmentReplayService.cs
│   └── Upload attachment binaries
│       └── UploadAttachmentsAsync(revision) → AttachmentRecords
│
├── EmbeddedImageReplayService.cs
│   └── Replay embedded images and rewrite field references
│       └── ReplayEmbeddedImagesAsync(revision, fields) → RewrittenFields
│
└── Models/
    ├── ImportReadinessReport.cs
    ├── ImportCheckpoint.cs
    ├── WorkItemImportContext.cs
    └── ... (other DTOs)
```

### Key Dependencies (Injected)

```csharp
// Import module constructor receives:
public WorkItemImportModule(
    IArtefactStore artefactStore,              // Package I/O
    IStateStore stateStore,                    // Cursor + idmap.db
    IIdentityMappingService identityMapping,   // Identity resolution
    INodeTranslationTool nodeTranslation,      // Path translation
    IFieldTransformTool fieldTransform,        // Field transformation
    IWorkItemService workItemService,          // Target system: create/update work items
    INodeService nodeService,                  // Target system: manage node paths
    IAttachmentService attachmentService,      // Target system: upload attachments
    IOptions<WorkItemImportOptions> options,   // Feature levers
    ILogger<WorkItemImportModule> logger)      // Structured logging
{
    // Store dependencies
}
```

### Testing Patterns

#### Unit Test Example

```csharp
[TestClass]
public class WorkItemRevisionImporterTests
{
    [TestMethod]
    public async Task ApplyFields_WithNodeTranslation_TranslatesAreaAndIterationPaths()
    {
        // Arrange
        var mockNodeTranslation = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        mockNodeTranslation
            .Setup(x => x.TranslatePath("Area", "Team\\Feature"))
            .Returns("Program\\Feature");

        var importer = new WorkItemRevisionImporter(
            /* ...dependencies... */
            nodeTranslation: mockNodeTranslation.Object);

        var revision = new Revision
        {
            AreaPath = "Team\\Feature",
            IterationPath = "Sprint 1"
        };

        // Act
        await importer.ApplyFieldsAsync(revision, /* ...other params... */);

        // Assert
        mockNodeTranslation.Verify(
            x => x.TranslatePath(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeastOnce);
    }
}
```

#### Feature Test Example (Reqnroll)

```gherkin
# features/import/deterministic-revision-replay.feature

Feature: Deterministic Revision Replay
    As a migration operator
    I want work item revisions replayed in package order
    So the target ends up with correct history and state

    Scenario: Revisions processed in lexicographic folder order
        Given an exported package with 3 work item revisions in order:
            | Folder                               | WorkItemId | RevisionIndex |
            | WorkItems/2026-05-13/123-42-0        | 42         | 0             |
            | WorkItems/2026-05-13/123-42-1        | 42         | 1             |
            | WorkItems/2026-05-13/456-43-0        | 43         | 0             |
        When I import the package
        Then revisions are applied in that order:
            | Step | WorkItemId | RevisionIndex | Stage            |
            | 1    | 42         | 0             | CreatedOrUpdated |
            | 2    | 42         | 1             | AppliedFields    |
            | 3    | 43         | 0             | CreatedOrUpdated |
        And checkpoint records last processed folder as "WorkItems/2026-05-13/456-43-0"
```

### Building and Running Tests

```bash
# Build solution
dotnet build

# Run all tests
dotnet test

# Run specific category
dotnet test --filter "Category=Feature"

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura

# Clean before build (enforce fresh state)
dotnet clean && dotnet build --no-incremental
```

### Debugging Locally

#### Enable Diagnostic Logging

```csharp
// In test setup:
var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<WorkItemImportModule>();
```

#### Inspect Checkpoint State

```bash
# After interrupted import:
cat .mission/Checkpoints/workitems-import.cursor.json

# Output:
{
  "version": 1,
  "lastProcessedRevisionFolder": "WorkItems/2026-05-13/123456789-42-1",
  "lastCompletedStage": "AppliedFields",
  "lastUpdated": "2026-05-13T14:32:10Z",
  "totalWorkItemsProcessed": 42,
  "totalRevisionsApplied": 120,
  ...
}
```

#### Inspect ID Mappings

```bash
# Query SQLite idmap.db:
sqlite3 .mission/Checkpoints/idmap.db

# List tables:
.tables

# Query work item mappings:
SELECT source_id, target_id FROM work_item_mappings;

# Query attachment mappings:
SELECT source_id, target_id, file_name FROM attachment_mappings;
```

### Adding a New Feature (e.g., Comment Replay)

Comment replay is **out of scope** for this increment. If needed in future:

1. **Design**: Add `ICommentReplayService` interface to `Abstractions.Agent`.
2. **Data Model**: Add `CommentReplayRecord` to `data-model.md`.
3. **Module**: Add `ApplyComments()` stage to `WorkItemRevisionImporter`.
4. **Checkpoint**: Add new stage (e.g., `AppliedComments`) to `ImportStage` enum.
5. **Spec**: Create new US with acceptance scenarios.
6. **Tests**: Add feature tests and unit tests for comment replay.
7. **Connectors**: Implement for Simulated, AzureDevOps, TFS.
8. **Docs**: Update configuration schema, operator guide, etc.

---

## Configuration Schema Reference

### WorkItemImportOptions (Sealed Options Class)

```csharp
public sealed class WorkItemImportOptions
{
    public const string SectionName = "Extensions:WorkItemImport";

    [Required]
    public bool RevisionReplay { get; init; } = true;

    [Required]
    public bool LinkReplay { get; init; } = true;

    [Required]
    public bool AttachmentReplay { get; init; } = true;

    [Required]
    public bool EmbeddedImageReplay { get; init; } = true;

    [Required]
    public bool FieldTransform { get; init; } = true;

    /// <summary>
    /// Max attachments to upload in parallel (for perf tuning).
    /// Default: 3 (conservative; increase if target API allows).
    /// </summary>
    [Range(1, 10)]
    public int MaxParallelAttachmentUploads { get; init; } = 3;

    /// <summary>
    /// Timeout per attachment upload in seconds.
    /// Default: 300s (5 minutes).
    /// </summary>
    [Range(10, 3600)]
    public int AttachmentUploadTimeoutSeconds { get; init; } = 300;
}
```

### Extension Configuration Example

```yaml
# appsettings.json or job config:
{
  "Extensions": {
    "WorkItemImport": {
      "RevisionReplay": true,
      "LinkReplay": true,
      "AttachmentReplay": true,
      "EmbeddedImageReplay": true,
      "FieldTransform": true,
      "MaxParallelAttachmentUploads": 5,
      "AttachmentUploadTimeoutSeconds": 600
    }
  }
}
```

---

## Observability

### Metrics (OpenTelemetry)

```
workitem.import.revisions.processed (counter)
  └── Attributes: connector, status (success/failed)

workitem.import.links.applied (counter)
workitem.import.attachments.uploaded (counter)
workitem.import.embedded_images.replayed (counter)

workitem.import.checkpoint.saved (counter)
  └── Attributes: stage, duration_ms

workitem.import.identity_resolved (counter)
  └── Attributes: mapping_type (explicit/default/fallback)

workitem.import.field_transform.applied (counter)
  └── Attributes: transform_group, field_name, status (success/failed)
```

### Traces

Each work item revision includes a trace span with child spans:
- `workitem.import.revision` (parent)
  - `workitem.import.revision.create_or_update`
  - `workitem.import.revision.apply_fields`
  - `workitem.import.revision.apply_links`
  - `workitem.import.revision.upload_attachments`
  - `workitem.import.revision.replay_embedded_images`

### Logs (Structured)

```json
{
  "timestamp": "2026-05-13T14:32:10.123Z",
  "level": "Info",
  "message": "Revision applied successfully",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7",
  "workItemId": 42,
  "sourceWorkItemId": 42,
  "revisionFolder": "WorkItems/2026-05-13/123456789-42-1",
  "stage": "AppliedFields",
  "durationMs": 234
}
```

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| `Import checkpoint mismatch` | Job config changed since last run | Verify config matches exported package; use ForceFresh to restart. |
| `Identity not resolved` | Mapping service unavailable | Ensure IdentitiesModule completed before import; check mapping file. |
| `Node creation fails` | Target lacks node management API (TFS may not support) | Disable node creation; create paths manually on target. |
| `Attachment upload timeout` | Target API slow or network congested | Increase `AttachmentUploadTimeoutSeconds` in config; reduce `MaxParallelAttachmentUploads`. |
| `Field transform error` | Transform rule syntax incorrect | Validate transform rule in config; disable FieldTransform to import raw values. |

---

## References

- [Plan](plan.md) — Architecture and gates
- [Data Model](data-model.md) — Entity definitions
- [Research](research.md) — Design decisions
- [Spec](spec.md) — User scenarios and requirements
- [Architecture Overview](../../docs/architecture.md) — Repository-wide architecture
- [Configuration Reference](../../docs/configuration-reference.md) — Full config schema


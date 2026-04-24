# Data Model: Work Item Field Transformation

**Feature**: 022-workitem-field-mapping  
**Date**: 2026-04-24

## Domain Model

### Core Abstractions (in `DevOpsMigrationPlatform.Abstractions`)

```
┌─────────────────────────────┐
│  IFieldTransformTool        │──── Top-level tool interface
│  ├── Validate()             │     Prepare-time validation
│  └── ApplyTransforms()      │     Execute pipeline on a field set
└─────────────┬───────────────┘
              │ uses
              ▼
┌─────────────────────────────┐
│  IFieldTransform            │──── Individual transform contract
│  ├── Type: string           │     Type discriminator
│  ├── Name: string           │     Display name (telemetry)
│  └── Apply(fields, ctx)     │     Pure transformation
└─────────────────────────────┘
              ▲
              │ implemented by 14 concrete transforms
              │
  ┌───────────┼───────────────────────────────┐
  │           │                               │
CopyField  MapValue  RegexField  ... (11 more)
```

### Records & Value Objects

```csharp
// Transform pipeline input/output
public sealed record FieldTransformContext(
    int WorkItemId,
    int RevisionIndex,
    string WorkItemType,
    string Phase);           // "export" | "import"

public sealed record FieldTransformResult(
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<FieldTransformAction> Actions);

public sealed record FieldTransformAction(
    string GroupName,
    string TransformName,
    string TransformType,
    string Field,
    bool Modified,
    string? OldValue,
    string? NewValue);

// Validation
public sealed record FieldTransformValidationReport(
    bool IsValid,
    IReadOnlyList<FieldTransformValidationEntry> Entries);

public sealed record FieldTransformValidationEntry(
    string GroupName,
    string TransformName,
    string Field,
    FieldTransformValidationSeverity Severity,
    string Message);

public enum FieldTransformValidationSeverity
{
    Error,
    Warning,
    Info
}
```

### Options Model (Configuration)

```csharp
public sealed class FieldTransformOptions
{
    public static string SectionName => "MigrationPlatform:Tools:FieldTransform";
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<FieldTransformGroupOptions> TransformGroups { get; init; } = [];
}

public sealed class FieldTransformGroupOptions
{
    public string? Name { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string>? ApplyTo { get; init; }
    public IReadOnlyList<FieldTransformRuleOptions> Transforms { get; init; } = [];
}

public sealed class FieldTransformRuleOptions
{
    public string? Name { get; init; }
    public string Type { get; init; } = "";          // Type discriminator
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string>? ApplyTo { get; init; }

    // --- Type-specific properties (polymorphic) ---
    // CopyField / CopyFieldBatch
    public string? SourceField { get; init; }
    public string? TargetField { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyDictionary<string, string>? FieldMappings { get; init; }  // CopyFieldBatch

    // SetField
    public string? Field { get; init; }
    public string? Value { get; init; }

    // MapValue
    public IReadOnlyDictionary<string, string>? ValueMap { get; init; }

    // MergeFields
    public IReadOnlyList<string>? SourceFields { get; init; }
    public string? FormatString { get; init; }

    // CalculateField
    public string? Expression { get; init; }

    // Regex
    public string? Pattern { get; init; }
    public string? Replacement { get; init; }

    // ConditionalTag / ConditionalField
    public string? Condition { get; init; }
    public string? Tag { get; init; }
    public string? TrueValue { get; init; }
    public string? FalseValue { get; init; }
}
```

**Design Note**: The `FieldTransformRuleOptions` uses a flat property bag rather than polymorphic deserialization. This is the simplest approach that works with `IOptions<T>` binding. The `Type` discriminator selects which properties are relevant. The `IFieldTransformFactory` validates that required properties are present for each type.

### Extension Reference Options

```csharp
public sealed class FieldTransformExtensionOptions
{
    public bool Enabled { get; init; } = true;
    public string Phase { get; init; } = "import";  // export | import | both
}
```

## Data Flow

### Import Phase (default)

```
revision.json (on disk)
    │
    ▼ IArtefactStore.ReadAsync()
    │
Dictionary<string, object?> fields
    │
    ▼ IFieldTransformTool.ApplyTransforms(fields, context)
    │
    ├── Group 1 [if enabled && applyTo matches]
    │   ├── Transform 1.1 [if enabled && applyTo matches] → modified fields
    │   ├── Transform 1.2 → modified fields
    │   └── ...
    ├── Group 2 → modified fields
    │   └── ...
    ├── Tag deduplication pass (if any tag transform fired)
    │
    ▼
Modified Dictionary<string, object?>
    │
    ▼ Apply to target (via existing import pipeline)
```

### Export Phase (opt-in)

```
Source API response → field dictionary
    │
    ▼ IFieldTransformTool.ApplyTransforms(fields, context)
    │
Modified Dictionary<string, object?>
    │
    ▼ Serialize to revision.json → IArtefactStore.WriteAsync()
```

### Prepare-Time Validation

```
IFieldTransformValidator.ValidateAsync()
    │
    ├── Load all configured transforms
    ├── Resolve field references against source field definitions
    ├── Resolve field references against target field definitions
    ├── Check type compatibility
    ├── Check picklist values for MapValue targets
    ├── Sample dry-run (N work items, default 10)
    │
    ▼
FieldTransformValidationReport
    │
    ├── IsValid = true → proceed
    └── IsValid = false → block migration
```

## Key Design Decisions

| Decision | Rationale |
|---|---|
| Flat options class (not polymorphic) | Simplest binding with `IOptions<T>`. Factory validates required properties per type. |
| Factory pattern for transform creation | Consistent with `IWorkItemRevisionSourceFactory`. Decouples config from implementation. |
| Tag dedup as pipeline post-processor | Keeps individual transforms simple. Single responsibility for dedup logic. |
| `FieldTransformResult` with actions log | Enables FR-009 structured telemetry without coupling transforms to the telemetry system. |
| Pure function design (no I/O in transforms) | FR-005 compliance. Enables unit testing without mocks. |
| No per-transform cursor/checkpoint | Transforms are part of Stage B processing. Existing cursor handles resume. |

# Contracts: Work Item Field Transformation

**Feature**: 022-workitem-field-mapping  
**Date**: 2026-04-24

These are the proposed interface contracts for the FieldTransformTool. All interfaces will be defined in `DevOpsMigrationPlatform.Abstractions/Tools/`.

## IFieldTransformTool

The top-level tool interface. Singleton, stateless, invoked per-revision.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// Applies a configured pipeline of field transforms to a work item revision's field collection.
/// Declared as a singleton under MigrationPlatform.Tools.FieldTransform.
/// </summary>
public interface IFieldTransformTool
{
    /// <summary>
    /// Applies all enabled transform groups and transforms to the given field collection.
    /// Returns a new field dictionary (does not mutate the input) and an action log for telemetry.
    /// </summary>
    FieldTransformResult ApplyTransforms(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context);

    /// <summary>
    /// Returns true if the tool is enabled and has any transforms configured for the given phase.
    /// </summary>
    bool IsEnabledForPhase(FieldTransformPhase phase);
}
```

## IFieldTransform

The contract for a single transform implementation.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// A single field transformation that operates on a revision's field collection.
/// Implementations must be pure — no I/O, no side effects, no state across invocations.
/// </summary>
public interface IFieldTransform
{
    /// <summary>Type discriminator matching the config value (e.g., "CopyField", "MapValue").</summary>
    string Type { get; }

    /// <summary>Display name for telemetry and error messages.</summary>
    string Name { get; }

    /// <summary>
    /// Applies the transform to the field collection. Returns a modified copy.
    /// Must not mutate the input dictionary.
    /// </summary>
    FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context);
}
```

## IFieldTransformFactory

Creates typed transform instances from configuration.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// Creates IFieldTransform instances from configuration options.
/// Validates that required properties are present for the given type discriminator.
/// </summary>
public interface IFieldTransformFactory
{
    /// <summary>
    /// Creates a transform from the given rule options.
    /// Throws InvalidOperationException if the type is unknown or required properties are missing.
    /// </summary>
    IFieldTransform Create(FieldTransformRuleOptions options, string groupName, int ordinal);
}
```

## IFieldTransformValidator

Prepare-time validation — callable from the `prepare` command.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// Validates all configured field transforms against source and target field definitions.
/// Invoked during the prepare command before migration begins.
/// </summary>
public interface IFieldTransformValidator
{
    /// <summary>
    /// Validates transform configuration against actual field metadata.
    /// Includes a sample dry-run of N work items.
    /// Source and target field definition providers are injected via constructor
    /// (IFieldDefinitionProviderFactory) per architecture review CA-M1.
    /// </summary>
    Task<FieldTransformValidationReport> ValidateAsync(
        int sampleSize = 10,
        CancellationToken cancellationToken = default);
}
```

## IFieldDefinitionProvider

Abstracts field metadata retrieval from source/target systems.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// Provides field definition metadata for a target or source system.
/// Used by the validator to check field existence, types, and allowed values.
/// </summary>
public interface IFieldDefinitionProvider
{
    /// <summary>Gets field definitions for the specified work item type, or all types if null.</summary>
    Task<IReadOnlyList<FieldDefinition>> GetFieldDefinitionsAsync(
        string? workItemType = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Metadata for a single work item field in a target/source system.</summary>
public sealed record FieldDefinition(
    string ReferenceName,
    string Name,
    string Type,
    bool IsReadOnly,
    IReadOnlyList<string>? AllowedValues);
```

## IFieldDefinitionProviderFactory

Creates source/target field definition providers. Injected into `IFieldTransformValidator` via constructor (CA-M1).

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Tools;

/// <summary>
/// Creates IFieldDefinitionProvider instances for source and target systems.
/// Injected into IFieldTransformValidator to resolve field metadata
/// without passing providers as method parameters (architecture review CA-M1).
/// </summary>
public interface IFieldDefinitionProviderFactory
{
    /// <summary>Creates a provider for the migration source system.</summary>
    IFieldDefinitionProvider CreateSourceProvider();

    /// <summary>Creates a provider for the migration target system.</summary>
    IFieldDefinitionProvider CreateTargetProvider();
}
```

## Dependency Registration

```csharp
namespace DevOpsMigrationPlatform.Infrastructure.Tools.FieldTransform;

public static class FieldTransformToolServiceCollectionExtensions
{
    public static IServiceCollection AddFieldTransformToolServices(
        this IServiceCollection services)
    {
        services.AddOptions<FieldTransformOptions>()
            .BindConfiguration(FieldTransformOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddSingleton<IFieldTransformTool, FieldTransformTool>();
        services.AddSingleton<IFieldTransformFactory, FieldTransformFactory>();
        services.AddSingleton<IFieldTransformValidator, FieldTransformValidator>();

        return services;
    }
}
```

# Contract: `SchemaOptionsEntry`

**Location**: `DevOpsMigrationPlatform.Abstractions/Options/SchemaOptionsEntry.cs`

## Purpose

`SchemaOptionsEntry` is a DI-registered record that binds an options type to its config section path. It is the sole mechanism by which the `SchemaGenerator` discovers which options types contribute to `migration.schema.json`. Registering an `IOptions<T>` without a corresponding `SchemaOptionsEntry` means that options type is invisible to the schema and to IDE IntelliSense.

## Interface Contract

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Registration record that links an IOptions&lt;T&gt; options type to its canonical
/// config section path. Register one entry per options type via
/// <see cref="SchemaOptionsEntryExtensions.AddSchemaEntry{T}"/>.
/// </summary>
public sealed class SchemaOptionsEntry
{
    /// <summary>The options type (e.g. typeof(WorkItemsModuleOptions)).</summary>
    public required Type OptionsType { get; init; }

    /// <summary>
    /// The dot-separated section path matching T.SectionName
    /// (e.g. "MigrationPlatform:Modules:WorkItems").
    /// </summary>
    public required string SectionPath { get; init; }

    /// <summary>Optional description injected into the schema's description field.</summary>
    public string? Description { get; init; }
}

/// <summary>Convenience extension for registering a schema entry alongside IOptions&lt;T&gt;.</summary>
public static class SchemaOptionsEntryExtensions
{
    public static IServiceCollection AddSchemaEntry<T>(
        this IServiceCollection services,
        string? description = null)
        where T : class
    {
        var sectionName = (string)typeof(T)
            .GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        return services.AddSingleton(new SchemaOptionsEntry
        {
            OptionsType = typeof(T),
            SectionPath = sectionName,
            Description = description
        });
    }
}
```

## Registration Pattern

Every `Add*Services(IServiceCollection services)` extension that registers `IOptions<T>` MUST also call `services.AddSchemaEntry<T>()`:

```csharp
// In SimulatedConnectorServiceExtensions.cs
public static IServiceCollection AddSimulatedConnectorServices(this IServiceCollection services)
{
    services.AddOptions<SimulatedEndpointOptions>()
            .BindConfiguration(SimulatedEndpointOptions.SectionName);
    services.AddSchemaEntry<SimulatedEndpointOptions>("Simulated source connector settings");
    // ...
    return services;
}
```

## Invariants

- `SectionPath` MUST equal `T.SectionName` (enforced by the helper via reflection).
- No two entries may share the same `SectionPath` — the `SchemaGenerator` fails the build with a structured `Error` log if a duplicate is found.
- All three connector assemblies (Simulated, AzureDevOps, TFS) MUST register entries for every options type they own.

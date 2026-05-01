using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Generation;

namespace DevOpsMigrationPlatform.SchemaGenerator;

/// <summary>
/// Generates the canonical migration.schema.json from registered SchemaOptionsEntry singletons.
/// Uses NJsonSchema to produce JSON Schema Draft 7 with strict additionalProperties: false
/// and discriminated unions (oneOf) for endpoint polymorphic types.
/// </summary>
public sealed class SchemaGeneratorHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchemaGeneratorHost> _logger;
    private readonly ActivitySource _activitySource;

    public SchemaGeneratorHost(
        IServiceProvider serviceProvider,
        ILogger<SchemaGeneratorHost> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activitySource = new ActivitySource(WellKnownActivitySourceNames.Migration);
    }

    /// <summary>
    /// Generates the JSON Schema file and writes it to the specified output path.
    /// Returns 0 on success, non-zero on failure.
    /// </summary>
    public async Task<int> RunAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("schema.generate");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var entries = _serviceProvider.GetServices<SchemaOptionsEntry>().ToList();

            _logger.LogInformation("Schema generation started — {EntryCount} entries", entries.Count);

            activity?.SetTag("schema.entry_count", entries.Count);
            activity?.SetTag("schema.output_path", outputPath);

            // Detect duplicate SectionPath registrations
            var duplicates = entries
                .GroupBy(e => e.SectionPath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                foreach (var group in duplicates)
                {
                    var types = string.Join(", ", group.Select(e => e.OptionsType.FullName));
                    _logger.LogError(
                        "Duplicate SectionName '{SectionPath}' registered by {Type1} and {Type2}",
                        group.Key,
                        group.First().OptionsType.FullName,
                        group.Skip(1).First().OptionsType.FullName);
                }
                return 1;
            }

            // Generate JSON Schema
            var schema = await GenerateSchemaAsync(entries, cancellationToken);

            // Write to output path
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, schema, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Schema generation succeeded — {EntryCount} entries in {DurationMs}ms → {OutputPath}",
                entries.Count,
                stopwatch.ElapsedMilliseconds,
                outputPath);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema generation failed at step '{Step}': {Error}", "RunAsync", ex.Message);
            return 1;
        }
    }

    private async Task<string> GenerateSchemaAsync(
        List<SchemaOptionsEntry> entries,
        CancellationToken cancellationToken)
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            SchemaType = SchemaType.JsonSchema,
            AlwaysAllowAdditionalObjectProperties = false,
            DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull
        };

        // Create root schema
        var rootSchema = new JsonSchema
        {
            Type = JsonObjectType.Object,
            AdditionalPropertiesSchema = null,
            AllowAdditionalProperties = false,
            Title = "Azure DevOps Migration Platform Configuration Schema",
            Description = "JSON Schema for migration configuration files (migration.json, migration-config.json)"
        };

        // Build nested schema tree from flat entries
        foreach (var entry in entries)
        {
            var pathParts = entry.SectionPath.Split(':');
            var currentSchema = rootSchema;

            for (int i = 0; i < pathParts.Length; i++)
            {
                var part = pathParts[i];
                var isLast = i == pathParts.Length - 1;

                if (!currentSchema.Properties.ContainsKey(part))
                {
                    if (isLast)
                    {
                        // Leaf node — generate schema from options type
                        var generator = new JsonSchemaGenerator(settings);
                        var typeSchema = await Task.Run(() => generator.Generate(entry.OptionsType), cancellationToken);

                        if (!string.IsNullOrWhiteSpace(entry.Description))
                        {
                            typeSchema.Description = entry.Description;
                        }

                        // Add to definitions and reference it
                        var defName = entry.OptionsType.Name;
                        rootSchema.Definitions[defName] = typeSchema;
                        
                        currentSchema.Properties[part] = new JsonSchemaProperty
                        {
                            Reference = typeSchema
                        };
                    }
                    else
                    {
                        // Intermediate node — create empty object schema property
                        currentSchema.Properties[part] = new JsonSchemaProperty
                        {
                            Type = JsonObjectType.Object,
                            AllowAdditionalProperties = false
                        };
                    }
                }

                if (!isLast)
                {
                    // Navigate to nested property for next iteration
                    currentSchema = currentSchema.Properties[part].ActualSchema;
                }
            }
        }

        // Handle discriminated unions for source/target using EndpointOptionsTypeRegistry
        var registry = _serviceProvider.GetService<EndpointOptionsTypeRegistry>();
        if (registry != null)
        {
            await ApplyDiscriminatedUnionsAsync(rootSchema, registry, settings, cancellationToken);
        }

        return rootSchema.ToJson();
    }

    private async Task ApplyDiscriminatedUnionsAsync(
        JsonSchema rootSchema,
        EndpointOptionsTypeRegistry registry,
        JsonSchemaGeneratorSettings settings,
        CancellationToken cancellationToken)
    {
        // Check if MigrationPlatform:Source exists in the schema tree
        if (rootSchema.Properties.TryGetValue("MigrationPlatform", out var platformSchema))
        {
            if (platformSchema.Properties.TryGetValue("Source", out var sourceSchema))
            {
                // Replace with oneOf discriminated union
                var sourceOneOf = new List<JsonSchema>();

                // Get all registered endpoint types
                var endpointTypes = GetRegisteredEndpointTypes(registry);

                foreach (var (key, type) in endpointTypes)
                {
                    var generator = new JsonSchemaGenerator(settings);
                    var typeSchema = await Task.Run(() => generator.Generate(type), cancellationToken);
                    
                    // Add discriminator constant
                    var discriminatorProperty = new JsonSchemaProperty
                    {
                        Type = JsonObjectType.String,
                        IsRequired = true
                    };
                    discriminatorProperty.Enumeration.Add(key);
                    typeSchema.Properties["type"] = discriminatorProperty;
                    
                    sourceOneOf.Add(typeSchema);
                }

                if (sourceOneOf.Any())
                {
                    sourceSchema.OneOf.Clear();
                    foreach (var schema in sourceOneOf)
                    {
                        sourceSchema.OneOf.Add(schema);
                    }
                }
            }

            if (platformSchema.Properties.TryGetValue("Target", out var targetSchema))
            {
                // Replace with oneOf discriminated union (same logic as Source)
                var targetOneOf = new List<JsonSchema>();

                var endpointTypes = GetRegisteredEndpointTypes(registry);

                foreach (var (key, type) in endpointTypes)
                {
                    var generator = new JsonSchemaGenerator(settings);
                    var typeSchema = await Task.Run(() => generator.Generate(type), cancellationToken);
                    
                    // Add discriminator constant
                    var discriminatorProperty = new JsonSchemaProperty
                    {
                        Type = JsonObjectType.String,
                        IsRequired = true
                    };
                    discriminatorProperty.Enumeration.Add(key);
                    typeSchema.Properties["type"] = discriminatorProperty;
                    
                    targetOneOf.Add(typeSchema);
                }

                if (targetOneOf.Any())
                {
                    targetSchema.OneOf.Clear();
                    foreach (var schema in targetOneOf)
                    {
                        targetSchema.OneOf.Add(schema);
                    }
                }
            }
        }
    }

    private List<(string Key, Type Type)> GetRegisteredEndpointTypes(EndpointOptionsTypeRegistry registry)
    {
        var types = new List<(string, Type)>();

        // Known endpoint types
        var knownKeys = new[] { "Simulated", "AzureDevOpsServices", "TeamFoundationServer" };

        foreach (var key in knownKeys)
        {
            if (registry.TryGetType(key, out var type) && type != null)
            {
                types.Add((key, type));
            }
        }

        return types;
    }
}

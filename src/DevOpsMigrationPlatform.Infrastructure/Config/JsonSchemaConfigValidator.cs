using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Configuration;
using Microsoft.Extensions.Options;
using NJsonSchema;

namespace DevOpsMigrationPlatform.Infrastructure.Config;

/// <summary>
/// Validates raw JSON configuration against a schema using NJsonSchema.
/// </summary>
public sealed class JsonSchemaConfigValidator : IConfigSchemaValidator
{
    private readonly string _schemaPath;

    public JsonSchemaConfigValidator(IOptions<JsonSchemaConfigValidatorOptions> options)
    {
        _schemaPath = options.Value.SchemaPath;
    }

    public IReadOnlyList<SchemaValidationError> Validate(string rawJson)
    {
        if (!File.Exists(_schemaPath))
        {
            return Array.Empty<SchemaValidationError>();
        }

        var schemaJson = File.ReadAllText(_schemaPath);
        var schema = JsonSchema.FromJsonAsync(schemaJson).GetAwaiter().GetResult();
        var errors = schema.Validate(rawJson);

        return errors.Select(e => new SchemaValidationError
        {
            JsonPath = e.Path ?? string.Empty,
            Constraint = e.Kind.ToString()
        }).ToList();
    }
}

/// <summary>
/// Configuration options for JsonSchemaConfigValidator.
/// </summary>
public sealed class JsonSchemaConfigValidatorOptions
{
    /// <summary>
    /// Absolute path to the schema file.
    /// </summary>
    public required string SchemaPath { get; init; }
}

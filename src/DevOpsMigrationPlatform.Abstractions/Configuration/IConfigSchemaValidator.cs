using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Configuration;

/// <summary>
/// Validates raw JSON configuration against a schema.
/// </summary>
public interface IConfigSchemaValidator
{
    /// <summary>
    /// Validates the given JSON string against the schema.
    /// </summary>
    /// <param name="rawJson">Raw JSON configuration text</param>
    /// <returns>List of validation errors; empty if valid</returns>
    IReadOnlyList<SchemaValidationError> Validate(string rawJson);
}

/// <summary>
/// Represents a single schema validation error.
/// </summary>
public sealed record SchemaValidationError
{
    /// <summary>JSON path to the invalid element (e.g. "#/modules/workitems/unknownKey")</summary>
    public required string JsonPath { get; init; }

    /// <summary>Description of the constraint that was violated</summary>
    public required string Constraint { get; init; }
}

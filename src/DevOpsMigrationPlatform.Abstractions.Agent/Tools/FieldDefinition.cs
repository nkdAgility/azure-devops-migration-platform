using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Describes a work item field as reported by the source or target system.</summary>
public sealed record FieldDefinition(
    string ReferenceName,
    string Name,
    string Type,
    bool IsReadOnly,
    IReadOnlyList<string>? AllowedValues);

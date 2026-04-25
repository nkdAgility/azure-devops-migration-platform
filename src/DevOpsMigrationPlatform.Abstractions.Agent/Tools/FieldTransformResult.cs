using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Output of applying all transforms to a set of work item fields.</summary>
public sealed record FieldTransformResult(
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<FieldTransformAction> Actions);

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Copies multiple fields in a single pass using a source → target mapping dictionary.
/// For each mapping, if the source field is absent the entry is skipped (no-op).
/// No default values are supported in batch mode.
/// </summary>
public sealed class CopyFieldBatchTransform : IFieldTransform
{
    private readonly IReadOnlyDictionary<string, string> _fieldMappings;
    private readonly string _groupName;

    public string Type => "CopyFieldBatch";
    public string Name { get; }

    public CopyFieldBatchTransform(
        string name,
        string groupName,
        IReadOnlyDictionary<string, string> fieldMappings)
    {
        Name = name;
        _groupName = groupName;
        _fieldMappings = fieldMappings;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;
        var actions = new List<FieldTransformAction>();

        foreach (var kvp in _fieldMappings)
        {
            var sourceField = kvp.Key;
            var targetField = kvp.Value;

            if (!fields.TryGetValue(sourceField, out var sourceValue))
                continue; // source absent: no-op for this mapping

            var oldTarget = updated.TryGetValue(targetField, out var ot) ? ot?.ToString() : null;
            updated[targetField] = sourceValue;
            actions.Add(new FieldTransformAction(_groupName, Name, Type, targetField, true, oldTarget, sourceValue?.ToString()));
        }

        return new FieldTransformResult(updated, actions);
    }
}
